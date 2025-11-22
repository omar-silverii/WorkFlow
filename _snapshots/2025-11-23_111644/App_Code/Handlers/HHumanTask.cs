using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo "human.task"
    /// 
    /// Primera ejecución para una instancia + nodo:
    ///   - Crea una fila en WF_Tarea (Estado = 'Pendiente')
    ///   - Marca wf.detener = true y el motor se detiene.
    ///
    /// Ejecuciones siguientes para la misma instancia + nodo:
    ///   - Si la tarea sigue 'Pendiente' → solo vuelve a frenar.
    ///   - Si la tarea está 'Completada' → NO crea nada,
    ///       expone datos en ctx.Estado y devuelve Etiqueta = Resultado,
    ///       para que las aristas del nodo usen ese label (apto, no_apto, etc.).
    ///
    /// Parámetros (node.Parameters):
    ///   titulo            (string)
    ///   descripcion       (string)
    ///   rol               (string)   → WF_Tarea.RolDestino
    ///   usuarioAsignado   (string)   → WF_Tarea.UsuarioAsignado
    ///   deadlineMinutes   (int?)     → suma a FechaCreacion para FechaVencimiento
    ///   metadata          (objeto/JObject, se guarda en WF_Tarea.Datos como JSON)
    /// </summary>
    public class HHumanTask : IManejadorNodo
    {
        public string TipoNodo => "human.task";

        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            var titulo = GetString(p, "titulo");
            var descripcion = GetString(p, "descripcion");
            var rolDestino = GetString(p, "rol");
            var usuarioAsignado = GetString(p, "usuarioAsignado");
            var deadlineMinutes = GetIntNullable(p, "deadlineMinutes");
            var metadataObj = GetObj(p, "metadata");

            // Id de instancia que nos pasa el Runtime vía WF_SEED
            long instId = 0;
            if (ctx.Estado.TryGetValue("wf.instanceId", out var instVal))
            {
                long.TryParse(Convert.ToString(instVal), out instId);
            }

            if (instId <= 0)
            {
                ctx.Log("[human.task] instancia no definida (wf.instanceId). Se continúa sin crear tarea.");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }

            // 1) ¿Ya existe tarea para esta instancia + nodo?
            WFTaskRow tarea = await BuscarTareaAsync(instId, nodo.Id, ct);
            if (tarea != null)
            {
                // === Caso A: tarea sigue pendiente → sólo frena el motor ===
                if (string.Equals(tarea.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Estado["wf.detener"] = true;
                    ctx.Estado["wf.currentNodeId"] = nodo.Id;
                    ctx.Estado["wf.currentNodeType"] = TipoNodo;

                    ctx.Log($"[human.task] tarea pendiente (Id={tarea.Id}) para instancia {instId} (nodo {nodo.Id})");
                    return new ResultadoEjecucion { Etiqueta = "pendiente" };
                }

                // === Caso B: tarea ya fue completada → seguir el flujo ===
                var etiqueta = tarea.Resultado;
                if (string.IsNullOrWhiteSpace(etiqueta))
                    etiqueta = "always";

                ctx.Log($"[human.task] tarea {tarea.Id} ya resuelta con resultado '{tarea.Resultado}'");

                // Dejo algunos datos en el contexto por si el grafo los quiere usar
                ctx.Estado["humanTask.id"] = tarea.Id;
                ctx.Estado["humanTask.resultado"] = tarea.Resultado;
                ctx.Estado["humanTask.usuario"] = tarea.UsuarioAsignado;

                return new ResultadoEjecucion { Etiqueta = etiqueta };
            }

            // 2) No existe → crear una nueva en estado Pendiente
            DateTime ahora = DateTime.Now;
            DateTime? vence = null;
            if (deadlineMinutes.HasValue && deadlineMinutes.Value > 0)
                vence = ahora.AddMinutes(deadlineMinutes.Value);

            // Metadata → si viene como JObject / Dictionary lo serializamos
            string datosJson = null;
            if (metadataObj != null)
            {
                if (metadataObj is string sMeta)
                    datosJson = sMeta;
                else if (metadataObj is JObject jobj)
                    datosJson = jobj.ToString(Formatting.None);
                else
                    datosJson = JsonConvert.SerializeObject(metadataObj);
            }

            long tareaId = await CrearTareaAsync(
                instId,
                nodo.Id,
                TipoNodo,
                string.IsNullOrWhiteSpace(titulo) ? "Tarea humana" : titulo,
                descripcion,
                rolDestino,
                usuarioAsignado,
                "Pendiente",
                null,
                ahora,
                vence,
                datosJson,
                ct
            );

            // Marcamos en el contexto que el motor debe detenerse
            ctx.Estado["wf.detener"] = true;
            ctx.Estado["wf.currentNodeId"] = nodo.Id;
            ctx.Estado["wf.currentNodeType"] = TipoNodo;
            ctx.Estado["humanTask.id"] = tareaId;

            ctx.Log($"[human.task] tarea creada para instancia {instId} (nodo {nodo.Id}) Id={tareaId}");

            return new ResultadoEjecucion { Etiqueta = "pendiente" };
        }

        #region Acceso a datos

        private class WFTaskRow
        {
            public long Id;
            public string Estado;
            public string Resultado;
            public string UsuarioAsignado;
        }

        private static async Task<WFTaskRow> BuscarTareaAsync(
            long instanciaId,
            string nodoId,
            CancellationToken ct)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 1 Id, Estado, Resultado, UsuarioAsignado
FROM dbo.WF_Tarea
WHERE WF_InstanciaId = @InstId AND NodoId = @NodoId
ORDER BY Id DESC;";
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = nodoId ?? (object)DBNull.Value;

                await cn.OpenAsync(ct).ConfigureAwait(false);
                using (var dr = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (await dr.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return new WFTaskRow
                        {
                            Id = dr.GetInt64(0),
                            Estado = dr.IsDBNull(1) ? null : dr.GetString(1),
                            Resultado = dr.IsDBNull(2) ? null : dr.GetString(2),
                            UsuarioAsignado = dr.IsDBNull(3) ? null : dr.GetString(3)
                        };
                    }
                }
            }
            return null;
        }

        private static async Task<long> CrearTareaAsync(
            long instanciaId,
            string nodoId,
            string nodoTipo,
            string titulo,
            string descripcion,
            string rolDestino,
            string usuarioAsignado,
            string estado,
            string resultado,
            DateTime fechaCreacion,
            DateTime? fechaVencimiento,
            string datosJson,
            CancellationToken ct)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO dbo.WF_Tarea
    (WF_InstanciaId, NodoId, NodoTipo,
     Titulo, Descripcion,
     RolDestino, UsuarioAsignado,
     Estado, Resultado,
     FechaCreacion, FechaVencimiento, Datos)
