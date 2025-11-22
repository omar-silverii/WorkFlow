using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "human.task": crea una fila en WF_Tarea y puede detener el flujo.
    /// - Primera vez que pasa por el nodo en una instancia → crea tarea y detiene.
    /// - Si ya existe una tarea para ese nodo+instancia → NO crea otra y deja seguir.
    /// </summary>
    public class HHumanTask : IManejadorNodo
    {
        public string TipoNodo => "human.task";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // 1) Obtener wf.instanceId desde el contexto
            long instanciaId = GetInstanceId(ctx);
            if (instanciaId <= 0)
            {
                ctx.Log("[human.task] wf.instanceId no encontrado en contexto; no se crea tarea.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // 2) Si ya existe una tarea para este nodo+instancia, NO crear otra
            if (!string.IsNullOrWhiteSpace(nodo.Id))
            {
                bool yaExiste = await ExisteTareaParaNodoAsync(instanciaId, nodo.Id, ct);
                if (yaExiste)
                {
                    ctx.Log($"[human.task] ya existe WF_Tarea para instancia {instanciaId}, nodo {nodo.Id}; se continúa sin crear otra.");
                    // IMPORTANTE: no seteamos wf.detener, así el motor sigue a la siguiente arista.
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
            }

            // 3) Título / descripción con soporte de ${...}
            string tituloTpl = GetString(p, "titulo") ?? $"Tarea para {instanciaId}";
            string descTpl = GetString(p, "descripcion") ?? "Completar acción pendiente";

            string titulo = ctx.ExpandString(tituloTpl);
            string descripcion = ctx.ExpandString(descTpl);
            string rol = ctx.ExpandString(GetString(p, "rol") ?? "");
            string usuarioAsignado = ctx.ExpandString(GetString(p, "usuarioAsignado") ?? "");

            int deadlineMinutes = GetInt(p, "deadlineMinutes", 0);
            DateTime? fechaVenc = null;
            if (deadlineMinutes > 0)
                fechaVenc = DateTime.Now.AddMinutes(deadlineMinutes);

            // 4) Metadata → JSON
            string metadataJson = BuildMetadataJson(p, instanciaId);

            long tareaId;
            try
            {
                tareaId = await InsertarTareaAsync(
                    instanciaId,
                    nodo.Id,
                    nodo.Type,
                    titulo,
                    descripcion,
                    rol,
                    usuarioAsignado,
                    fechaVenc,
                    metadataJson,
                    ct
                );
            }
            catch (Exception ex)
            {
                ctx.Log("[human.task] error al insertar WF_Tarea: " + ex.Message);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            ctx.Log($"[human.task] tarea creada para instancia {instanciaId} (nodo {nodo.Id}), tareaId={tareaId}");

            // 5) Marcar que el motor debe detenerse aquí (primera vez)
            ctx.Estado["wf.detener"] = true;
            ctx.Estado["wf.currentNodeId"] = nodo.Id;
            ctx.Estado["wf.currentNodeType"] = nodo.Type;
            ctx.Estado["wf.tarea.id"] = tareaId;   // por si lo necesitás después

            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        // -------- Helpers privados --------

        private static long GetInstanceId(ContextoEjecucion ctx)
        {
            object v;

            // Primero desde ctx.Estado
            if (ctx.Estado != null &&
                ctx.Estado.TryGetValue("wf.instanceId", out v) &&
                long.TryParse(Convert.ToString(v), out var id1))
            {
                return id1;
            }

            // Fallback: WF_SEED (por las dudas)
            try
            {
                var items = HttpContext.Current?.Items;
                if (items != null &&
                    items["WF_SEED"] is IDictionary<string, object> seed &&
                    seed.TryGetValue("wf.instanceId", out v) &&
                    long.TryParse(Convert.ToString(v), out var id2))
                {
                    return id2;
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Verifica si ya existe una WF_Tarea para ese WF_InstanciaId + NodoId.
        /// Sirve para evitar crear tareas duplicadas al reanudar el flujo.
        /// </summary>
        private static async Task<bool> ExisteTareaParaNodoAsync(long instanciaId, string nodoId, CancellationToken ct)
        {
            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
SELECT TOP 1 Id
FROM dbo.WF_Tarea
WHERE WF_InstanciaId = @InstId
  AND NodoId         = @NodoId;", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = nodoId ?? (object)DBNull.Value;

                await cn.OpenAsync(ct);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                return scalar != null && scalar != DBNull.Value;
            }
        }

        private static string BuildMetadataJson(Dictionary<string, object> p, long instanciaId)
        {
            object metaObj;
            var metaDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "origen", "workflow" },
                { "instanciaId", instanciaId.ToString() }
            };

            if (p.TryGetValue("metadata", out metaObj) && metaObj != null)
            {
                if (metaObj is Newtonsoft.Json.Linq.JObject jo)
                {
                    foreach (var prop in jo.Properties())
                        metaDict[prop.Name] = prop.Value?.ToString();
                }
                else if (metaObj is IDictionary<string, object> dic)
                {
                    foreach (var kv in dic)
                        metaDict[kv.Key] = kv.Value;
                }
            }

            return JsonConvert.SerializeObject(metaDict);
        }

        private static async Task<long> InsertarTareaAsync(
            long instanciaId,
            string nodoId,
            string nodoTipo,
            string titulo,
            string descripcion,
            string rol,
            string usuarioAsignado,
            DateTime? fechaVencimiento,
            string metadataJson,
            CancellationToken ct)
        {
            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Tarea
    (WF_InstanciaId, NodoId, NodoTipo,
     Titulo, Descripcion,
     RolDestino, UsuarioAsignado,
     Estado, Resultado,
     FechaCreacion, FechaVencimiento, FechaCierre,
     Datos)
VALUES
    (@InstId, @NodoId, @NodoTipo,
     @Titulo, @Descripcion,
     @RolDestino, @UsuarioAsignado,
     @Estado, @Resultado,
     GETDATE(), @FechaVencimiento, NULL,
     @Datos);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = (object)nodoId ?? DBNull.Value;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = (object)nodoTipo ?? DBNull.Value;
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = titulo ?? "";
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar).Value = (object)descripcion ?? DBNull.Value;
                cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value = (object)rol ?? DBNull.Value;
                cmd.Parameters.Add("@UsuarioAsignado", SqlDbType.NVarChar, 100).Value = (object)usuarioAsignado ?? DBNull.Value;
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = "Pendiente";
                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = DBNull.Value;
                cmd.Parameters.Add("@FechaVencimiento", SqlDbType.DateTime).Value = (object)fechaVencimiento ?? DBNull.Value;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)metadataJson ?? DBNull.Value;

                await cn.OpenAsync(ct);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt64(scalar);
            }
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            object v;
            return (p != null && p.TryGetValue(key, out v) && v != null)
                ? Convert.ToString(v)
                : null;
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def = 0)
        {
            object v;
            if (p != null && p.TryGetValue(key, out v) && v != null &&
                int.TryParse(Convert.ToString(v), out var i))
            {
                return i;
            }
            return def;
        }
    }
}
