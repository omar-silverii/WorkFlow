using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms

{//    // ====== NUEVO: Handler SWITCH ======
    public class ManejadorSwitch : IManejadorNodo
    {
        public string TipoNodo => "control.switch";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string elegido = null;

            if (p.TryGetValue("casos", out var casosObj) && casosObj is Newtonsoft.Json.Linq.JObject jobj)
            {
                foreach (var prop in jobj.Properties())
                {
                    var label = prop.Name;
                    var expr = prop.Value?.ToString();
                    string logExpr;
                    bool ok = HIf.Evaluar(expr, ctx, out logExpr);
                    ctx.Log($"[Switch] caso '{label}' => {(ok ? "True" : "False")} ({logExpr})");
                    if (ok) { elegido = label; break; }
                }
            }

            if (elegido == null)
            {
                if (p.TryGetValue("default", out var defObj))
                    elegido = Convert.ToString(defObj);
                if (string.IsNullOrWhiteSpace(elegido)) elegido = "always";
                ctx.Log($"[Switch] default => '{elegido}'");
            }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = elegido });
        }
    }
}