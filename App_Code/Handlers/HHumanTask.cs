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

                // ===== Retroceso controlado (backtrack) =====
                var back = GetOrCreateBackState(ctx);
                var activeFrame = back.ActiveFrameForTaskNode(nodo.Id);
                var returnFrame = back.ActiveRejectedFrameReturningTo(nodo.Id);

                int expectedCycle = 0;
                if (activeFrame != null && activeFrame.StatusEquals("rejected"))
                    expectedCycle = activeFrame.Cycle + 1;
                else if (returnFrame != null)
                    expectedCycle = returnFrame.Cycle + 1;

                if (tarea != null)
                {
                    // 1) Si está pendiente => detenerse acá
                    if (EstaPendiente(tarea.Estado))
                    {
                        ctx.Log($"[human.task] ya existe WF_Tarea PENDIENTE para instancia {instanciaId}, nodo {nodo.Id}; se detiene aquí. tareaId={tarea.Id}");

                        ctx.Estado["wf.detener"] = true;
                        ctx.Estado["wf.currentNodeId"] = nodo.Id;
                        ctx.Estado["wf.currentNodeType"] = nodo.Type;
                        ctx.Estado["wf.tarea.id"] = tarea.Id;

                        var f0 = ExtraerFrameId(tarea.Datos);
                        if (!string.IsNullOrWhiteSpace(f0))
                            ctx.Estado["wf.back.activeFrameId"] = f0;

                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    // 2) Si está cerrada:
                    //    - si venimos por rechazo y corresponde nuevo ciclo => NO retornar (deja que cree una nueva)
                    //    - si no => continuar normalmente
                    if (EstaCerrada(tarea.Estado))
                    {
                        tarea.Cycle = ExtraerCycle(tarea.Datos);
                        tarea.FrameId = ExtraerFrameId(tarea.Datos);

                        if (expectedCycle > 0 && tarea.Cycle > 0 && tarea.Cycle < expectedCycle)
                        {
                            ctx.Log($"[human.task] backtrack: ciclo anterior detectado (lastCycle={tarea.Cycle}, expectedCycle={expectedCycle}) -> se crea nueva tarea.");
                            // NO retornar => cae al bloque de creación abajo
                        }
                        else
                        {
                            ctx.Log($"[human.task] tarea ya CERRADA para instancia {instanciaId}, nodo {nodo.Id}; se continúa. tareaId={tarea.Id}, estado={tarea.Estado}, resultado={tarea.Resultado}");

                            ctx.Estado["wf.tarea.id"] = tarea.Id;
                            ctx.Estado["wf.tarea.estado"] = tarea.Estado;

                            var res = (tarea.Resultado ?? "").Trim().ToLowerInvariant();
                            ctx.Estado["wf.tarea.resultado"] = res;
                            ctx.Estado[$"wf.tarea.{nodo.Id}.resultado"] = res;

                            ctx.Estado["wf.currentNodeId"] = nodo.Id;
                            ctx.Estado["wf.currentNodeType"] = nodo.Type;

                            return new ResultadoEjecucion { Etiqueta = "always" };
                        }
                    }
                    else
                    {
                        // Estado raro (ni pendiente ni cerrada) => por seguridad detener
                        ctx.Log($"[human.task] tarea existe con estado desconocido '{tarea.Estado}' para instancia {instanciaId}, nodo {nodo.Id}; por seguridad se detiene. tareaId={tarea.Id}");

                        ctx.Estado["wf.detener"] = true;
                        ctx.Estado["wf.currentNodeId"] = nodo.Id;
                        ctx.Estado["wf.currentNodeType"] = nodo.Type;
                        ctx.Estado["wf.tarea.id"] = tarea.Id;

                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }
                }

                // Si tarea == null, o si estaba cerrada pero corresponde crear nuevo ciclo,
                // cae al flujo de creación que ya tenés más abajo.

            }


            // ===== Retroceso controlado (backtrack): definir frame/cycle/returnTo =====
            var back2 = GetOrCreateBackState(ctx);

            var prevNodeId = GetStringFromState(ctx, "wf.exec.prevNodeId");

            var rf0 = back2.ActiveRejectedFrameReturningTo(nodo.Id);
            if (rf0 != null)
                prevNodeId = rf0.ReturnToNodeId;

            string frameId = null;
            int cycle = 1;

            // Si venimos de un rechazo:
            // - si el nodo actual es el rechazado, reabrimos ESE frame y subimos ciclo
            // - si el nodo actual es el returnTo del frame rechazado, creamos NUEVA tarea para el anterior
            var af = back2.ActiveFrameForTaskNode(nodo.Id);
            var rf = back2.ActiveRejectedFrameReturningTo(nodo.Id);

            if (af != null && af.StatusEquals("rejected"))
            {
                frameId = af.FrameId;
                cycle = af.Cycle + 1;
                af.Cycle = cycle;
                af.Status = "open";
            }
            else if (rf != null)
            {
                frameId = Guid.NewGuid().ToString("N");
                cycle = rf.Cycle + 1;

                // Nuevo frame para el nodo al que vuelve el rechazo
                back2.EnsureFrame(frameId, nodo.Id, prevNodeId, cycle);
            }
            else
            {
                frameId = Guid.NewGuid().ToString("N");
                cycle = 1;

                // Nuevo frame normal
                back2.EnsureFrame(frameId, nodo.Id, prevNodeId, cycle);
            }

            // Dejar activo el frame (el flujo queda detenido en este human.task)
            ctx.Estado["wf.back.activeFrameId"] = frameId;
            ctx.Estado["wf.back.mode"] = "task";

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
                object v;

                if (ctx.Estado.TryGetValue("input.scopeKey", out v) && v != null)
                    scopeKey = Convert.ToString(v);
                else if (ctx.Estado.TryGetValue("input.sector", out v) && v != null)
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
            var wfBack = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "frameId", frameId },
                { "taskNodeId", nodo.Id },
                { "returnToNodeId", prevNodeId },
                { "cycle", cycle }
            };

            string metadataJson = BuildMetadataJson(p, instanciaId, wfBack);

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
            public string Datos { get; set; }        // JSON (metadata / data)
            public int Cycle { get; set; }           // wfBack.cycle
            public string FrameId { get; set; }      // wfBack.frameId
        }

                // ===== Backtrack helpers (retroceso controlado) =====
        private sealed class BackFrame
        {
            private readonly Dictionary<string, object> _d;

            public BackFrame(Dictionary<string, object> d)
            {
                _d = d ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            private string GetStr(string k)
            {
                if (_d.TryGetValue(k, out var v) && v != null) return Convert.ToString(v);
                return null;
            }

            private void SetStr(string k, string v)
            {
                _d[k] = v;
            }

            private int GetInt(string k, int defVal)
            {
                if (_d.TryGetValue(k, out var v) && v != null)
                {
                    if (v is int i) return i;
                    if (int.TryParse(Convert.ToString(v), out var x)) return x;
                }
                return defVal;
            }

            private void SetInt(string k, int v)
            {
                _d[k] = v;
            }

            public string FrameId => GetStr("frameId");
            public string TaskNodeId { get => GetStr("taskNodeId"); set => SetStr("taskNodeId", value); }
            public string ReturnToNodeId { get => GetStr("returnToNodeId"); set => SetStr("returnToNodeId", value); }
            public int Cycle { get => GetInt("cycle", 1); set => SetInt("cycle", value); }
            public string Status { get => GetStr("status"); set => SetStr("status", value); } // open/rejected/approved

            public bool StatusEquals(string s) => string.Equals(Status ?? "", s ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class BackState
        {
            private readonly ContextoEjecucion _ctx;
            private readonly List<Dictionary<string, object>> _stack;

            public BackState(ContextoEjecucion ctx, List<Dictionary<string, object>> stack)
            {
                _ctx = ctx;
                _stack = stack;
            }

            public BackFrame ActiveFrameForTaskNode(string taskNodeId)
            {
                if (_ctx?.Estado == null) return null;

                if (!_ctx.Estado.TryGetValue("wf.back.activeFrameId", out var v) || v == null) return null;
                var fid = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(fid)) return null;

                var fr = FindFrame(fid);
                if (fr == null) return null;

                if (!string.IsNullOrWhiteSpace(taskNodeId) && !string.Equals(fr.TaskNodeId, taskNodeId, StringComparison.OrdinalIgnoreCase))
                    return null;

                return fr;
            }

            public BackFrame ActiveRejectedFrameReturningTo(string nodeId)
            {
                if (_ctx?.Estado == null) return null;

                if (!_ctx.Estado.TryGetValue("wf.back.activeFrameId", out var v) || v == null) return null;
                var fid = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(fid)) return null;

                var fr = FindFrame(fid);
                if (fr == null) return null;

                if (!fr.StatusEquals("rejected")) return null;
                if (string.IsNullOrWhiteSpace(nodeId)) return null;

                if (!string.Equals(fr.ReturnToNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    return null;

                return fr;
            }

            public void EnsureFrame(string frameId, string taskNodeId, string returnToNodeId, int cycle)
            {
                if (string.IsNullOrWhiteSpace(frameId)) return;

                // ya existe?
                for (int i = 0; i < _stack.Count; i++)
                {
                    var d = _stack[i];
                    if (d == null) continue;
                    if (d.TryGetValue("frameId", out var v) && string.Equals(Convert.ToString(v), frameId, StringComparison.OrdinalIgnoreCase))
                    {
                        d["taskNodeId"] = taskNodeId;
                        d["returnToNodeId"] = returnToNodeId;
                        d["cycle"] = cycle;
                        if (!d.ContainsKey("status")) d["status"] = "open";
                        return;
                    }
                }

                _stack.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "frameId", frameId },
                    { "taskNodeId", taskNodeId },
                    { "returnToNodeId", returnToNodeId },
                    { "cycle", cycle },
                    { "status", "open" },
                    { "rejectCount", 0 }
                });
            }

            private BackFrame FindFrame(string frameId)
            {
                if (string.IsNullOrWhiteSpace(frameId)) return null;

                for (int i = 0; i < _stack.Count; i++)
                {
                    var d = _stack[i];
                    if (d == null) continue;

                    if (d.TryGetValue("frameId", out var v) && string.Equals(Convert.ToString(v), frameId, StringComparison.OrdinalIgnoreCase))
                    {
                        return new BackFrame(d);
                    }
                }

                return null;
            }

            private static int TryToInt(object v, int defVal)
            {
                if (v == null) return defVal;
                if (v is int i) return i;
                if (int.TryParse(Convert.ToString(v), out var x)) return x;
                return defVal;
            }
        }

        private static BackState GetOrCreateBackState(ContextoEjecucion ctx)
        {
            if (ctx?.Estado == null) return new BackState(ctx, new List<Dictionary<string, object>>());

            if (!ctx.Estado.TryGetValue("wf.back.stack", out var v) || v == null)
            {
                var stackNew = new List<Dictionary<string, object>>();
                ctx.Estado["wf.back.stack"] = stackNew;
                return new BackState(ctx, stackNew);
            }

            // v puede venir como JArray (por snapshot)
            if (v is Newtonsoft.Json.Linq.JArray ja)
            {
                var stack = new List<Dictionary<string, object>>();
                foreach (var it in ja)
                {
                    try
                    {
                        var d = it.ToObject<Dictionary<string, object>>();
                        if (d != null) stack.Add(new Dictionary<string, object>(d, StringComparer.OrdinalIgnoreCase));
                    }
                    catch { }
                }
                ctx.Estado["wf.back.stack"] = stack;
                return new BackState(ctx, stack);
            }

            if (v is List<Dictionary<string, object>> list)
                return new BackState(ctx, list);

            if (v is System.Collections.IEnumerable en)
            {
                var stack = new List<Dictionary<string, object>>();
                foreach (var it in en)
                {
                    if (it is Dictionary<string, object> d)
                        stack.Add(d);
                    else if (it is Newtonsoft.Json.Linq.JObject jo)
                    {
                        try
                        {
                            var d2 = jo.ToObject<Dictionary<string, object>>();
                            if (d2 != null) stack.Add(new Dictionary<string, object>(d2, StringComparer.OrdinalIgnoreCase));
                        }
                        catch { }
                    }
                }
                ctx.Estado["wf.back.stack"] = stack;
                return new BackState(ctx, stack);
            }

            var fallback = new List<Dictionary<string, object>>();
            ctx.Estado["wf.back.stack"] = fallback;
            return new BackState(ctx, fallback);
        }

        private static string GetStringFromState(ContextoEjecucion ctx, string key)
        {
            if (ctx?.Estado == null || string.IsNullOrWhiteSpace(key)) return null;
            if (!ctx.Estado.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static int ExtraerCycle(string datosJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(datosJson)) return 0;
                var tok = Newtonsoft.Json.Linq.JToken.Parse(datosJson);

                // si viene merged (meta/data), usar meta
                var meta = (tok is Newtonsoft.Json.Linq.JObject o && o["meta"] != null) ? o["meta"] : tok;
                var wb = meta["wfBack"];
                if (wb != null && wb["cycle"] != null && int.TryParse(wb["cycle"].ToString(), out var c))
                    return c;

                return 0;
            }
            catch { return 0; }
        }

        private static string ExtraerFrameId(string datosJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(datosJson)) return null;
                var tok = Newtonsoft.Json.Linq.JToken.Parse(datosJson);
                var meta = (tok is Newtonsoft.Json.Linq.JObject o && o["meta"] != null) ? o["meta"] : tok;
                var wb = meta["wfBack"];
                var fid = wb?["frameId"]?.ToString();
                return string.IsNullOrWhiteSpace(fid) ? null : fid;
            }
            catch { return null; }
        }

        private static string ExtraerReturnTo(string datosJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(datosJson)) return null;
                var tok = Newtonsoft.Json.Linq.JToken.Parse(datosJson);
                var meta = (tok is Newtonsoft.Json.Linq.JObject o && o["meta"] != null) ? o["meta"] : tok;
                var wb = meta["wfBack"];
                var rt = wb?["returnToNodeId"]?.ToString();
                return string.IsNullOrWhiteSpace(rt) ? null : rt;
            }
            catch { return null; }
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
                                            SELECT TOP 1 Id, Estado, Resultado, Datos
                                            FROM dbo.WF_Tarea
                                            WHERE WF_InstanciaId = @InstId
                                              AND NodoId         = @NodoId
                                              AND NodoTipo       = @NodoTipo
                                            ORDER BY
                                              CASE WHEN Estado IS NULL OR Estado='Pendiente' OR Estado='Abierta' THEN 0 ELSE 1 END,
                                              Id DESC;", cn))
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
                        Resultado = rd.IsDBNull(2) ? null : rd.GetString(2),
                        Datos = rd.IsDBNull(3) ? null : rd.GetString(3)
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

        private static string BuildMetadataJson(Dictionary<string, object> p, long instanciaId, IDictionary<string, object> wfBack)
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

            if (wfBack != null)
                metaDict["wfBack"] = wfBack;

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
                //scopeKey = null;

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
                cmd.Parameters.Add("@AsignadoA", SqlDbType.NVarChar, 100).Value = (object)asignadoA ?? DBNull.Value;

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
