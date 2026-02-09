using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// control.parallel
    /// Ejecuta ramas declaradas en Parameters.branches y vuelve por "always" cuando todas terminaron.
    ///
    /// Requiere:
    /// - branches: array de nodeIds (string)
    /// - joinNodeId: nodeId donde deben detenerse las ramas (ej: control.join)
    ///
    /// Ejecución: en serie (seguro con ctx.Estado Dictionary).
    /// </summary>
    public class HParallel : IManejadorNodo
    {
        public string TipoNodo => "control.parallel";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var branches = LeerStringArray(nodo.Parameters, "branches");
            var joinNodeId = LeerString(nodo.Parameters, "joinNodeId");

            if (branches.Count == 0)
            {
                ctx.Log("[control.parallel] Sin branches. Continúa por 'always'.");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }

            if (string.IsNullOrWhiteSpace(joinNodeId))
            {
                ctx.Log("[control.parallel] Falta Parameters.joinNodeId.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            var items = System.Web.HttpContext.Current?.Items ?? Intranet.WorkflowStudio.Runtime.WorkflowAmbient.Items.Value;
            if (items == null)
            {
                ctx.Log("[control.parallel] No hay Items/Ambient disponible.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            var wf = items["wf.def"] as WorkflowDef;
            var motor = items["wf.motor"] as MotorFlujo;

            if (wf == null || motor == null)
            {
                ctx.Log("[control.parallel] Falta wf.def / wf.motor en Items.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            ctx.Log($"[control.parallel] Ejecutando {branches.Count} rama(s) hasta joinNodeId={joinNodeId}...");

            foreach (var startId in branches)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(startId))
                    continue;

                if (wf.Nodes == null || !wf.Nodes.ContainsKey(startId))
                {
                    ctx.Log($"[control.parallel] Rama inválida: nodeId '{startId}' no existe.");
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                // Ejecuta la rama desde startId y se DETIENE antes de ejecutar el joinNodeId
                await motor.EjecutarDesdeNodoAsync(wf, ctx, startId, stopNodeId: joinNodeId, ct: ct);
            }

            ctx.Log("[control.parallel] OK. Continúa por 'always'.");
            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        private static string LeerString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static List<string> LeerStringArray(Dictionary<string, object> p, string key)
        {
            var list = new List<string>();
            if (p == null) return list;
            if (!p.TryGetValue(key, out var v) || v == null) return list;

            if (v is JArray ja)
            {
                foreach (var x in ja)
                {
                    var s = x?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            if (v is object[] arr)
            {
                foreach (var x in arr)
                {
                    var s = x == null ? null : Convert.ToString(x);
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            if (v is List<object> lo)
            {
                foreach (var x in lo)
                {
                    var s = x == null ? null : Convert.ToString(x);
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            if (v is string csv && csv.Contains(","))
            {
                foreach (var part in csv.Split(','))
                {
                    var s = part?.Trim();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            if (v is string single && !string.IsNullOrWhiteSpace(single))
            {
                list.Add(single.Trim());
                return list;
            }

            return list;
        }
    }

    /// <summary>
    /// control.join: nodo visual + punto de detención de ramas.
    /// </summary>
    public class HJoin : IManejadorNodo
    {
        public string TipoNodo => "control.join";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            ctx.Log("[control.join] Join alcanzado. Continúa por 'always'.");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}
