using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Web;   // <<< NUEVO: para HttpContext

namespace Intranet.WorkflowStudio.Runtime
{
    public static class WorkflowRuntime
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        /// <summary>
        /// Reejecuta una instancia creando una NUEVA instancia con la misma definición y datos de entrada.
        /// (Esto es para "replay", no para reanudar human.tasks)
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

            // Crea una NUEVA instancia usando la misma definición y los mismos datos de entrada
            return await CrearInstanciaYEjecutarAsync(defId, datosEntrada, usuario);
        }

        // Info mínima para reanudar un flujo a partir de una tarea
        private class InfoInstanciaDesdeTarea
        {
            public long InstanciaId { get; set; }
            public int DefinicionId { get; set; }
            public string DatosEntradaJson { get; set; }
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
        i.DatosEntrada
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
                        DatosEntradaJson = dr.IsDBNull(2) ? null : dr.GetString(2)
                    };
                }
            }
        }

        /// <summary>
        /// Marca la WF_Tarea como Completada, guarda resultado y JSON de datos.
        /// </summary>
        private static void MarcarTareaCompletada(long tareaId, string resultado, string datosJson)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Tarea
SET    Estado      = @Estado,
       Resultado   = @Resultado,
       FechaCierre = GETDATE(),
       Datos       = @Datos
WHERE  Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = "Completada";
                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value =
                    (object)resultado ?? DBNull.Value;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value =
                    (object)datosJson ?? DBNull.Value;
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = tareaId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Crea una instancia de workflow, setea el seed (WF_SEED) y ejecuta el motor.
        /// Si el flujo termina normal => WF_Instancia.Estado = 'Finalizado'.
        /// Si el flujo se detiene (ej: human.task) => WF_Instancia queda 'EnCurso'.
        /// </summary>
        public static async Task<long> CrearInstanciaYEjecutarAsync(
            int defId,
            string datosEntradaJson,
            string usuario)
        {
            string jsonDef = CargarJsonDefinicion(defId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + defId);

            long instId = CrearInstancia(defId, datosEntradaJson, usuario);

            var wf = MotorDemo.FromJson(jsonDef);
            var logs = new List<string>();

            // ================== 1) Seed para el motor (WF_SEED) ==================
            // Esto permite que ContextoEjecucion tome wf.instanceId, input, etc.
            var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Datos de entrada como objeto (si vienen en JSON)
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
                // Limpio cualquier residuo previo de contexto
                items["WF_CTX_ESTADO"] = null;
            }

            // ================== 2) Acción de log para el motor ==================
            Action<string> logAction = s =>
            {
                logs.Add(s);

                // Si más adelante querés guardar en WF_InstanciaLog, acá es el lugar.
                // Por ahora, solo guardamos en memoria y luego en DatosContexto.
                // Si quisieras usar GuardarLog, algo como:
                //
                // string nodoId = null;
                // string nodoTipo = null;
                // try
                // {
                //     var it = HttpContext.Current?.Items;
                //     if (it != null && it["WF_CTX_ESTADO"] is IDictionary<string, object> est)
                //     {
                //         if (est.TryGetValue("wf.currentNodeId", out var nid)) nodoId = Convert.ToString(nid);
                //         if (est.TryGetValue("wf.currentNodeType", out var nt)) nodoTipo = Convert.ToString(nt);
                //     }
                // }
                // catch { }
                //
                // GuardarLog(instId, "Info", s, nodoId, nodoTipo);
            };

            // ================== 3) Ejecutar motor con handlers por defecto + SQL ==================
            var handlersExtra = new IManejadorNodo[] { new ManejadorSql() };

            await MotorDemo.EjecutarAsync(
                wf,
                logAction,
                handlersExtra,
                ct: CancellationToken.None
            );

            // ================== 4) Ver si el motor se detuvo (human.task u otro nodo Detener=true) ==================
            bool detenido = false;

            if (items != null && items["WF_CTX_ESTADO"] is IDictionary<string, object> estadoFinal)
            {
                if (estadoFinal.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                {
                    detenido = true;
                }
            }

            // Por ahora, guardamos solo logs en DatosContexto (igual que antes)
            string datosContexto =
                JsonConvert.SerializeObject(new { logs }, Formatting.None);

            if (detenido)
            {
                // La instancia queda EnCurso (no se marca Finalizado)
                ActualizarInstanciaEnCurso(instId, datosContexto);
            }
            else
            {
                // Flujo terminó normalmente
                CerrarInstanciaOk(instId, datosContexto);
            }

            return instId;
        }

        // ======================================================================
        //                      Helpers privados de Runtime
        // ======================================================================

        private static string CargarJsonDefinicion(int defId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(
                       "SELECT JsonDef FROM dbo.WF_Definicion WHERE Id=@Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", defId);
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? null : o.ToString();
            }
        }

        private static long CrearInstancia(int defId, string datos, string usuario)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Instancia
    (WF_DefinicionId, Estado, FechaInicio, DatosEntrada, CreadoPor)
