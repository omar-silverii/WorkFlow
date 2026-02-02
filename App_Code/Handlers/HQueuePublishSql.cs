using Intranet.WorkflowStudio.WebForms;
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.Runtime
{
    /// <summary>
    /// Publica un mensaje en WF_Queue usando SQL.
    /// Tipo de nodo: "queue.publish"
    /// </summary>
    public class HQueuePublishSql : IManejadorNodo
    {
        public string TipoNodo => "queue.publish";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // Broker: hoy solo soportamos "sql"
            var broker = (p.ContainsKey("broker") ? Convert.ToString(p["broker"]) : "sql")
                         ?.ToLowerInvariant() ?? "sql";

            if (broker != "sql")
            {
                var msg = $"Broker '{broker}' no soportado por HQueuePublishSql.";
                ctx.Log("[Queue.PublishSql/error] " + msg);
                ctx.Estado["queue.error"] = msg;
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // Nombre de cola (permite templating: "notif-${input.usuarioId}")
            var queueRaw = p.ContainsKey("queue") ? Convert.ToString(p["queue"]) : "default";
            var queue = TemplateUtil.Expand(ctx, queueRaw ?? "default");

            // Tomamos SOLO el parámetro "payload" como mensaje de negocio
            object payloadObj = null;
            p.TryGetValue("payload", out payloadObj);

            JToken payloadToken;
            if (payloadObj is JToken jt)
            {
                payloadToken = jt.DeepClone();
            }
            else if (payloadObj != null)
            {
                payloadToken = JToken.FromObject(payloadObj);
            }
            else
            {
                payloadToken = new JObject();
            }

            // Expandimos recursivamente TODOS los strings dentro del payload
            ExpandStringsRecursive(ctx, payloadToken);

            // JSON final que va a la columna WF_Queue.Payload
            var payloadJson = payloadToken.ToString(Formatting.None);

            // CorrelationId (si no viene, usamos wf.instanceId o un Guid)
            string correlationId = null;
            if (p.ContainsKey("correlationId"))
            {
                var cidRaw = Convert.ToString(p["correlationId"]);
                correlationId = TemplateUtil.Expand(ctx, cidRaw);
            }

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                var fromCtx = ContextoEjecucion.ResolverPath(ctx.Estado, "wf.instanceId");
                correlationId = (fromCtx != null ? Convert.ToString(fromCtx) : null)
                                ?? Guid.NewGuid().ToString("N");
            }

            // DueAt (opcional)
            DateTime? dueAt = null;
            if (p.ContainsKey("dueAt") && p["dueAt"] != null)
            {
                var dueRaw = TemplateUtil.Expand(ctx, Convert.ToString(p["dueAt"]));
                if (DateTime.TryParse(dueRaw, out var dt))
                {
                    dueAt = dt;
                }
            }

            // Priority (opcional, default 0)
            int priority = 0;
            if (p.ContainsKey("priority") && p["priority"] != null)
            {
                var prRaw = TemplateUtil.Expand(ctx, Convert.ToString(p["priority"]));
                int.TryParse(prRaw, out priority);
            }

            // ConnectionString
            var cnnName = p.ContainsKey("connectionStringName")
                ? Convert.ToString(p["connectionStringName"])
                : "DefaultConnection";

            var csItem = ConfigurationManager.ConnectionStrings[cnnName];
            if (csItem == null)
            {
                var msg = $"ConnectionString '{cnnName}' no encontrada en Web.config.";
                ctx.Log("[Queue.PublishSql/error] " + msg);
                ctx.Estado["queue.error"] = msg;
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            var cnnString = csItem.ConnectionString;
            long idGenerado;

            try
            {
                using (var cn = new SqlConnection(cnnString))
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO dbo.WF_Queue
    (Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, Attempts)
VALUES
    (@Queue, @CorrelationId, @Payload, GETDATE(), @DueAt, @Priority, 0, 0);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                    cmd.Parameters.Add("@Queue", SqlDbType.NVarChar, 100).Value =
                        (object)queue ?? DBNull.Value;
                    cmd.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 100).Value =
                        (object)correlationId ?? DBNull.Value;
                    cmd.Parameters.Add("@Payload", SqlDbType.NVarChar).Value =
                        (object)payloadJson ?? "{}";

                    var dueParam = cmd.Parameters.Add("@DueAt", SqlDbType.DateTime);
                    if (dueAt.HasValue)
                        dueParam.Value = dueAt.Value;
                    else
                        dueParam.Value = DBNull.Value;

                    cmd.Parameters.Add("@Priority", SqlDbType.Int).Value = priority;

                    await cn.OpenAsync(ct);
                    var obj = await cmd.ExecuteScalarAsync(ct);
                    idGenerado = Convert.ToInt64(obj);
                }

                // Guardamos info de la última publicación
                var info = new Dictionary<string, object>
                {
                    ["queue"] = queue,
                    ["id"] = idGenerado,
                    ["correlationId"] = correlationId,
                    ["payload"] = JsonConvert.DeserializeObject<object>(payloadJson)
                };

                ctx.Estado["queue.last"] = info;

                ctx.Log($"[Queue.PublishSql] Encolado Id={idGenerado} queue='{queue}'.");

                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Estado["queue.error"] = ex.Message;
                ctx.Log("[Queue.PublishSql/error] " + ex.Message);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        /// <summary>
        /// Recorre el JToken y aplica TemplateUtil.Expand a todos los valores string.
        /// Soporta objetos y arrays anidados.
        /// </summary>
        private static void ExpandStringsRecursive(ContextoEjecucion ctx, JToken token)
        {
            if (token == null) return;

            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        ExpandStringsRecursive(ctx, prop.Value);
                    }
                    break;

                case JTokenType.Array:
                    foreach (var item in (JArray)token)
                    {
                        ExpandStringsRecursive(ctx, item);
                    }
                    break;

                case JTokenType.String:
                    var original = token.ToString();
                    var expanded = TemplateUtil.Expand(ctx, original) ?? original;
                    if (!string.Equals(original, expanded, StringComparison.Ordinal))
                    {
                        ((JValue)token).Value = expanded;
                    }
                    break;

                default:
                    // Otros tipos (Number, Boolean, etc.) se dejan igual
                    break;
            }
        }
    }

    /// <summary>
    /// Consume mensajes de WF_Queue usando SQL.
    /// Tipo de nodo: "queue.consume"
    /// </summary>
    public class HQueueConsumeSql : IManejadorNodo
    {
        public string TipoNodo => "queue.consume";

        // Aplana recursivamente un JToken en ctx.Estado, generando claves tipo:
        // payload.ocNumero
        // payload.proveedor.codigo
        // payload.proveedor.razonSocial
        // etc.
        private static void FlattenIntoEstado(ContextoEjecucion ctx, string prefix, JToken token)
        {
            if (ctx == null || ctx.Estado == null) return;

            if (token == null || token.Type == JTokenType.Null)
            {
                ctx.Estado[prefix] = null;
                return;
            }

            switch (token.Type)
            {
                case JTokenType.Object:
                    {
                        // ✅ guardo el objeto completo como diccionario "plain" (navegable)
                        ctx.Estado[prefix] = (Dictionary<string, object>)TokenToPlainObject(token);

                        foreach (var prop in ((JObject)token).Properties())
                        {
                            FlattenIntoEstado(ctx, prefix + "." + prop.Name, prop.Value);
                        }
                        break;
                    }

                case JTokenType.Array:
                    {
                        ctx.Estado[prefix] = (List<object>)TokenToPlainObject(token);
                        break;
                    }

                default:
                    {
                        ctx.Estado[prefix] = ((JValue)token).Value;
                        break;
                    }
            }
        }


        private static object TokenToPlainObject(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            switch (token.Type)
            {
                case JTokenType.Object:
                    {
                        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in ((JObject)token).Properties())
                            d[p.Name] = TokenToPlainObject(p.Value);
                        return d;
                    }

                case JTokenType.Array:
                    {
                        var list = new List<object>();
                        foreach (var it in (JArray)token)
                            list.Add(TokenToPlainObject(it));
                        return list;
                    }

                default:
                    return ((JValue)token).Value;
            }
        }


        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            ctx.Log("[Queue.ConsumeSql/BUILD] 2026-02-01 A");
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // Nombre de la cola (permite templating)
            var queueRaw = p.ContainsKey("queue") ? Convert.ToString(p["queue"]) : "default";
            var queue = TemplateUtil.Expand(ctx, queueRaw ?? "default");

            // Cantidad de mensajes a tomar (hoy usamos 1)
            int take = 1;
            if (p.ContainsKey("take") && p["take"] != null)
            {
                var takeRaw = TemplateUtil.Expand(ctx, Convert.ToString(p["take"]));
                if (!int.TryParse(takeRaw, out take) || take <= 0) take = 1;
            }

            // ConnectionString
            var cnnName = p.ContainsKey("connectionStringName")
                ? Convert.ToString(p["connectionStringName"])
                : "DefaultConnection";

            var csItem = ConfigurationManager.ConnectionStrings[cnnName];
            if (csItem == null)
            {
                var msg = $"ConnectionString '{cnnName}' no encontrada en Web.config.";
                ctx.Log("[Queue.ConsumeSql/error] " + msg);
                ctx.Estado["queue.error"] = msg;
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            var cnnString = csItem.ConnectionString;
            var mensajes = new List<Dictionary<string, object>>();

            try
            {
                using (var cn = new SqlConnection(cnnString))
                {
                    await cn.OpenAsync(ct);

                    using (var tx = cn.BeginTransaction(IsolationLevel.ReadCommitted))
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
;WITH cte AS (
    SELECT TOP (@Take)
        Id, Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, ProcessedAt, Attempts
    FROM dbo.WF_Queue WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE Queue     = @Queue
      AND Processed = 0
      AND (DueAt IS NULL OR DueAt <= GETDATE())
    ORDER BY Priority DESC, CreatedAt, Id
)
UPDATE cte
   SET Processed   = 1,
       ProcessedAt = GETDATE(),
       Attempts    = Attempts + 1
OUTPUT
    inserted.Id,
    inserted.Queue,
    inserted.CorrelationId,
    inserted.Payload,
    inserted.CreatedAt,
    inserted.DueAt,
    inserted.Priority,
    inserted.Attempts;";

                        cmd.Parameters.Add("@Queue", SqlDbType.NVarChar, 100).Value =
                            (object)queue ?? DBNull.Value;
                        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;

                        using (var rdr = await cmd.ExecuteReaderAsync(ct))
                        {
                            while (await rdr.ReadAsync(ct))
                            {
                                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["Id"] = rdr.GetInt64(0),
                                    ["Queue"] = rdr.GetString(1),
                                    ["CorrelationId"] = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                                    ["Payload"] = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                                    ["CreatedAt"] = rdr.GetDateTime(4),
                                    ["DueAt"] = rdr.IsDBNull(5) ? (DateTime?)null : rdr.GetDateTime(5),
                                    ["Priority"] = rdr.IsDBNull(6) ? 0 : rdr.GetInt32(6),
                                    ["Attempts"] = rdr.IsDBNull(7) ? 0 : rdr.GetInt32(7)
                                };
                                mensajes.Add(row);
                            }
                        }

                        tx.Commit();
                    }
                }

                if (mensajes.Count == 0)
                {
                    ctx.Estado["queue.hasMessage"] = false;
                    ctx.Estado["queue.messages"] = new List<Dictionary<string, object>>();
                    ctx.Estado["queue.message"] = null;
                    ctx.Estado["queue.messageId"] = null;

                    // ✅ SIEMPRE publicar raíz queue
                    ctx.Estado["queue"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["hasMessage"] = false,
                        ["messages"] = new List<Dictionary<string, object>>(),
                        ["message"] = null,
                        ["messageId"] = null
                    };

                    ctx.Estado["payload"] = null;

                    ctx.Log($"[Queue.ConsumeSql] No hay mensajes pendientes en '{queue}'.");
                }
                else
                {
                    var first = mensajes[0];

                    // ✅ ID del mensaje
                    var messageId = first.ContainsKey("Id") ? first["Id"] : null;

                    // Publicar flags + lista
                    ctx.Estado["queue.hasMessage"] = true;
                    ctx.Estado["queue.messages"] = mensajes;

                    // ✅ queue.message como diccionario navegable
                    ctx.Estado["queue.message"] = first;

                    ctx.Estado["queue.messageId"] = messageId;   // ${queue.messageId}
                    ctx.Estado["queueId"] = messageId;          // ${queueId} alias
                    ctx.Estado["queue.message.Id"] = messageId; // ${queue.message.Id}
                    ctx.Estado["queue.message.id"] = messageId; // ${queue.message.id}

                    foreach (var kv in first)
                    {
                        ctx.Estado["queue.message." + kv.Key] = kv.Value;
                        ctx.Estado["queue.message." + kv.Key.ToLowerInvariant()] = kv.Value;
                    }

                    // ✅ SIEMPRE publicar raíz queue (también cuando hay mensaje)
                    ctx.Estado["queue"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["hasMessage"] = true,
                        ["messages"] = mensajes,
                        ["message"] = first,
                        ["messageId"] = messageId
                    };

                    var payloadJson = first["Payload"] as string;
                    ctx.Estado["payload"] = null;

                    if (!string.IsNullOrWhiteSpace(payloadJson))
                    {
                        try
                        {
                            ctx.Log("[Queue.ConsumeSql/debug] payloadJson=" + payloadJson);

                            // 1) Parse inicial
                            JToken rootToken = JToken.Parse(payloadJson);

                            // 2) ✅ FIX DOBLE-JSON: si el payload es un string que contiene JSON, parsear de nuevo
                            if (rootToken.Type == JTokenType.String)
                            {
                                var inner = rootToken.ToString(); // contenido del string
                                inner = inner?.Trim();

                                if (!string.IsNullOrEmpty(inner) &&
                                    ((inner.StartsWith("{") && inner.EndsWith("}")) || (inner.StartsWith("[") && inner.EndsWith("]"))))
                                {
                                    rootToken = JToken.Parse(inner);
                                    ctx.Log("[Queue.ConsumeSql/debug] double-json detected -> reparsed inner JSON OK.");
                                }
                                else
                                {
                                    ctx.Log("[Queue.ConsumeSql/debug] rootToken is STRING but inner is not JSON object/array.");
                                }
                            }

                            // 3) Si viene envoltorio { payload: {...} }, usar el payload interno
                            JToken effectivePayload = rootToken;
                            if (rootToken.Type == JTokenType.Object)
                            {
                                var rootObj = (JObject)rootToken;
                                if (rootObj["payload"] != null &&
                                    (rootObj["payload"].Type == JTokenType.Object || rootObj["payload"].Type == JTokenType.Array))
                                {
                                    effectivePayload = rootObj["payload"];
                                }
                            }

                            // 4) ✅ payload SIEMPRE plain (Dictionary/List), case-insensitive, navegable
                            var plain = TokenToPlainObject(effectivePayload);
                            ctx.Estado["payload"] = plain;

                            // 5) ✅ rutas planas payload.xxx
                            FlattenIntoEstado(ctx, "payload", effectivePayload);

                            // 6) raw por si hace falta
                            ctx.Estado["payload.raw"] = TokenToPlainObject(rootToken);

                            ctx.Log("[Queue.ConsumeSql] Mensaje Id=" + first["Id"] + " → payload en Estado['payload'].");

                            // DEBUG DURO
                            var test1 = ContextoEjecucion.ResolverPath(ctx.Estado, "payload.ocNumero");
                            ctx.Log("[Queue.ConsumeSql/debug] ResolverPath(payload.ocNumero)=" + Convert.ToString(test1 ?? "(null)"));

                            var test2 = ctx.Estado.ContainsKey("payload.ocNumero") ? ctx.Estado["payload.ocNumero"] : null;
                            ctx.Log("[Queue.ConsumeSql/debug] Estado[payload.ocNumero]=" + Convert.ToString(test2 ?? "(null)"));

                            var pObj = ctx.Estado.ContainsKey("payload") ? ctx.Estado["payload"] : null;
                            ctx.Log("[Queue.ConsumeSql/debug] payload.type=" + (pObj == null ? "(null)" : pObj.GetType().FullName));


                        }
                        catch (Exception exJson)
                        {
                            ctx.Estado["payloadRaw"] = payloadJson;
                            ctx.Log("[Queue.ConsumeSql] Error parseando payload: " + exJson.Message);
                            ctx.Estado["payload"] = null;
                        }
                    }


                    ctx.Log($"[Queue.ConsumeSql] Consumidos {mensajes.Count} mensaje(s) de '{queue}'.");
                }

                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Estado["queue.error"] = ex.Message;
                ctx.Log("[Queue.ConsumeSql/error] " + ex.Message);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }
    }



}
