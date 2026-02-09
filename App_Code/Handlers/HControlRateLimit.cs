using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// control.ratelimit
    /// Limita la tasa de ejecución usando un token bucket en memoria (global al proceso).
    ///
    /// Parameters:
    ///   - key           (string)  Identificador del bucket. Default: nodo.Id
    ///   - maxPerMinute  (int)     Tokens por minuto. Default: 60
    ///   - burst         (int)     Capacidad máxima. Default: = maxPerMinute
    ///   - mode          (string)  "delay" (default) o "error"
    ///   - maxWaitMs     (int)     Máximo de espera cuando mode=delay. Default: 60000
    ///
    /// Return:
    ///   - "always" si pudo consumir token
    ///   - "error"  si no pudo (mode=error o excede maxWaitMs)
    /// </summary>
    public class HControlRateLimit : IManejadorNodo
    {
        public string TipoNodo => "control.ratelimit";

        private class Bucket
        {
            public double Tokens;
            public DateTime LastUtc;
            public object Sync = new object();
        }

        private static readonly ConcurrentDictionary<string, Bucket> _buckets =
            new ConcurrentDictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string key = GetString(p, "key");
            if (string.IsNullOrWhiteSpace(key)) key = nodo.Id;

            int maxPerMinute = GetInt(p, "maxPerMinute", 60);
            if (maxPerMinute < 1) maxPerMinute = 1;
            if (maxPerMinute > 60000) maxPerMinute = 60000;

            int burst = GetInt(p, "burst", maxPerMinute);
            if (burst < 1) burst = 1;
            if (burst > 60000) burst = 60000;

            string mode = GetString(p, "mode");
            if (string.IsNullOrWhiteSpace(mode)) mode = "delay";
            mode = mode.Trim().ToLowerInvariant();

            int maxWaitMs = GetInt(p, "maxWaitMs", 60000);
            if (maxWaitMs < 0) maxWaitMs = 0;
            if (maxWaitMs > 10 * 60 * 1000) maxWaitMs = 10 * 60 * 1000;

            var bucket = _buckets.GetOrAdd(key, _ => new Bucket
            {
                Tokens = burst,
                LastUtc = DateTime.UtcNow
            });

            // tokens por segundo
            double ratePerSec = maxPerMinute / 60.0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                double waitMs = 0;

                lock (bucket.Sync)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = (now - bucket.LastUtc).TotalSeconds;
                    if (elapsed < 0) elapsed = 0;

                    // refill
                    bucket.Tokens = Math.Min(burst, bucket.Tokens + elapsed * ratePerSec);
                    bucket.LastUtc = now;

                    if (bucket.Tokens >= 1.0)
                    {
                        bucket.Tokens -= 1.0;
                        ctx.Log($"[control.ratelimit] OK key={key} tokens={bucket.Tokens.ToString("0.##", CultureInfo.InvariantCulture)}/{burst} rate={maxPerMinute}/min");
                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    // no hay token: calcular espera aproximada hasta 1 token
                    var needed = 1.0 - bucket.Tokens;
                    waitMs = (needed / ratePerSec) * 1000.0;
                    if (waitMs < 0) waitMs = 0;
                }

                if (mode == "error")
                {
                    ctx.Log($"[control.ratelimit] EXCEEDED key={key} (mode=error)");
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                // mode=delay
                int w = (int)Math.Ceiling(waitMs);
                if (w > maxWaitMs)
                {
                    ctx.Log($"[control.ratelimit] EXCEEDED key={key} waitMs={w} > maxWaitMs={maxWaitMs}");
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                if (w > 0)
                {
                    ctx.Log($"[control.ratelimit] Esperando {w}ms (key={key})");
                    await Task.Delay(w, ct);
                }
                else
                {
                    // loop corto
                    await Task.Delay(1, ct);
                }
            }
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def)
        {
            if (p == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;

            if (v is int i) return i;
            if (v is long l) return (int)l;

            var s = Convert.ToString(v);
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            if (int.TryParse(s, out x)) return x;
            return def;
        }
    }
}
