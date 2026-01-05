using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;

namespace Api
{
    public class Generico : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";

            var action = (ctx.Request["action"] ?? "").ToLowerInvariant();

            switch (action)
            {
                case "doctipo.list":
                    ListarDocTipos(ctx);
                    break;

                case "doctipo.rules":
                    ObtenerRulesDocTipo(ctx);
                    break;
                // NUEVO
                case "doctipo.reglas.list":
                    ListarReglasExtract(ctx);
                    break;

                // NUEVO (POST JSON)
                case "doctipo.reglas.save":
                    GuardarReglaExtract(ctx);
                    break;

                // NUEVO (POST JSON)
                case "doctipo.reglas.test":
                    ProbarReglaExtract(ctx);
                    break;
                case "dashboard.kpis":
                    DashboardKpis(ctx);
                    break;
                case "worker.escalamiento.run":
                    WorkerEscalamientoRun(ctx);
                    break;
                case "tarea.historial":
                    TareaHistorial(ctx);
                    break;
                case "instancia.escalamiento.historial":
                    InstanciaEscalamientoHistorial(ctx);
                    break;
                default:
                    ctx.Response.Write("{\"error\":\"acción no soportada\"}");
                    break;
            }
        }

        private void InstanciaEscalamientoHistorial(HttpContext ctx)
        {
            var raw = (ctx.Request["instanciaId"] ?? "").Trim();
            if (!long.TryParse(raw, out var instanciaId) || instanciaId <= 0)
            {
                WriteJson(ctx, new { ok = false, error = "instanciaId inválido" });
                return;
            }

            var items = new List<object>();

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
;WITH Base AS (
    SELECT
        t.Id,
        t.WF_InstanciaId,
        t.OrigenTareaId,
        t.Titulo,
        t.RolDestino,
        t.Estado,
        t.Resultado,
        t.FechaCreacion,
        t.FechaCierre,
        t.Datos,
        JSON_QUERY(ISNULL(t.Datos,'{}'), '$.origenEscalamiento') AS OrigenEscObj
    FROM dbo.WF_Tarea t WITH (READPAST)
    WHERE t.WF_InstanciaId = @InstanciaId
),
Roots AS (
    -- raíces = tareas que tienen al menos un hijo
    SELECT b.Id
    FROM Base b
    WHERE EXISTS (SELECT 1 FROM Base c WHERE c.OrigenTareaId = b.Id)
),
CTE AS (
    -- anchor: raíces
    SELECT
        b.Id,
        b.OrigenTareaId,
        b.Titulo,
        b.RolDestino,
        b.Estado,
        b.Resultado,
        b.FechaCreacion,
        b.FechaCierre,
        b.Datos,
        b.OrigenEscObj,
        b.Id AS RootId,
        CAST(0 AS int) AS Nivel
    FROM Base b
    INNER JOIN Roots r ON r.Id = b.Id

    UNION ALL

    -- recursivo: hijos
    SELECT
        c.Id,
        c.OrigenTareaId,
        c.Titulo,
        c.RolDestino,
        c.Estado,
        c.Resultado,
        c.FechaCreacion,
        c.FechaCierre,
        c.Datos,
        c.OrigenEscObj,
        p.RootId,
        p.Nivel + 1
    FROM Base c
    INNER JOIN CTE p ON c.OrigenTareaId = p.Id
)
SELECT
    Id, OrigenTareaId, RootId, Nivel,
    Titulo, RolDestino, Estado, Resultado,
    CONVERT(varchar(19), FechaCreacion, 120) AS FechaCreacion,
    CONVERT(varchar(19), FechaCierre, 120) AS FechaCierre,
    OrigenEscObj
FROM CTE
ORDER BY RootId DESC, Nivel ASC, FechaCreacion ASC, Id ASC
OPTION (MAXRECURSION 100);";

                cmd.Parameters.AddWithValue("@InstanciaId", instanciaId);

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string origenObj = dr.IsDBNull(10) ? null : dr.GetString(10);

                        items.Add(new
                        {
                            id = dr.GetInt64(0),
                            origenTareaId = dr.IsDBNull(1) ? (long?)null : dr.GetInt64(1),
                            rootId = dr.GetInt64(2),
                            nivel = dr.GetInt32(3),
                            titulo = dr.IsDBNull(4) ? "" : dr.GetString(4),
                            rolDestino = dr.IsDBNull(5) ? "" : dr.GetString(5),
                            estado = dr.IsDBNull(6) ? "" : dr.GetString(6),
                            resultado = dr.IsDBNull(7) ? null : dr.GetString(7),
                            fechaCreacion = dr.IsDBNull(8) ? "" : dr.GetString(8),
                            fechaCierre = dr.IsDBNull(9) ? null : dr.GetString(9),
                            origenEscalamientoObj = string.IsNullOrWhiteSpace(origenObj) ? null : (object)origenObj
                        });
                    }
                }
            }

            // OJO: origenEscalamientoObj viene como string JSON -> lo devolvemos tal cual,
            // el JS lo intenta JSON.parse al renderizar (si preferís lo parseo acá con JObject).
            WriteJson(ctx, new { ok = true, instanciaId = instanciaId, items = items });
        }




        private void TareaHistorial(HttpContext ctx)
        {
            var raw = (ctx.Request["tareaId"] ?? "").Trim();
            long tareaId = 0;
            long.TryParse(raw, out tareaId);

            if (tareaId <= 0)
            {
                WriteJson(ctx, new { ok = false, error = "tareaId inválido" });
                return;
            }

            var items = new List<object>();

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand("dbo.WF_Tarea_Historial", cn))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@TareaId", tareaId);

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        // columnas según el SELECT del SP
                        items.Add(new
                        {
                            id = Convert.ToInt64(dr["Id"]),
                            wfInstanciaId = Convert.ToInt64(dr["WF_InstanciaId"]),
                            titulo = Convert.ToString(dr["Titulo"]),
                            rolDestino = Convert.ToString(dr["RolDestino"]),
                            estado = Convert.ToString(dr["Estado"]),
                            resultado = Convert.ToString(dr["Resultado"]),
                            fechaCreacion = Convert.ToString(dr["FechaCreacion"]),
                            fechaCierre = Convert.ToString(dr["FechaCierre"]),
                            origenTareaId = dr["OrigenTareaId"] == DBNull.Value ? (long?)null : Convert.ToInt64(dr["OrigenTareaId"]),
                            nivel = Convert.ToInt32(dr["Nivel"]),
                            origenEscalamiento = dr["OrigenEscalamientoObj"] == DBNull.Value ? null : Convert.ToString(dr["OrigenEscalamientoObj"])
                        });
                    }
                }
            }

            WriteJson(ctx, new { ok = true, tareaId = tareaId, historial = items });
        }

        private string ResolverRolEscalado(SqlConnection cn, string rolActual, string scopeKey)
        {
            if (cn == null) throw new ArgumentNullException(nameof(cn));
            if (string.IsNullOrWhiteSpace(rolActual)) return null;

            // 1) Traer roleMap desde WF_Setting
            //    Preferimos un override por ScopeKey si viene, y si no, el global (ScopeKey NULL).
            string json = null;

            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP (1) Value
FROM dbo.WF_Setting WITH (READPAST)
WHERE Activo = 1
  AND SettingKey = 'wf.escalamiento.roleMap'
  AND (
        (@ScopeKey IS NOT NULL AND ScopeKey = @ScopeKey)
     OR (ScopeKey IS NULL)
  )
ORDER BY CASE WHEN ScopeKey IS NULL THEN 1 ELSE 0 END ASC;";  // primero el específico, después global

                cmd.Parameters.AddWithValue("@ScopeKey",
                    string.IsNullOrWhiteSpace(scopeKey) ? (object)DBNull.Value : scopeKey);

                json = Convert.ToString(cmd.ExecuteScalar());
            }

            if (string.IsNullOrWhiteSpace(json))
                return null;

            // 2) Parse JSON a diccionario case-insensitive
            //    Ej: { "RRHH":"GERENCIA" }
            try
            {
                var map = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                          ?? new Dictionary<string, string>();

                // case-insensitive lookup
                foreach (var kv in map)
                {
                    if (string.Equals(kv.Key?.Trim(), rolActual.Trim(), StringComparison.OrdinalIgnoreCase))
                        return (kv.Value ?? "").Trim();
                }

                return null;
            }
            catch
            {
                // JSON inválido en setting
                return null;
            }
        }


        private void WorkerEscalamientoRun(HttpContext ctx)
        {
            int processed = 0;
            long msgId = 0;
            string payloadJson = null;

            long tareaId = 0;
            long instanciaId = 0;
            string rolDestino = null;
            string scopeKey = null;
            string titulo = null;
            string fechaVenc = null;
            string escaladoEn = null;

            string nuevoRol = null;
            long tareaNuevaId = 0;
            bool escaladoReal = false;
            bool alreadyExisted = false;

            bool notifEnqueued = false;
            bool queueReverted = false;

            string error = null;

            // ✅ MODO TEST: si viene tareaId, consumimos sólo ese mensaje (CorrelationId)
            string tareaIdParamRaw = (ctx.Request["tareaId"] ?? "").Trim();
            long tareaIdTarget = 0;
            long.TryParse(tareaIdParamRaw, out tareaIdTarget);

            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            {
                cn.Open();

                // 1) Tomar 1 mensaje de WF_Queue (ATÓMICO)
                using (var tx = cn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                using (var cmd = cn.CreateCommand())
                {
                    cmd.Transaction = tx;

                    if (tareaIdTarget > 0)
                    {
                        cmd.CommandText = @"
;WITH cte AS (
    SELECT TOP (1)
        Id, Payload, Processed, ProcessedAt, Attempts, LastError
    FROM dbo.WF_Queue WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE Queue = @Queue
      AND Processed = 0
      AND CorrelationId = @CorrelationId
      AND (DueAt IS NULL OR DueAt <= GETDATE())
    ORDER BY Priority DESC, CreatedAt DESC, Id DESC
)
UPDATE cte
   SET Processed = 1,
       ProcessedAt = GETDATE(),
       Attempts = Attempts + 1,
       LastError = NULL
OUTPUT inserted.Id, inserted.Payload;";
                        cmd.Parameters.AddWithValue("@Queue", "wf.escalamiento");
                        cmd.Parameters.AddWithValue("@CorrelationId", tareaIdTarget.ToString());
                    }
                    else
                    {
                        cmd.CommandText = @"
;WITH cte AS (
    SELECT TOP (1)
        Id, Payload, Processed, ProcessedAt, Attempts, LastError
    FROM dbo.WF_Queue WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE Queue = @Queue
      AND Processed = 0
      AND (DueAt IS NULL OR DueAt <= GETDATE())
    ORDER BY Priority DESC, CreatedAt, Id
)
UPDATE cte
   SET Processed = 1,
       ProcessedAt = GETDATE(),
       Attempts = Attempts + 1,
       LastError = NULL
OUTPUT inserted.Id, inserted.Payload;";
                        cmd.Parameters.AddWithValue("@Queue", "wf.escalamiento");
                    }

                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            msgId = Convert.ToInt64(dr[0]);
                            payloadJson = dr.IsDBNull(1) ? null : Convert.ToString(dr[1]);
                        }
                    }

                    tx.Commit();
                }

                if (msgId == 0)
                {
                    WriteJson(ctx, new
                    {
                        ok = true,
                        processed = 0,
                        message = (tareaIdTarget > 0)
                            ? ("No hay mensajes en wf.escalamiento para tareaId=" + tareaIdTarget)
                            : "No hay mensajes en wf.escalamiento"
                    });
                    return;
                }

                // Helper local: revertir mensaje de cola para reintento
                Action<string> RevertQueue = (string err) =>
                {
                    try
                    {
                        using (var cmd = cn.CreateCommand())
                        {
                            cmd.CommandText = @"
UPDATE dbo.WF_Queue
   SET Processed = 0,
       ProcessedAt = NULL,
       LastError = @Err
 WHERE Id = @Id;";
                            cmd.Parameters.AddWithValue("@Err", (object)(err ?? "") ?? "");
                            cmd.Parameters.AddWithValue("@Id", msgId);
                            cmd.ExecuteNonQuery();
                        }
                        queueReverted = true;
                    }
                    catch
                    {
                        // si esto falla, no rompemos la respuesta del API
                    }
                };

                // 2) Parse payload
                try
                {
                    var j = JObject.Parse(payloadJson ?? "{}");

                    long.TryParse(Convert.ToString(j["tareaId"] ?? ""), out tareaId);
                    long.TryParse(Convert.ToString(j["instanciaId"] ?? ""), out instanciaId);

                    rolDestino = Convert.ToString(j["rolDestino"] ?? "");
                    scopeKey = Convert.ToString(j["scopeKey"] ?? "");
                    titulo = Convert.ToString(j["titulo"] ?? "");
                    fechaVenc = Convert.ToString(j["fechaVencimiento"] ?? "");
                    escaladoEn = Convert.ToString(j["escaladoEn"] ?? "");
                }
                catch (Exception ex)
                {
                    error = "Payload inválido: " + ex.Message;
                    RevertQueue(error);
                    WriteJson(ctx, new
                    {
                        ok = false,
                        processed = 0,
                        msgId,
                        error,
                        queueReverted
                    });
                    return;
                }

                if (tareaId <= 0)
                {
                    error = "Payload sin tareaId válido.";
                    RevertQueue(error);
                    WriteJson(ctx, new
                    {
                        ok = false,
                        processed = 0,
                        msgId,
                        tareaId,
                        error,
                        queueReverted
                    });
                    return;
                }

                // 3) ESCALAMIENTO REAL (primero)  ✅
                try
                {
                    nuevoRol = ResolverRolEscalado(cn, rolDestino, scopeKey);

                    if (string.IsNullOrWhiteSpace(nuevoRol))
                    {
                        error = "No hay rol de escalamiento para rolDestino='" + (rolDestino ?? "") + "'";
                        RevertQueue(error);
                    }
                    else
                    {
                        // ya existía?
                        using (var cmdChk = cn.CreateCommand())
                        {
                            cmdChk.CommandText = @"
SELECT TOP (1) Id
FROM dbo.WF_Tarea WITH (READPAST)
WHERE OrigenTareaId = @Orig
ORDER BY Id DESC;";
                            cmdChk.Parameters.AddWithValue("@Orig", tareaId);
                            var x = cmdChk.ExecuteScalar();
                            alreadyExisted = (x != null && x != DBNull.Value);
                        }

                        using (var cmdEsc = cn.CreateCommand())
                        {
                            cmdEsc.CommandText = "dbo.WF_Tarea_Escalar_CrearNueva";
                            cmdEsc.CommandType = System.Data.CommandType.StoredProcedure;

                            cmdEsc.Parameters.AddWithValue("@TareaIdOriginal", tareaId);
                            cmdEsc.Parameters.AddWithValue("@NuevoRolDestino", nuevoRol);
                            cmdEsc.Parameters.AddWithValue("@Motivo", "SLA vencido");
                            cmdEsc.Parameters.AddWithValue("@Usuario", "system");

                            using (var dr = cmdEsc.ExecuteReader())
                            {
                                if (dr.Read())
                                {
                                    if (!dr.IsDBNull(1)) tareaNuevaId = Convert.ToInt64(dr[1]);
                                }
                            }
                        }

                        escaladoReal = (tareaNuevaId > 0);

                        if (!escaladoReal)
                        {
                            error = "Escalamiento ejecutado pero no devolvió TareaNuevaId.";
                            RevertQueue(error);
                        }
                    }
                }
                catch (Exception exEsc)
                {
                    error = "Error escalamiento real: " + exEsc.Message;
                    RevertQueue(error);
                }

                // Si falló escalamiento real, no encolamos notificación (así no duplicás notifs)
                if (!string.IsNullOrWhiteSpace(error))
                {
                    WriteJson(ctx, new
                    {
                        ok = false,
                        processed = 0,
                        consumedQueue = "wf.escalamiento",
                        producedQueue = "wf.notificaciones",
                        msgId,
                        tareaId,
                        instanciaId,
                        rolDestino,
                        nuevoRol,
                        escaladoReal,
                        tareaNuevaId,
                        alreadyExisted,
                        error,
                        queueReverted
                    });
                    return;
                }

                // 4) Encolar notificación (idempotente por msgId) ✅
                try
                {
                    var notifPayload =
                        "{"
                        + "\"tipo\":\"SLA_ESCALADO\","
                        + "\"msgId\":" + msgId + ","
                        + "\"tareaId\":\"" + tareaId + "\","
                        + "\"instanciaId\":\"" + instanciaId + "\","
                        + "\"rolDestino\":\"" + (rolDestino ?? "") + "\","
                        + "\"scopeKey\":\"" + (scopeKey ?? "") + "\","
                        + "\"titulo\":\"" + (titulo ?? "") + "\","
                        + "\"fechaVencimiento\":\"" + (fechaVenc ?? "") + "\","
                        + "\"escaladoEn\":\"" + (escaladoEn ?? "") + "\""
                        + "}";

                    using (var cmd2 = cn.CreateCommand())
                    {
                        cmd2.CommandText = @"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.WF_Queue WITH (READPAST)
    WHERE Queue = @Queue
      AND CorrelationId = @CorrelationId
      AND ISJSON(Payload) = 1
      AND JSON_VALUE(Payload,'$.tipo') = 'SLA_ESCALADO'
      AND JSON_VALUE(Payload,'$.msgId') = @MsgId
)
BEGIN
    INSERT INTO dbo.WF_Queue
        (Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, Attempts)
    VALUES
        (@Queue, @CorrelationId, @Payload, GETDATE(), NULL, @Priority, 0, 0);
END";

                        cmd2.Parameters.AddWithValue("@Queue", "wf.notificaciones");
                        cmd2.Parameters.AddWithValue("@CorrelationId", (object)tareaId);
                        cmd2.Parameters.AddWithValue("@Payload", (object)notifPayload ?? "{}");
                        cmd2.Parameters.AddWithValue("@Priority", 5);
                        cmd2.Parameters.AddWithValue("@MsgId", msgId.ToString());

                        cmd2.ExecuteNonQuery();
                        notifEnqueued = true;
                    }
                }
                catch (Exception exNotif)
                {
                    // Si falla la notificación, NO revertimos escalamiento real.
                    // Pero sí dejamos LastError en el mensaje (para que quede auditado).
                    try
                    {
                        using (var cmdErr = cn.CreateCommand())
                        {
                            cmdErr.CommandText = @"
UPDATE dbo.WF_Queue
   SET LastError = @Err
 WHERE Id = @Id;";
                            cmdErr.Parameters.AddWithValue("@Err", "Error notificación: " + exNotif.Message);
                            cmdErr.Parameters.AddWithValue("@Id", msgId);
                            cmdErr.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }

                processed = 1;
            }

            WriteJson(ctx, new
            {
                ok = true,
                processed = processed,
                consumedQueue = "wf.escalamiento",
                producedQueue = "wf.notificaciones",
                msgId = msgId,
                tareaId = tareaId,
                instanciaId = instanciaId,
                rolDestino = rolDestino,
                nuevoRol = nuevoRol,
                escaladoReal = escaladoReal,
                tareaNuevaId = tareaNuevaId,
                alreadyExisted = alreadyExisted,
                notifEnqueued = notifEnqueued,
                queueReverted = queueReverted,
                error = (string)null
            });
        }








        // =====================================================
        // GET /Api/Generico.ashx?action=doctipo.list
        // =====================================================
        private void ListarDocTipos(HttpContext ctx)
        {
            var list = new System.Collections.Generic.List<object>();

            using (var cn = new SqlConnection(
                ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
                SELECT DocTipoId, Codigo, Nombre
                FROM dbo.WF_DocTipo
                WHERE EsActivo = 1
                ORDER BY Codigo", cn))
            {
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(new
                        {
                            id = dr.GetInt32(0),
                            codigo = dr.GetString(1),
                            nombre = dr.GetString(2)
                        });
                    }
                }
            }

            ctx.Response.Write(
                new JavaScriptSerializer().Serialize(list)
            );
        }

        // =====================================================
        // GET /Api/Generico.ashx?action=doctipo.rules&codigo=XXX
        // =====================================================
        private void ObtenerRulesDocTipo(HttpContext ctx)
        {
            string codigo = (ctx.Request["codigo"] ?? "").Trim();

            ctx.Response.ContentType = "application/json";

            if (string.IsNullOrWhiteSpace(codigo))
            {
                ctx.Response.Write("[]");
                return;
            }

            string cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            {
                cn.Open();

                // 1) Si WF_DocTipo.RulesJson tiene algo, gana eso (override)
                using (var cmd1 = new SqlCommand(@"
SELECT ISNULL(NULLIF(LTRIM(RTRIM(RulesJson)), ''), '')
FROM dbo.WF_DocTipo
WHERE Codigo = @Codigo AND EsActivo = 1;", cn))
                {
                    cmd1.Parameters.AddWithValue("@Codigo", codigo);

                    var rulesOverride = Convert.ToString(cmd1.ExecuteScalar()) ?? "";
                    if (!string.IsNullOrWhiteSpace(rulesOverride))
                    {
                        ctx.Response.Write(rulesOverride);
                        return;
                    }
                }

                // 2) Si no hay override, construir JSON desde WF_DocTipoReglaExtract
                using (var cmd2 = new SqlCommand(@"
SELECT
(
    SELECT
        r.Campo  AS [campo],
        r.Regex  AS [regex],
        r.Grupo  AS [grupo]
    FROM dbo.WF_DocTipo d
    JOIN dbo.WF_DocTipoReglaExtract r ON r.DocTipoId = d.DocTipoId
    WHERE d.Codigo = @Codigo
      AND d.EsActivo = 1
      AND r.Activo = 1
    ORDER BY r.Orden
    FOR JSON PATH
) AS JsonRules;", cn))
                {
                    cmd2.Parameters.AddWithValue("@Codigo", codigo);

                    var json = Convert.ToString(cmd2.ExecuteScalar()) ?? "";
                    // Si no hay reglas activas, devolvemos vacío (textarea vacío)
                    ctx.Response.Write(string.IsNullOrWhiteSpace(json) ? "[]" : json);
                    return;
                }
            }
        }

        private class ReglaDto
        {
            public int id { get; set; }
            public int docTipoId { get; set; }
            public string docTipoCodigo { get; set; }

            public string campo { get; set; }
            public string tipoDato { get; set; }     // Texto|Fecha|CUIT|Importe|Numero|Codigo|Email
            public int grupo { get; set; }
            public int orden { get; set; }
            public bool activo { get; set; }

            public string ejemplo { get; set; }
            public string hintLabel { get; set; }
            public string hintContext { get; set; }
            public string modo { get; set; }         // LabelValue|...
            public string regex { get; set; }        // generado
        }

        private string ReadRequestBody(HttpContext ctx)
        {
            using (var sr = new System.IO.StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private void WriteJson(HttpContext ctx, object obj)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Write(new JavaScriptSerializer().Serialize(obj));
        }

        private string Cs()
        {
            return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        }

        //
        //GET /Api/Generico.ashx?action=doctipo.reglas.list&codigo=ORDEN_COMPRA
        //
        private void ListarReglasExtract(HttpContext ctx)
        {
            var codigo = (ctx.Request["codigo"] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                WriteJson(ctx, new { ok = false, error = "Falta codigo" });
                return;
            }

            var list = new System.Collections.Generic.List<ReglaDto>();

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
SELECT r.Id, d.DocTipoId, d.Codigo,
       r.Campo, ISNULL(r.TipoDato,''), ISNULL(r.Grupo,1), ISNULL(r.Orden,0), ISNULL(r.Activo,1),
       ISNULL(r.Ejemplo,''), ISNULL(r.HintLabel,''), ISNULL(r.HintContext,''), ISNULL(r.Modo,''), ISNULL(r.Regex,'')
FROM dbo.WF_DocTipo d
JOIN dbo.WF_DocTipoReglaExtract r ON r.DocTipoId = d.DocTipoId
WHERE d.Codigo = @Codigo
ORDER BY r.Orden, r.Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Codigo", codigo);
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        list.Add(new ReglaDto
                        {
                            id = dr.GetInt32(0),
                            docTipoId = dr.GetInt32(1),
                            docTipoCodigo = dr.GetString(2),
                            campo = dr.GetString(3),
                            tipoDato = dr.GetString(4),
                            grupo = dr.GetInt32(5),
                            orden = dr.GetInt32(6),

                            // BIT -> bool
                            activo = dr.GetBoolean(7),

                            ejemplo = dr.GetString(8),
                            hintLabel = dr.GetString(9),
                            hintContext = dr.GetString(10),
                            modo = dr.GetString(11),
                            regex = dr.GetString(12)
                        });
                    }
                }
            }

            WriteJson(ctx, new { ok = true, reglas = list });
        }

        //
        //POST /Api/Generico.ashx?action=doctipo.reglas.save
        //Body JSON: { id, docTipoCodigo, campo, tipoDato, orden, activo, ejemplo, hintContext }
        //

        private string EscapeRegex(string s)
        {
            return System.Text.RegularExpressions.Regex.Escape(s ?? "");
        }

        private string BuildRegex(string tipoDato, string ejemplo, string hintContext)
        {
            // patrón base por tipo (SIEMPRE capturante)
            string cap;
            var td = (tipoDato ?? "").Trim().ToLowerInvariant();

            if (td == "cuit") cap = "(\\d{2}-\\d{8}-\\d)";
            else if (td == "fecha") cap = "(\\d{2}/\\d{2}/\\d{4})";
            else if (td == "importe") cap = "\\$\\s*([0-9]{1,3}(?:\\.[0-9]{3})*)";
            else if (td == "numero") cap = "([0-9]+)";
            else if (td == "email") cap = "([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,})";
            else cap = "([^\\r\\n]+)";

            string ctx = hintContext ?? "";
            string ex = (ejemplo ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(ctx) && !string.IsNullOrWhiteSpace(ex))
            {
                // Trabajamos por líneas para evitar “agarrar medio documento”
                var lines = ctx.Replace("\r\n", "\n").Split('\n');
                foreach (var raw in lines)
                {
                    var line = (raw ?? "").Trim();
                    if (line.Length == 0) continue;

                    // ¿La línea contiene el ejemplo?
                    if (line.IndexOf(ex, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // 1) Caso "LABEL: valor"
                    int cpos = line.IndexOf(':');
                    if (cpos >= 0)
                    {
                        var label = line.Substring(0, cpos).Trim();

                        // Si el ejemplo es el LABEL (ej: "Nota de Pedido..." en vez del valor),
                        // igual sirve: label = "Nota de Pedido..." y capturamos lo que viene después de ":"
                        if (label.Length >= 2 && label.Length <= 80)
                        {
                            var labelEsc = EscapeRegex(label);
                            return labelEsc + "\\s*:\\s*" + cap;
                        }
                    }

                    // 2) Caso "LABEL valor" (sin ':') -> usamos el prefijo antes del ejemplo
                    //    Ej: "EMPRESA ACME S.A." -> prefijo "EMPRESA"
                    var idx = line.IndexOf(ex, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        var left = line.Substring(0, idx).Trim();

                        // quedarnos con el último token del prefijo (robusto)
                        // ej: "EMPRESA" / "Cod" / "Sector"
                        var parts = left.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var token = parts[parts.Length - 1].Trim();
                            if (token.Length >= 2 && token.Length <= 40)
                            {
                                var tokenEsc = EscapeRegex(token);
                                return tokenEsc + "\\s+" + cap;
                            }
                        }
                    }

                    // 3) Caso raro: el ejemplo está al inicio y NO hay ':'
                    //    Si el ejemplo parece un label (ej: "Solicitante") capturamos lo siguiente.
                    //    OJO: esto es un “mejor esfuerzo”, pero sigue siendo extracción.
                    if (idx == 0)
                    {
                        var exEsc = EscapeRegex(ex);
                        return exEsc + "\\s+" + cap;
                    }
                }
            }

            // 4) Si no pudimos inferir nada desde contexto:
            //    JAMÁS devolvemos el ejemplo literal. Devolvemos el capturador por tipo.
            return cap;
        }


        private void GuardarReglaExtract(HttpContext ctx)
        {
            var body = ReadRequestBody(ctx);
            var js = new JavaScriptSerializer();
            ReglaDto dto;

            try { dto = js.Deserialize<ReglaDto>(body); }
            catch
            {
                WriteJson(ctx, new { ok = false, error = "JSON inválido" });
                return;
            }

            var codigo = (dto.docTipoCodigo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                WriteJson(ctx, new { ok = false, error = "Falta docTipoCodigo" });
                return;
            }

            // Generar regex acá (NO en SQL)
            var regex = BuildRegex(dto.tipoDato, dto.ejemplo, dto.hintContext);
            var grupo = (dto.grupo <= 0 ? 1 : dto.grupo);

            int docTipoId = 0;

            using (var cn = new SqlConnection(Cs()))
            {
                cn.Open();

                // DocTipoId por código
                using (var cmd = new SqlCommand(@"SELECT DocTipoId FROM dbo.WF_DocTipo WHERE Codigo=@C AND EsActivo=1;", cn))
                {
                    cmd.Parameters.AddWithValue("@C", codigo);
                    var x = cmd.ExecuteScalar();
                    if (x == null)
                    {
                        WriteJson(ctx, new { ok = false, error = "DocTipo no encontrado" });
                        return;
                    }
                    docTipoId = Convert.ToInt32(x);
                }

                if (dto.id > 0)
                {
                    using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_DocTipoReglaExtract
SET Campo=@Campo, TipoDato=@TipoDato, Grupo=@Grupo, Orden=@Orden, Activo=@Activo,
    Ejemplo=@Ejemplo, HintLabel=@HintLabel, HintContext=@HintContext, Modo=@Modo,
    Regex=@Regex, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;", cn))
                    {
                        cmd.Parameters.AddWithValue("@Campo", (object)(dto.campo ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@TipoDato", (object)(dto.tipoDato ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@Grupo", grupo);
                        cmd.Parameters.AddWithValue("@Orden", dto.orden);
                        cmd.Parameters.AddWithValue("@Activo", dto.activo ? 1 : 0);
                        cmd.Parameters.AddWithValue("@Ejemplo", (object)(dto.ejemplo ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@HintLabel", (object)(dto.hintLabel ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@HintContext", (object)(dto.hintContext ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@Modo", (object)(dto.modo ?? "LabelValue") ?? "LabelValue");
                        cmd.Parameters.AddWithValue("@Regex", (object)regex ?? "");
                        cmd.Parameters.AddWithValue("@Id", dto.id);
                        cmd.ExecuteNonQuery();
                    }

                    WriteJson(ctx, new { ok = true, id = dto.id, regex = regex });
                    return;
                }
                else
                {
                    using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_DocTipoReglaExtract (DocTipoId, Campo, Regex, Grupo, Orden, Activo, CreatedAt,
                                      TipoDato, Ejemplo, HintLabel, HintContext, Modo)
VALUES (@DocTipoId, @Campo, @Regex, @Grupo, @Orden, @Activo, SYSUTCDATETIME(),
        @TipoDato, @Ejemplo, @HintLabel, @HintContext, @Modo);
SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
                    {
                        cmd.Parameters.AddWithValue("@DocTipoId", docTipoId);
                        cmd.Parameters.AddWithValue("@Campo", (object)(dto.campo ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@Regex", (object)regex ?? "");
                        cmd.Parameters.AddWithValue("@Grupo", grupo);
                        cmd.Parameters.AddWithValue("@Orden", dto.orden);
                        cmd.Parameters.AddWithValue("@Activo", dto.activo ? 1 : 0);
                        cmd.Parameters.AddWithValue("@TipoDato", (object)(dto.tipoDato ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@Ejemplo", (object)(dto.ejemplo ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@HintLabel", (object)(dto.hintLabel ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@HintContext", (object)(dto.hintContext ?? "") ?? "");
                        cmd.Parameters.AddWithValue("@Modo", (object)(dto.modo ?? "LabelValue") ?? "LabelValue");

                        var newId = Convert.ToInt32(cmd.ExecuteScalar());
                        WriteJson(ctx, new { ok = true, id = newId, regex = regex });
                        return;
                    }
                }
            }
        }

        //
        //POST /Api/Generico.ashx?action=doctipo.reglas.test
        //Body JSON: { regex: "...", grupo: 1, text: "...." }
        //
        private class TestReq
        {
            public string regex { get; set; }
            public int grupo { get; set; }
            public string text { get; set; }
        }

        private void ProbarReglaExtract(HttpContext ctx)
        {
            var body = ReadRequestBody(ctx);
            var js = new JavaScriptSerializer();
            TestReq req;

            try { req = js.Deserialize<TestReq>(body); }
            catch
            {
                WriteJson(ctx, new { ok = false, error = "JSON inválido" });
                return;
            }

            var rx = (req.regex ?? "").Trim();
            var text = req.text ?? "";
            var grupo = req.grupo <= 0 ? 1 : req.grupo;

            if (string.IsNullOrWhiteSpace(rx))
            {
                WriteJson(ctx, new { ok = false, error = "Falta regex" });
                return;
            }

            try
            {
                var re = new System.Text.RegularExpressions.Regex(rx,
                    System.Text.RegularExpressions.RegexOptions.Multiline);

                var m = re.Match(text);
                if (!m.Success)
                {
                    WriteJson(ctx, new { ok = true, success = false, value = (string)null });
                    return;
                }

                string value = (grupo < m.Groups.Count) ? m.Groups[grupo].Value : m.Value;

                WriteJson(ctx, new { ok = true, success = true, value = value, match = m.Value });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, new { ok = false, error = "Regex inválida: " + ex.Message });
            }
        }

          private void DashboardKpis(HttpContext ctx)
        {
            int total = 0;
            int draft = 0;
            int error = 0;

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
SELECT Id, CAST(ISNULL(Activo, 0) AS int) AS Activo, ISNULL(JsonDef, '') AS JsonDef
FROM dbo.WF_Definicion WITH (READPAST);", cn))
            {
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        total++;

                        int activo = dr.GetInt32(1);
                        if (activo == 0) draft++;

                        string json = dr.GetString(2);
                        if (IsWorkflowInvalid(json))
                            error++;
                    }
                }
            }

            // Instancias: por ahora lo devolvemos en 0 (lo conectamos cuando confirmes nombres de columnas)
            WriteJson(ctx, new
            {
                ok = true,
                workflows = new { total, draft, error },
                instancias = new { today = 0, failedToday = 0 }
            });
        }

        private bool IsWorkflowInvalid(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;

            try
            {
                var root = JObject.Parse(json);

                // StartNodeId obligatorio
                var startId = (string)root["StartNodeId"];
                if (string.IsNullOrWhiteSpace(startId)) return true;

                // Nodes obligatorio y que contenga el StartNodeId
                var nodes = root["Nodes"] as JObject;
                if (nodes == null || !nodes.Properties().Any()) return true;
                if (nodes[startId] == null) return true;

                // Debe existir al menos un util.end
                bool hasEnd = false;
                foreach (var p in nodes.Properties())
                {
                    var n = p.Value as JObject;
                    var t = (string)n?["Type"];
                    if (string.Equals(t, "util.end", StringComparison.OrdinalIgnoreCase))
                    {
                        hasEnd = true;
                        break;
                    }
                }
                if (!hasEnd) return true;

                // Edges: si existen, validar que From/To existan en Nodes
                var edges = root["Edges"] as JArray;
                if (edges != null)
                {
                    foreach (var e in edges)
                    {
                        var from = (string)e["From"];
                        var to = (string)e["To"];
                        if (!string.IsNullOrWhiteSpace(from) && nodes[from] == null) return true;
                        if (!string.IsNullOrWhiteSpace(to) && nodes[to] == null) return true;
                    }
                }

                return false; // OK
            }
            catch
            {
                return true; // JSON inválido
            }
        }





        public bool IsReusable => false;
    }
}
