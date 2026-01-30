using Intranet.WorkflowStudio.WebForms;
using Intranet.WorkflowStudio.WebForms.DocumentProcessing;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;   // HttpContext

namespace Intranet.WorkflowStudio.Runtime
{
    public static class WorkflowRuntime
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        /// <summary>
        /// Reejecuta una instancia creando una NUEVA instancia con la misma definición y datos de entrada.
        /// (Replay, no reanudar human.tasks)
        /// </summary>
        public static async Task<long> ReejecutarInstanciaAsync(long instanciaId, string usuario)
        {
            int defId;
            string datosEntrada;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(
                "SELECT WF_DefinicionId, DatosEntrada FROM dbo.WF_Instancia WHERE Id=@Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", instanciaId);
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new InvalidOperationException("Instancia no encontrada: " + instanciaId);

                    defId = dr.GetInt32(0);
                    datosEntrada = dr.IsDBNull(1) ? null : dr.GetString(1);
                }
            }

            return await CrearInstanciaYEjecutarAsync(defId, datosEntrada, usuario);
        }

        // Info mínima para reanudar un flujo a partir de una tarea
        private class InfoInstanciaDesdeTarea
        {
            public long InstanciaId { get; set; }
            public int DefinicionId { get; set; }
            public string DatosEntradaJson { get; set; }
            public string NodoId { get; set; }
        }

        /// <summary>
        /// Dado el Id de una WF_Tarea, trae la instancia y la definición asociada.
        /// </summary>
        private static InfoInstanciaDesdeTarea CargarInfoInstanciaPorTarea(long tareaId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT  t.WF_InstanciaId,
        i.WF_DefinicionId,
        i.DatosEntrada,
        t.NodoId
FROM    dbo.WF_Tarea      t
JOIN    dbo.WF_Instancia  i ON i.Id = t.WF_InstanciaId
WHERE   t.Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = tareaId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new InvalidOperationException("Tarea no encontrada: " + tareaId);

                    return new InfoInstanciaDesdeTarea
                    {
                        InstanciaId = dr.GetInt64(0),
                        DefinicionId = dr.GetInt32(1),
                        DatosEntradaJson = dr.IsDBNull(2) ? null : dr.GetString(2),
                        NodoId = dr.IsDBNull(3) ? null : dr.GetString(3)
                    };
                }
            }
        }

        /// <summary>
        /// Marca la WF_Tarea como Completada, guarda resultado y JSON de datos.
        /// </summary>
        private static void MarcarTareaCompletada(long tareaId, string resultado, string datosJson)
        {
            MarcarTareaCompletada(tareaId, resultado, usuario: null, datosJson: datosJson);
        }

        public static async Task EjecutarInstanciaExistenteAsync(long instId, string usuario, CancellationToken ct)
        {
            int defId;
            string datosEntrada;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT WF_DefinicionId, DatosEntrada
FROM dbo.WF_Instancia
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instId;
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new InvalidOperationException("Instancia no encontrada: " + instId);

                    defId = dr.GetInt32(0);
                    datosEntrada = dr.IsDBNull(1) ? null : dr.GetString(1);
                }
            }

            string jsonDef = CargarJsonDefinicion(defId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + defId);

            var wf = WorkflowRunner.FromJson(jsonDef);
            var logs = new List<string>();

            // Seed
            var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(datosEntrada))
            {
                try
                {
                    var inputObj = JsonConvert.DeserializeObject<object>(datosEntrada);
                    seed["input"] = inputObj;
                }
                catch
                {
                    seed["inputRaw"] = datosEntrada;
                }
            }

            seed["wf.instanceId"] = instId;
            seed["wf.definicionId"] = defId;
            seed["wf.creadoPor"] = usuario ?? "app";

            // subflows
            seed["wf.depth"] = 0;
            seed["wf.callStack"] = new string[0];

            // RootInstanciaId/Depth desde tabla (si existe)
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand("SELECT RootInstanciaId, Depth FROM dbo.WF_Instancia WHERE Id=@Id", cn))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instId;
                    cn.Open();
                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            var root = dr.IsDBNull(0) ? (long?)null : dr.GetInt64(0);
                            var depth = dr.IsDBNull(1) ? (int?)null : dr.GetInt32(1);

                            if (root.HasValue) seed["wf.rootInstanceId"] = root.Value;
                            if (depth.HasValue) seed["wf.depth"] = depth.Value;
                        }
                    }
                }
            }
            catch { }

            var items = HttpContext.Current?.Items;
            if (items != null)
            {
                items["WF_SEED"] = seed;
                items["WF_CTX_ESTADO"] = null;
            }

            Action<string> logAction = s =>
            {
                logs.Add(s);
                try
                {
                    string nodoId = null;
                    string nodoTipo = null;

                    var it = HttpContext.Current?.Items;
                    if (it != null && it["WF_CTX_ESTADO"] is IDictionary<string, object> est)
                    {
                        if (est.TryGetValue("wf.currentNodeId", out var nid))
                            nodoId = Convert.ToString(nid);

                        if (est.TryGetValue("wf.currentNodeType", out var nt))
                            nodoTipo = Convert.ToString(nt);
                    }

                    GuardarLog(instId, "Info", s, nodoId, nodoTipo);
                }
                catch { }
            };

            var handlersExtra = new IManejadorNodo[]
            {
                new ManejadorSql(),
                new HParallel(),
                new HJoin(),
                new HUtilError(),
                new HUtilNotify(),
                new HFileRead(),
                new HFileWrite(),
                new HDocExtract(),
                new HControlDelay(),
                new HFtpPut(),
                new HEmailSend(),
                new HQueuePublishSql(),
                new HQueueConsumeSql(),
                new HDocLoad(),
                new HDocTipoResolve(),
                new HChatNotify(),
                new HControlRetry(),
                new HSubflow()
            };

            await WorkflowRunner.EjecutarAsync(
                wf,
                logAction,
                handlers: handlersExtra,
                ct: ct
            );

            PersistirFinal(instId, logs);
        }

        /// <summary>
        /// Crea una instancia, setea WF_SEED y ejecuta el motor.
        /// Finalizado/Error/EnCurso según estado publicado por el motor.
        /// </summary>
        public static async Task<long> CrearInstanciaYEjecutarAsync(int defId, string datosEntradaJson, string usuario)
        {
            string jsonDef = CargarJsonDefinicion(defId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + defId);

            long instId = CrearInstancia(defId, datosEntradaJson, usuario);

            var wf = WorkflowRunner.FromJson(jsonDef);
            var logs = new List<string>();

            var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(datosEntradaJson))
            {
                try
                {
                    var inputObj = JsonConvert.DeserializeObject<object>(datosEntradaJson);
                    seed["input"] = inputObj;
                }
                catch
                {
                    seed["inputRaw"] = datosEntradaJson;
                }
            }

            seed["wf.instanceId"] = instId;
            seed["wf.definicionId"] = defId;
            seed["wf.creadoPor"] = usuario ?? "app";

            var items = HttpContext.Current?.Items;
            if (items != null)
            {
                items["WF_SEED"] = seed;
                items["WF_CTX_ESTADO"] = null;
            }

            Action<string> logAction = s =>
            {
                logs.Add(s);
                try
                {
                    string nodoId = null;
                    string nodoTipo = null;

                    var it = HttpContext.Current?.Items;
                    if (it != null && it["WF_CTX_ESTADO"] is IDictionary<string, object> est)
                    {
                        if (est.TryGetValue("wf.currentNodeId", out var nid))
                            nodoId = Convert.ToString(nid);

                        if (est.TryGetValue("wf.currentNodeType", out var nt))
                            nodoTipo = Convert.ToString(nt);
                    }

                    GuardarLog(instId, "Info", s, nodoId, nodoTipo);
                }
                catch { }
            };

            var handlersExtra = new IManejadorNodo[]
            {
                new ManejadorSql(),
                new HParallel(),
                new HJoin(),
                new HUtilError(),
                new HUtilNotify(),
                new HFileRead(),
                new HFileWrite(),
                new HDocExtract(),
                new HControlDelay(),
                new HFtpPut(),
                new HEmailSend(),
                new HChatNotify(),
                new HQueuePublishSql(),
                new HQueueConsumeSql(),
                new HDocLoad(),
                new HDocTipoResolve(),
                new HControlRetry(),
                new HSubflow()
            };

            await WorkflowRunner.EjecutarAsync(
                wf,
                logAction,
                handlers: handlersExtra,
                ct: CancellationToken.None
            );

            PersistirFinal(instId, logs);
            return instId;
        }

        // ======================================================================
        //                      Persistencia final (UNIFICADA)
        // ======================================================================
        private static void PersistirFinal(long instId, List<string> logs)
        {
            var items = HttpContext.Current?.Items;

            bool detenido = false;
            bool hayError = false;
            string mensajeError = null;

            if (items != null && items["WF_CTX_ESTADO"] is IDictionary<string, object> estadoFinal)
            {
                if (estadoFinal.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                    detenido = true;

                if (estadoFinal.TryGetValue("wf.error", out var errVal) &&
                    ContextoEjecucion.ToBool(errVal))
                    hayError = true;

                if (estadoFinal.TryGetValue("wf.error.message", out var msgVal))
                    mensajeError = Convert.ToString(msgVal);

                string datosContexto = ConstruirDatosContextoJson(logs, estadoFinal, mensajeError);

                if (detenido) ActualizarInstanciaEnCurso(instId, datosContexto);
                else if (hayError) MarcarInstanciaError(instId, datosContexto);
                else CerrarInstanciaOk(instId, datosContexto);

                return;
            }

            // fallback sin estado
            string fallback = JsonConvert.SerializeObject(
                (mensajeError != null)
                    ? (object)new { logs, error = new { message = mensajeError } }
                    : (object)new { logs },
                Formatting.None
            );

            // si no hay estado, asumimos finalizado OK (si querés, lo podemos dejar EnCurso)
            CerrarInstanciaOk(instId, fallback);
        }

        // ======================================================================
        //                      Helpers privados de Runtime
        // ======================================================================

        private static string CargarJsonDefinicion(int defId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("SELECT JsonDef FROM dbo.WF_Definicion WHERE Id=@Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", defId);
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? null : o.ToString();
            }
        }

        private static long CrearInstancia(int defId, string datos, string usuario)
        {
            string procesoKey = null;
            string scopeKey = null;
            int? tenantId = null;
            int? docTipoId = null;

            if (!string.IsNullOrWhiteSpace(datos))
            {
                try
                {
                    var jo = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(datos);
                    if (jo != null)
                    {
                        procesoKey = (string)jo["procesoKey"];
                        scopeKey = (string)jo["scopeKey"];

                        var t = jo["tenantId"];
                        if (t != null && int.TryParse(t.ToString(), out var xt)) tenantId = xt;

                        var d = jo["docTipoId"];
                        if (d != null && int.TryParse(d.ToString(), out var xd)) docTipoId = xd;
                    }
                }
                catch { }
            }

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Instancia
    (WF_DefinicionId, Estado, FechaInicio, DatosEntrada, CreadoPor,
     ProcesoKey, ScopeKey, TenantId, DocTipoId)
VALUES
    (@DefId, 'EnCurso', GETDATE(), @Datos, @User,
     @ProcesoKey, @ScopeKey, @TenantId, @DocTipoId);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datos ?? DBNull.Value;
                cmd.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = (object)usuario ?? "app";

                cmd.Parameters.Add("@ProcesoKey", SqlDbType.NVarChar, 50).Value = (object)procesoKey ?? DBNull.Value;
                cmd.Parameters.Add("@ScopeKey", SqlDbType.NVarChar, 100).Value = (object)scopeKey ?? DBNull.Value;
                cmd.Parameters.Add("@TenantId", SqlDbType.Int).Value = (object)tenantId ?? DBNull.Value;
                cmd.Parameters.Add("@DocTipoId", SqlDbType.Int).Value = (object)docTipoId ?? DBNull.Value;

                cn.Open();
                return (long)cmd.ExecuteScalar();
            }
        }

        private static void GuardarLog(long instId, string nivel, string mensaje, string nodoId, string nodoTipo)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_InstanciaLog
    (WF_InstanciaId, FechaLog, Nivel, Mensaje, NodoId, NodoTipo)
