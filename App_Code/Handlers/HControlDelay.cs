using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo "control.delay"
    /// - Lee segundos (o ms) desde parámetros.
    /// - Hace un Task.Delay.
    /// - Loguea lo que está haciendo.
    /// - Siempre devuelve Etiqueta = "always".
    /// </summary>
    public class HControlDelay : IManejadorNodo
    {
        public string TipoNodo => "control.delay";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // ms tiene prioridad; segundos es un atajo amigable.
            int ms = 0;

            if (TryGetInt(p, "ms", out var msParam))
                ms = msParam;
            else if (TryGetInt(p, "segundos", out var s))
                ms = s * 1000;

            if (ms < 0) ms = 0;

            string mensaje = null;
            if (p.TryGetValue("mensaje", out var rawMsg) && rawMsg != null)
                mensaje = Convert.ToString(rawMsg);

            if (!string.IsNullOrWhiteSpace(mensaje))
            {
                var expanded = ctx.ExpandString(mensaje);
                ctx.Log($"[control.delay] {expanded} (espera={ms} ms)");
            }
            else
            {
                ctx.Log($"[control.delay] Esperando {ms} ms...");
            }

            if (ms > 0)
            {
                await Task.Delay(ms, ct);
            }

            return new ResultadoEjecucion
            {
                Etiqueta = "always"
            };
        }

        private static bool TryGetInt(IDictionary<string, object> p, string key, out int value)
        {
            value = 0;
            if (p == null) return false;

            if (!p.TryGetValue(key, out var raw) || raw == null)
                return false;

            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (int.TryParse(Convert.ToString(raw), out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }
    }
}
