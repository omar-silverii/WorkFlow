using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            var msg = ExpandMessage(ctx, msgTpl);

            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.level", level);
            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.message", msg);
            ContextoEjecucion.SetPath(ctx.Estado, "logger.last.nodeId", nodo.Id ?? string.Empty);

            ctx.Log($"[Logger] [{level}] {msg}");

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        private static readonly Regex TemplateRegex = new Regex(
            @"\$\{(?<path>[^}]+)\}", RegexOptions.Compiled);

        private static string ExpandMessage(ContextoEjecucion ctx, string template)
        {
            if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

            return TemplateRegex.Replace(template, match =>
            {
                var path = match.Groups["path"].Value.Trim();
                var value = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                return FormatValue(value);
            });
        }

        private static string FormatValue(object value)
        {
            if (value == null) return string.Empty;

            try
            {
                if (value is JToken token)
                    return token.ToString(Formatting.None);

                if (value is IDictionary)
                    return JsonConvert.SerializeObject(value, Formatting.None);

                if (!(value is string) && value is IEnumerable)
                    return JsonConvert.SerializeObject(value, Formatting.None);
            }
            catch
            {
                // Compatibilidad defensiva: si un objeto no puede serializarse,
                // el Logger conserva el comportamiento histórico de Convert.ToString.
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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