VALUES
    (@InstId, GETDATE(), @Nivel, @Msg, @NodoId, @NodoTipo);", cn))
            {
                cmd.Parameters.AddWithValue("@InstId", instId);
                cmd.Parameters.AddWithValue("@Nivel", (object)nivel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Msg", (object)mensaje ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NodoId", (object)nodoId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NodoTipo", (object)nodoTipo ?? DBNull.Value);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void ActualizarInstanciaEnCurso(long instId, string datosContexto)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Instancia
SET Estado = 'EnCurso',
    DatosContexto = @Ctx
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Ctx", (object)datosContexto ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void CerrarInstanciaOk(long instId, string datosContexto)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Instancia
SET Estado = 'Finalizado',
    FechaFin = GETDATE(),
    DatosContexto = @Ctx
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Ctx", (object)datosContexto ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void MarcarInstanciaError(long instId, string datosContexto)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Instancia
SET Estado       = 'Error',
    FechaFin     = ISNULL(FechaFin, GETDATE()),
    DatosContexto = @Ctx
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Ctx", (object)datosContexto ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // ======================================================================
        //                      Reanudar desde tarea (MISMA instancia)
        // ======================================================================
        public static async Task ReanudarDesdeTareaAsync(long tareaId, string resultado, string datosJson, string usuario)
        {
            var info = CargarInfoInstanciaPorTarea(tareaId);

            MarcarTareaCompletada(tareaId, resultado, usuario, datosJson);

            string jsonDef = CargarJsonDefinicion(info.DefinicionId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + info.DefinicionId);

            var wf = WorkflowRunner.FromJson(jsonDef);

            string CalcularSiguienteNodo(WorkflowDef w, string fromNodeId)
            {
                if (w == null || string.IsNullOrWhiteSpace(fromNodeId) || w.Edges == null) return null;

                var salientes = w.Edges
                    .Where(e => string.Equals(e.From, fromNodeId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (salientes.Count == 0) return null;

                var e1 = salientes.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase))
                         ?? salientes[0];

                return e1.To;
            }

            var logs = new List<string>();

            // Seed base desde snapshot
            var seed = CargarSeedDesdeDatosContexto(info.InstanciaId);

            // fallback input
            if (!seed.ContainsKey("input") && !string.IsNullOrWhiteSpace(info.DatosEntradaJson))
            {
                try
                {
                    var inputObj = JsonConvert.DeserializeObject<object>(info.DatosEntradaJson);
                    seed["input"] = inputObj;
                }
                catch
                {
                    seed["inputRaw"] = info.DatosEntradaJson;
                }
            }

            // identidades mínimas
            seed["wf.instanceId"] = info.InstanciaId;
            seed["wf.definicionId"] = info.DefinicionId;
            seed["wf.reanudadoPor"] = usuario ?? "app";

            // inyección técnica (compatibilidad)
            seed["humanTask.id"] = tareaId;
            seed["humanTask.result"] = resultado ?? "";

            seed["wf.tarea.id"] = tareaId;
            seed["wf.tarea.resultado"] = resultado ?? "";

            if (!string.IsNullOrWhiteSpace(info.NodoId))
            {
                seed[$"wf.tarea.{info.NodoId}.resultado"] = resultado ?? "";

                if (seed.TryGetValue("wf.tarea.datos", out var datosNodo) && datosNodo != null)
                    seed[$"wf.tarea.{info.NodoId}.datos"] = datosNodo;

                if (seed.TryGetValue("wf.tarea.datosRaw", out var datosRaw) && datosRaw != null)
                    seed[$"wf.tarea.{info.NodoId}.datosRaw"] = datosRaw;
            }


            seed["tarea.id"] = tareaId;
            seed["tarea.resultado"] = resultado ?? "";

            if (!string.IsNullOrWhiteSpace(datosJson))
            {
                try
                {
                    var datosObj = JsonConvert.DeserializeObject<object>(datosJson);

                    seed["humanTask.data"] = datosObj;
                    seed["wf.tarea.datos"] = datosObj;
                    seed["tarea.datos"] = datosObj;
                }
                catch
                {
                    seed["humanTask.dataRaw"] = datosJson;
                    seed["wf.tarea.datosRaw"] = datosJson;
                    seed["tarea.datosRaw"] = datosJson;
                }
            }

            // ✅ CONTRATO BIZ (genérico, estable)
            seed["biz.task.id"] = tareaId;
            seed["biz.task.result"] = resultado ?? "";
            if (!string.IsNullOrWhiteSpace(datosJson))
            {
                try
                {
                    var datosObj = JsonConvert.DeserializeObject<object>(datosJson);
                    seed["biz.task.data"] = datosObj;
                }
                catch
                {
                    seed["biz.task.dataRaw"] = datosJson;
                }
            }

            // start override
            var resumeFrom = CalcularSiguienteNodo(wf, info.NodoId);
            if (!string.IsNullOrWhiteSpace(resumeFrom))
            {
                seed["wf.startNodeIdOverride"] = resumeFrom;
                seed["wf.resume.taskNodeId"] = info.NodoId;
                seed["wf.resume.fromNodeId"] = resumeFrom;
            }

            // limpieza flags
            seed["wf.detener"] = false;
            seed.Remove("wf.currentNodeId");
            seed.Remove("wf.currentNodeType");
            seed["wf.error"] = false;
            seed.Remove("wf.error.message");

            var items = HttpContext.Current?.Items;
            if (items != null)
            {
                items["WF_SEED"] = seed;
                items["WF_CTX_ESTADO"] = null;
            }

            Action<string> logAction = s =>
            {
                logs.Add(s);
                try
                {
                    string nodoId = null;
                    string nodoTipo = null;

                    var it = HttpContext.Current?.Items;
                    if (it != null && it["WF_CTX_ESTADO"] is IDictionary<string, object> est)
                    {
                        if (est.TryGetValue("wf.currentNodeId", out var nid))
                            nodoId = Convert.ToString(nid);

                        if (est.TryGetValue("wf.currentNodeType", out var nt))
                            nodoTipo = Convert.ToString(nt);
                    }

                    GuardarLog(info.InstanciaId, "Info", s, nodoId, nodoTipo);
                }
                catch { }
            };

            var handlersExtra = new IManejadorNodo[]
            {
                new ManejadorSql(),
                new HParallel(),
                new HJoin(),
                new HUtilError(),
                new HUtilNotify(),
                new HFileRead(),
                new HFileWrite(),
                new HDocExtract(),
                new HControlDelay(),
                new HFtpPut(),
                new HEmailSend(),
                new HChatNotify(),
                new HQueuePublishSql(),
                new HQueueConsumeSql(),
                new HDocLoad(),
                new HDocTipoResolve(),
                new HControlRetry(),
                new HSubflow()
            };

            await WorkflowRunner.EjecutarAsync(
                wf,
                logAction,
                handlers: handlersExtra,
                ct: CancellationToken.None
            );

            PersistirFinal(info.InstanciaId, logs);
        }

        private static void MarcarTareaCompletada(long tareaId, string resultado, string usuario, string datosJson)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_Tarea
SET Estado          = 'Completada',
    Resultado       = @Resultado,
    UsuarioAsignado = COALESCE(UsuarioAsignado, @Usuario),
    FechaCierre     = GETDATE(),
    Datos           = CASE WHEN @Datos IS NULL OR @Datos = '' THEN Datos ELSE @Datos END
WHERE Id = @Id
  AND Estado NOT IN ('Completada','Cancelada');";

                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = (object)resultado ?? DBNull.Value;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 100).Value = (object)usuario ?? DBNull.Value;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosJson ?? DBNull.Value;
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = tareaId;

                cn.Open();
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    throw new InvalidOperationException("WF_Tarea ya fue completada o cancelada por otro usuario.");
            }
        }

        private static string CargarDatosContextoInstancia(long instId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT DatosContexto
FROM dbo.WF_Instancia
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instId;
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? null : Convert.ToString(o);
            }
        }

        // ======================================================================
        //                      BIZ SNAPSHOT HELPERS
        // ======================================================================
        private static Dictionary<string, object> ExtraerBizDesdeEstado(IDictionary<string, object> estadoFinal)
        {
            var biz = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (estadoFinal == null) return biz;

            foreach (var kv in estadoFinal)
            {
                if (kv.Key != null && kv.Key.StartsWith("biz.", StringComparison.OrdinalIgnoreCase))
                    biz[kv.Key] = kv.Value;
            }
            return biz;
        }

        private static string ConstruirDatosContextoJson(List<string> logs, IDictionary<string, object> estadoFinal, string mensajeError)
        {
            var biz = ExtraerBizDesdeEstado(estadoFinal);

            object payload = (!string.IsNullOrWhiteSpace(mensajeError))
                ? (object)new { logs, error = new { message = mensajeError }, estado = estadoFinal, biz }
                : (object)new { logs, estado = estadoFinal, biz };

            return JsonConvert.SerializeObject(payload, Formatting.None);
        }

        private static Dictionary<string, object> CargarSeedDesdeDatosContexto(long instId)
        {
            var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var json = CargarDatosContextoInstancia(instId);
                if (string.IsNullOrWhiteSpace(json)) return seed;

                var root = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                if (root == null) return seed;

                var estadoTok = root["estado"];
                if (estadoTok == null) return seed;

                var dict = estadoTok.ToObject<Dictionary<string, object>>();
                if (dict == null) return seed;

                foreach (var kv in dict)
                    seed[kv.Key] = kv.Value;

                return seed;
            }
            catch
            {
                return seed;
            }
        }
    }
}
