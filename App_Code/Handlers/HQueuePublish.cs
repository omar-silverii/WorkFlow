using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.Runtime
{
    public class HQueuePublish : IManejadorNodo
    {
        public string TipoNodo => "queue.publish";

        private string Cnn
            => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string broker = GetString(p, "broker") ?? "simulado";
            string queueName = GetString(p, "queue") ?? "default";

            // payload opcional desde parámetros o desde el contexto
            object payload = null;
            if (p.TryGetValue("payload", out var v) && v != null)
            {
                payload = v;
            }
            else if (ctx.Estado.TryGetValue("payload", out var fromCtx) && fromCtx != null)
            {
                payload = fromCtx;
            }

            // ========= MODO SQL =========
            if (string.Equals(broker, "sql", StringComparison.OrdinalIgnoreCase))
            {
                return PublishToSql(ctx, queueName, payload);
            }

            // ========= MODO SIMULADO (EN MEMORIA, TAL COMO YA TENÍAS) =========
            string key = BuildQueueKey(queueName);

            if (!ctx.Estado.TryGetValue(key, out var listObj) || !(listObj is List<object> list))
            {
                list = new List<object>();
                ctx.Estado[key] = list;
            }

            list.Add(payload);

            string payloadPreview = payload == null
                ? "(null)"
                : Truncate(JsonConvert.SerializeObject(payload), 200);

            ctx.Log($"[queue.publish] modo=simulado, broker={broker}, queue={queueName}, mensajesEnCola={list.Count}.");
            ctx.Log("[queue.publish] payload: " + payloadPreview);

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        // ========= Implementación SQL =========
        private Task<ResultadoEjecucion> PublishToSql(ContextoEjecucion ctx, string queueName, object payload)
        {
            string payloadJson = payload == null
                ? "null"
                : JsonConvert.SerializeObject(payload);

            long messageId;
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO dbo.WF_QueueMessage (Broker, Queue, Payload, Estado)
VALUES (@Broker, @Queue, @Payload, @Estado);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                    cmd.Parameters.Add("@Broker", SqlDbType.NVarChar, 50).Value = "sql";
                    cmd.Parameters.Add("@Queue", SqlDbType.NVarChar, 200).Value = queueName;
                    cmd.Parameters.Add("@Payload", SqlDbType.NVarChar).Value = payloadJson;
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = "Pendiente";

                    cn.Open();
                    var obj = cmd.ExecuteScalar();
                    messageId = Convert.ToInt64(obj);
                }

                ctx.Estado["queue.lastMessageId"] = messageId;
                ctx.Log($"[queue.publish/sql] mensaje encolado Id={messageId} en queue='{queueName}'.");

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }
            catch (Exception ex)
            {
                ctx.Log("[queue.publish/sql/error] " + ex.GetType().Name + ": " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }
        }

        private static string BuildQueueKey(string queueName)
            => "queue:" + (queueName ?? "default");

        private static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            return d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : null;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }
    }
}
