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
    public class HQueueConsume : IManejadorNodo
    {
        public string TipoNodo => "queue.consume.legacy";

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
            int prefetch = GetInt(p, "prefetch", 1); // por ahora sólo informativo

            // ========= MODO SQL =========
            if (string.Equals(broker, "sql", StringComparison.OrdinalIgnoreCase))
            {
                return ConsumeFromSql(ctx, queueName, prefetch);
            }

            // ========= MODO SIMULADO (EN MEMORIA, TAL COMO YA TENÍAS) =========
            string key = BuildQueueKey(queueName);

            if (!ctx.Estado.TryGetValue(key, out var listObj) || !(listObj is List<object> list) || list.Count == 0)
            {
                ctx.Log($"[queue.consume] cola '{queueName}' vacía; no hay mensajes para consumir. (broker={broker})");
                ctx.Estado["queue.last"] = null;
                ctx.Estado["payload"] = null;
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }

            // Por ahora consumimos solo un mensaje (el primero)
            var msg = list[0];
            list.RemoveAt(0);

            ctx.Estado["queue.last"] = msg;
            ctx.Estado["payload"] = msg; // para que otros nodos puedan usar ${payload...}

            string preview = msg == null
                ? "(null)"
                : Truncate(JsonConvert.SerializeObject(msg), 200);

            ctx.Log($"[queue.consume] mensaje consumido de queue='{queueName}'. Restantes={list.Count}. (prefetch={prefetch}, broker={broker})");
            ctx.Log("[queue.consume] payload: " + preview);

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        // ========= Implementación SQL =========
        private Task<ResultadoEjecucion> ConsumeFromSql(ContextoEjecucion ctx, string queueName, int prefetch)
        {
            long id = 0;
            string payloadJson = null;

            try
            {
                using (var cn = new SqlConnection(Cnn))
                {
                    cn.Open();

                    // 1) Tomamos el primer mensaje pendiente
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
SELECT TOP(1) Id, Payload
FROM dbo.WF_QueueMessage WITH (ROWLOCK, READPAST)
WHERE Broker = @Broker
  AND Queue  = @Queue
  AND Estado = 'Pendiente'
ORDER BY Id;";

                        cmd.Parameters.Add("@Broker", SqlDbType.NVarChar, 50).Value = "sql";
                        cmd.Parameters.Add("@Queue", SqlDbType.NVarChar, 200).Value = queueName;

                        using (var dr = cmd.ExecuteReader())
                        {
                            if (dr.Read())
                            {
                                id = dr.GetInt64(0);
                                payloadJson = dr.IsDBNull(1) ? null : dr.GetString(1);
                            }
                        }
                    }

                    if (id == 0)
                    {
                        ctx.Log($"[queue.consume/sql] cola '{queueName}' vacía; no hay mensajes con Estado='Pendiente'.");
                        ctx.Estado["queue.last"] = null;
                        ctx.Estado["payload"] = null;

                        return Task.FromResult(new ResultadoEjecucion
                        {
                            Etiqueta = "always"
                        });
                    }

                    // 2) Marcamos como procesado (muy simple, sin reintentos todavía)
                    using (var cmd2 = cn.CreateCommand())
                    {
                        cmd2.CommandText = @"
UPDATE dbo.WF_QueueMessage
SET Estado   = 'Procesado',
    Intentos = ISNULL(Intentos,0) + 1
WHERE Id = @Id;";

                        cmd2.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
                        cmd2.ExecuteNonQuery();
                    }
                }

                object msgObj = null;
                if (!string.IsNullOrWhiteSpace(payloadJson))
                {
                    try
                    {
                        msgObj = JsonConvert.DeserializeObject<object>(payloadJson);
                    }
                    catch
                    {
                        msgObj = payloadJson;
                    }
                }

                ctx.Estado["queue.last"] = msgObj;
                ctx.Estado["payload"] = msgObj;

                string preview = msgObj == null
                    ? "(null)"
                    : Truncate(JsonConvert.SerializeObject(msgObj), 200);

                ctx.Log($"[queue.consume/sql] mensaje Id={id} consumido de queue='{queueName}'. (prefetch={prefetch})");
                ctx.Log("[queue.consume/sql] payload: " + preview);

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }
            catch (Exception ex)
            {
                ctx.Log("[queue.consume/sql/error] " + ex.GetType().Name + ": " + ex.Message);
                ctx.Estado["queue.last"] = null;
                ctx.Estado["payload"] = null;

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

        private static int GetInt(Dictionary<string, object> d, string k, int def = 0)
        {
            if (d == null) return def;
            if (!d.TryGetValue(k, out var v) || v == null) return def;
            return int.TryParse(Convert.ToString(v), out var i) ? i : def;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "...";
        }
    }
}
