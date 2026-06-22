using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    // Handler para "util.logger": registra un mensaje interpolando ${...} con valores del estado.
    public class HLogger : IManejadorNodo
    {
        public string TipoNodo => "util.logger";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            ct.ThrowIfCancellationRequested();

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Compatibilidad: se aceptan nombres nuevos (level/message) y nombres históricos (nivel/mensaje).
            var levelTpl = GetString(p, "level") ?? GetString(p, "nivel") ?? "Info";
            var msgTpl = GetString(p, "message") ?? GetString(p, "mensaje") ?? string.Empty;

            var level = NormalizeLevel(ctx.ExpandString(levelTpl));
            var msg = ctx.ExpandString(msgTpl) ?? string.Empty;

            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.level", level);
            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.message", msg);
            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.nodeId", nodo.Id ?? string.Empty);

            ctx.Log($"[Logger] [{level}] {msg}");

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null || string.IsNullOrWhiteSpace(key)) return null;
            return p.TryGetValue(key, out var v) ? Convert.ToString(v) : null;
        }

        private static string NormalizeLevel(string level)
        {
            var raw = (level ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "Info";

            if (raw.Equals("warning", StringComparison.OrdinalIgnoreCase)) return "Warn";
            if (raw.Equals("warn", StringComparison.OrdinalIgnoreCase)) return "Warn";
            if (raw.Equals("error", StringComparison.OrdinalIgnoreCase)) return "Error";
            if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)) return "Debug";
            if (raw.Equals("info", StringComparison.OrdinalIgnoreCase)) return "Info";

            return raw;
        }
    }
}
