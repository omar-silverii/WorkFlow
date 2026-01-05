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
                ctx.Log($"[human.task] ejecutando: inst={instanciaId} nodoId={nodo.Id} tipo={nodo.Type}");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            ctx.Log($"[human.task] ejecutando: inst={instanciaId} nodoId={nodo.Id} tipo={nodo.Type}");

            // 2) Si ya existe una tarea para este nodo+instancia, NO crear otra
            // 2) Si ya existe una tarea para este nodo+instancia:
            //    - si está Pendiente => detenerse acá
            //    - si está Completada/Cerrada => continuar
            if (!string.IsNullOrWhiteSpace(nodo.Id))
            {
               
                var tarea = await GetTareaParaNodoAsync(instanciaId, nodo.Id, nodo.Type, ct);
                if (tarea != null)
                {
                    if (EstaPendiente(tarea.Estado))
                    {
                        ctx.Log($"[human.task] ya existe WF_Tarea PENDIENTE para instancia {instanciaId}, nodo {nodo.Id}; se detiene aquí. tareaId={tarea.Id}");

                        ctx.Estado["wf.detener"] = true;
                        ctx.Estado["wf.currentNodeId"] = nodo.Id;
                        ctx.Estado["wf.currentNodeType"] = nodo.Type;
                        ctx.Estado["wf.tarea.id"] = tarea.Id;

                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    if (EstaCerrada(tarea.Estado))
                    {
                        ctx.Log($"[human.task] tarea ya CERRADA para instancia {instanciaId}, nodo {nodo.Id}; se continúa. tareaId={tarea.Id}, estado={tarea.Estado}");
                        // opcional: acá podrías inyectar tarea.Resultado al contexto si querés
                        ctx.Estado["wf.currentNodeId"] = nodo.Id;
                        ctx.Estado["wf.currentNodeType"] = nodo.Type;
                        ctx.Estado["wf.tarea.id"] = tarea.Id; // opcional, pero útil para logs
                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    // Estado desconocido: por seguridad, detener
                    ctx.Log($"[human.task] tarea existe con estado desconocido '{tarea.Estado}' para instancia {instanciaId}, nodo {nodo.Id}; por seguridad se detiene. tareaId={tarea.Id}");
                    ctx.Estado["wf.detener"] = true;
                    ctx.Estado["wf.currentNodeId"] = nodo.Id;
                    ctx.Estado["wf.currentNodeType"] = nodo.Type;
                    ctx.Estado["wf.tarea.id"] = tarea.Id;
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

            // 3.b) ScopeKey (desde el documento) + AsignadoA (desde usuarioAsignado)
            string scopeKey = null;

            // prioridad: input.sector (como lo venís usando en tu extracción)
            if (ctx.Estado != null)
            {
                object v = null;

                if (ctx.Estado.TryGetValue("input.sector", out v) && v != null)
                    scopeKey = Convert.ToString(v);
                else if (ctx.Estado.TryGetValue("input.Sector", out v) && v != null)
                    scopeKey = Convert.ToString(v);
            }

            // AsignadoA: solo si el nodo viene con usuarioAsignado puntual
            string asignadoA = null;
            if (!string.IsNullOrWhiteSpace(usuarioAsignado))
                asignadoA = usuarioAsignado;

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
                    scopeKey,
                    asignadoA,
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
        private sealed class TareaInfo
        {
            public long Id { get; set; }
            public string Estado { get; set; }      // "Pendiente", "Completada", etc.
            public string Resultado { get; set; }   // opcional
        }

        private static async Task<TareaInfo> GetTareaParaNodoAsync(
                                                                    long instanciaId,
                                                                    string nodoId,
                                                                    string nodoTipo,
                                                                    CancellationToken ct)
        {
            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
                                            SELECT TOP 1 Id, Estado, Resultado
                                            FROM dbo.WF_Tarea
                                            WHERE WF_InstanciaId = @InstId
                                              AND NodoId         = @NodoId
                                              AND NodoTipo       = @NodoTipo
                                            ORDER BY Id DESC;", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = nodoId;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = nodoTipo;

                await cn.OpenAsync(ct);
                using (var rd = await cmd.ExecuteReaderAsync(ct))
                {
                    if (!await rd.ReadAsync(ct)) return null;

                    return new TareaInfo
                    {
                        Id = rd.GetInt64(0),
                        Estado = rd.IsDBNull(1) ? null : rd.GetString(1),
                        Resultado = rd.IsDBNull(2) ? null : rd.GetString(2)
                    };
                }
            }
        }


        private static bool EstaPendiente(string estado)
        {
            return string.IsNullOrWhiteSpace(estado)
                || estado.Equals("Pendiente", StringComparison.OrdinalIgnoreCase)
                || estado.Equals("Abierta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EstaCerrada(string estado)
        {
            return estado != null &&
                (estado.Equals("Completada", StringComparison.OrdinalIgnoreCase)
              || estado.Equals("Cerrada", StringComparison.OrdinalIgnoreCase)
              || estado.Equals("Finalizada", StringComparison.OrdinalIgnoreCase));
        }

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
                                                            string scopeKey,
                                                            string asignadoA,
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
                                                     Datos,
                                                     ScopeKey,
                                                     AsignadoA)
                                                VALUES
                                                    (@InstId, @NodoId, @NodoTipo,
                                                     @Titulo, @Descripcion,
                                                     @RolDestino, @UsuarioAsignado,
                                                     @Estado, @Resultado,
                                                     GETDATE(), @FechaVencimiento, NULL,
                                                     @Datos,
                                                     @ScopeKey,
                                                     @AsignadoA);
                                                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                // ---------------------------
                // 1) ScopeKey desde el documento (mejor forma: mismo criterio que Instancia)
                //    Priorizo input.sector, y dejo fallback a input.Sector (por si alguna vez cambia).
                // ---------------------------
                scopeKey = null;

                // OJO: esta función no recibe ctx, entonces el scope lo tenés que calcular ANTES
                // y pasarlo como parámetro, o mover este bloque afuera.
                //
                // 👉 Mejor opción: agregar 2 parámetros nuevos a InsertarTareaAsync:
                //    string scopeKey, string asignadoA
                //
                // Como vos me pasaste la firma actual, dejo el SQL listo,
                // pero estos 2 valores los paso por parámetros y los seteás donde llamás a InsertarTareaAsync.

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

                // ✅ NUEVOS CAMPOS (ya existen en tu tabla)
                cmd.Parameters.Add("@ScopeKey", SqlDbType.NVarChar, 100).Value = (object)scopeKey ?? DBNull.Value;

                // ✅ “AsignadoA” = usuario puntual (si existe). Si no, NULL.
                //    (en tareas por rol puede estar vacío)
                cmd.Parameters.Add("@AsignadoA", SqlDbType.NVarChar, 100).Value = DBNull.Value;

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
