// App_Code/Handlers/HControlRetry.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// control.retry: “decorador” de control. El motor reintenta internamente el nodo siguiente.
    ///
    /// Parámetros:
    ///   - reintentos : int (opcional) cantidad de reintentos (default 3)  => total intentos = reintentos + 1
    ///   - backoffMs  : int (opcional) espera entre intentos en ms (default 500)
    ///   - message    : string (opcional) texto para log
    ///
    /// Etiquetas:
    ///   - always
    /// </summary>
    public class HControlRetry : IManejadorNodo
    {
        public string TipoNodo => "control.retry";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>();

            int reintentos = ParseInt(p, "reintentos", 3);
            int backoffMs = ParseInt(p, "backoffMs", 500);

            // clamps razonables
            if (reintentos < 0) reintentos = 0;
            if (reintentos > 50) reintentos = 50;     // evita loops enormes por error
            if (backoffMs < 0) backoffMs = 0;
            if (backoffMs > 600000) backoffMs = 600000; // 10 min

            string msg = null;
            if (p.TryGetValue("message", out var mv) && mv != null)
                msg = ctx.ExpandString(mv.ToString());

            if (!string.IsNullOrWhiteSpace(msg))
                ctx.Log($"[Retry] {msg} (reintentos={reintentos}, backoffMs={backoffMs})");
            else
                ctx.Log($"[Retry] configurado (reintentos={reintentos}, backoffMs={backoffMs})");

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        private static int ParseInt(IDictionary<string, object> p, string key, int def)
        {
            if (p == null || string.IsNullOrWhiteSpace(key)) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;

            try
            {
                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (v is decimal d) return (int)d;
                if (v is double db) return (int)db;
                var s = v.ToString().Trim();
                if (string.IsNullOrEmpty(s)) return def;

                // soportar coma/punto
                s = s.Replace(",", ".");
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                    return x;

                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dd))
                    return (int)dd;

                return def;
            }
            catch { return def; }
        }
    }
}