VALUES
    (@InstId, @NodoId, @NodoTipo,
     @Titulo, @Descripcion,
     @RolDestino, @UsuarioAsignado,
     @Estado, @Resultado,
     @FecCre, @FecVto, @Datos);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = (object)nodoId ?? DBNull.Value;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = (object)nodoTipo ?? DBNull.Value;
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = (object)titulo ?? DBNull.Value;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar).Value = (object)descripcion ?? DBNull.Value;
                cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value = (object)rolDestino ?? DBNull.Value;
                cmd.Parameters.Add("@UsuarioAsignado", SqlDbType.NVarChar, 100).Value = (object)usuarioAsignado ?? DBNull.Value;
                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = (object)estado ?? DBNull.Value;
                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = (object)resultado ?? DBNull.Value;
                cmd.Parameters.Add("@FecCre", SqlDbType.DateTime).Value = fechaCreacion;
                cmd.Parameters.Add("@FecVto", SqlDbType.DateTime).Value = (object)fechaVencimiento ?? DBNull.Value;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosJson ?? DBNull.Value;

                await cn.OpenAsync(ct).ConfigureAwait(false);
                var idObj = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return Convert.ToInt64(idObj);
            }
        }

        #endregion

        #region Helpers parámetros

        private static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            if (!d.TryGetValue(k, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static int? GetIntNullable(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            if (!d.TryGetValue(k, out var v) || v == null) return null;
            if (int.TryParse(Convert.ToString(v), out var i)) return i;
            return null;
        }

        private static object GetObj(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            d.TryGetValue(k, out var v);
            return v;
        }

        #endregion
    }
}