VALUES
    (@DefId, 'EnCurso', GETDATE(), @Datos, @User);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datos ?? DBNull.Value;
                cmd.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value =
                    (object)usuario ?? "app";
                cn.Open();
                return (long)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// (Opcional) Insertar una línea en WF_InstanciaLog.
        /// Hoy no la estamos usando desde logAction, pero queda disponible.
        /// </summary>
        private static void GuardarLog(long instId, string nivel, string mensaje,
            string nodoId, string nodoTipo)
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

        /// <summary>
        /// La instancia sigue "EnCurso": actualizamos solo DatosContexto.
        /// (Se usa cuando el motor se detuvo por un human.task u otro nodo Detener = true)
        /// </summary>
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

        // Dentro de la clase WorkflowRuntime (namespace Intranet.WorkflowStudio.Runtime)

        //        public static async Task ReanudarDesdeTareaAsync(
        //            long tareaId,
        //            string resultado,
        //            string usuario,
        //            string datosJson = null)
        //        {
        //            long instId;
        //            int defId;
        //            string datosEntrada;

        //            // 1) Datos de la instancia + definición asociadas a la tarea
        //            using (var cn = new SqlConnection(Cnn))
        //            using (var cmd = cn.CreateCommand())
        //            {
        //                cmd.CommandText = @"
        //SELECT i.Id, i.WF_DefinicionId, i.DatosEntrada
        //FROM dbo.WF_Instancia i
        //JOIN dbo.WF_Tarea t ON t.WF_InstanciaId = i.Id
        //WHERE t.Id = @TareaId;";
        //                cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;

        //                await cn.OpenAsync().ConfigureAwait(false);
        //                using (var dr = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
        //                {
        //                    if (!await dr.ReadAsync().ConfigureAwait(false))
        //                        throw new InvalidOperationException("Tarea no encontrada: " + tareaId);

        //                    instId = dr.GetInt64(0);
        //                    defId = dr.GetInt32(1);
        //                    datosEntrada = dr.IsDBNull(2) ? null : dr.GetString(2);
        //                }
        //            }

        //            // 2) Marcar la tarea como completada
        //            MarcarTareaCompletada(tareaId, resultado, usuario, datosJson);

        //            // 3) Reanudar el motor sobre la MISMA instancia
        //            await ReanudarInstanciaAsync(instId, defId, datosEntrada, usuario);
        //        }

        /// <summary>
        /// Completa una tarea humana (WF_Tarea) y reanuda el workflow
        /// sobre la MISMA instancia (no crea otra).
        /// </summary>
        public static async Task ReanudarDesdeTareaAsync(
            long tareaId,
            string resultado,
            string datosJson,
            string usuario)
        {
            // 1) Info de instancia + definición
            var info = CargarInfoInstanciaPorTarea(tareaId);

            // 2) Marcar la tarea como completada
            MarcarTareaCompletada(tareaId, resultado, datosJson);

            // 3) Cargar JSON de definición
            string jsonDef = CargarJsonDefinicion(info.DefinicionId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + info.DefinicionId);

            var wf = MotorDemo.FromJson(jsonDef);
            var logs = new List<string>();

            // 4) Seed de contexto (igual que en CrearInstanciaYEjecutarAsync,
            //    pero usando la instancia EXISTENTE)
            var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(info.DatosEntradaJson))
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

            seed["wf.instanceId"] = info.InstanciaId;
            seed["wf.definicionId"] = info.DefinicionId;
            seed["wf.creadoPor"] = usuario ?? "app";

            // info específica de la tarea humana
            seed["humanTask.id"] = tareaId;
            seed["humanTask.result"] = resultado ?? "";

            if (!string.IsNullOrWhiteSpace(datosJson))
            {
                try
                {
                    seed["humanTask.data"] = JsonConvert.DeserializeObject<object>(datosJson);
                }
                catch
                {
                    seed["humanTask.dataRaw"] = datosJson;
                }
            }

            var items = HttpContext.Current?.Items;
            if (items != null)
            {
                items["WF_SEED"] = seed;
                items["WF_CTX_ESTADO"] = null;    // limpia cualquier contexto previo
            }

            // 5) Ejecutar el motor otra vez sobre la misma instancia
            Action<string> logAction = s => logs.Add(s);
            var handlersExtra = new IManejadorNodo[] { new ManejadorSql() };

            await MotorDemo.EjecutarAsync(
                wf,
                logAction,
                handlersExtra,
                ct: CancellationToken.None
            );

            // 6) Ver si el motor volvió a detenerse (otra human.task) o terminó
            bool detenido = false;

            if (items != null && items["WF_CTX_ESTADO"] is IDictionary<string, object> estadoFinal)
            {
                if (estadoFinal.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                {
                    detenido = true;
                }
            }

            string datosContexto =
                JsonConvert.SerializeObject(new { logs }, Formatting.None);

            if (detenido)
            {
                // la instancia sigue viva (EnCurso)
                ActualizarInstanciaEnCurso(info.InstanciaId, datosContexto);
            }
            else
            {
                // terminó normalmente
                CerrarInstanciaOk(info.InstanciaId, datosContexto);
            }
        }


        private static void MarcarTareaCompletada(
    long tareaId,
    string resultado,
    string usuario,
    string datosJson)
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
    Datos           = CASE 
                        WHEN @Datos IS NULL OR @Datos = '' THEN Datos 
                        ELSE @Datos 
                      END
