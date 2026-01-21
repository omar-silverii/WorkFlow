// App_Code/Handlers/HControlDelay.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// control.delay: pausa la ejecución por un tiempo.
    ///
    /// Parámetros:
    ///   - ms      : int (opcional) milisegundos
    ///   - seconds : int/decimal (opcional) segundos (si no viene ms)
    ///   - message : string (opcional) texto para log
    ///
    /// Regla:
    ///   - Si viene ms, se usa ms.
    ///   - Si no viene ms y viene seconds, se usa seconds.
    ///   - Si no viene ninguno o es <= 0, no duerme.
    ///
    /// Etiquetas:
    ///   - always
    /// </summary>
    public class HControlDelay : IManejadorNodo
    {
        public string TipoNodo => "control.delay";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            ct.ThrowIfCancellationRequested();

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            int ms = GetInt(p, "ms", 0);

            if (ms <= 0)
            {
                // seconds puede venir como string, int o decimal. Lo soportamos.
                var secObj = GetObj(p, "seconds");
                if (secObj != null)
                {
                    var secStr = Convert.ToString(secObj);
                    if (!string.IsNullOrWhiteSpace(secStr))
                    {
                        // aceptar "1.5" con punto o coma
                        if (double.TryParse(secStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var s1) ||
                            double.TryParse(secStr, NumberStyles.Any, CultureInfo.GetCultureInfo("es-AR"), out s1))
                        {
                            ms = (int)Math.Round(s1 * 1000.0);
                        }
                    }
                }
            }

            string msg = GetString(p, "message");
            if (!string.IsNullOrWhiteSpace(msg))
                msg = ctx.ExpandString(msg);

            if (ms <= 0)
            {
                ctx.Log("[control.delay] sin demora (ms<=0).");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }

            // clamp razonable para evitar overflow/locuras
            if (ms > 24 * 60 * 60 * 1000) ms = 24 * 60 * 60 * 1000;

            if (!string.IsNullOrWhiteSpace(msg))
                ctx.Log($"[control.delay] {msg} (ms={ms}).");
            else
                ctx.Log($"[control.delay] durmiendo {ms} ms...");

            await Task.Delay(ms, ct);

            ctx.Log("[control.delay] OK.");
            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        // ===================== helpers =====================

        private static object GetObj(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v)) return v;
            return null;
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static int GetInt(IDictionary<string, object> p, string key, int def)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (int.TryParse(Convert.ToString(v), out var i)) return i;
            }
            return def;
        }
    }
}