WHERE Id = @Id;";
                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = (object)resultado ?? DBNull.Value;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 100).Value = (object)usuario ?? DBNull.Value;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosJson ?? DBNull.Value;
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = tareaId;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static async Task ReanudarInstanciaAsync(
            long instId,
            int defId,
            string datosEntradaJson,
            string usuario)
        {
            string jsonDef = CargarJsonDefinicion(defId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + defId);

            var wf = MotorDemo.FromJson(jsonDef);
            var logs = new List<string>();

            // Seed de contexto
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
            seed["wf.reanudadoPor"] = usuario ?? "app";

            var items = System.Web.HttpContext.Current?.Items;
            if (items != null)
            {
                items["WF_SEED"] = seed;
                items["WF_CTX_ESTADO"] = null;
            }

            Action<string> logAction = s =>
            {
                logs.Add(s);
                // si querés, acá después usamos GuardarLog(instId, "Info", s, nodoId, nodoTipo);
            };

            var handlersExtra = new IManejadorNodo[] { new ManejadorSql() };

            await MotorDemo.EjecutarAsync(
                wf,
                logAction,
                handlersExtra,
                ct: CancellationToken.None
            );

            bool detenido = false;
            if (items != null && items["WF_CTX_ESTADO"] is IDictionary<string, object> estadoFinal)
            {
                if (estadoFinal.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                {
                    detenido = true;
                }
            }

            string datosContexto =
                JsonConvert.SerializeObject(new { logs }, Formatting.None);

            if (detenido)
                ActualizarInstanciaEnCurso(instId, datosContexto);
            else
                CerrarInstanciaOk(instId, datosContexto);
        }

    }
}
