using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Registry (whitelist) para code.function.
    /// - NO ejecuta código arbitrario.
    /// - Agregás funciones acá, con nombres estables.
    /// </summary>
    public static class CodeFunctionRegistry
    {
        private static readonly Dictionary<string, Func<ContextoEjecucion, JObject, JToken>> _fns =
            new Dictionary<string, Func<ContextoEjecucion, JObject, JToken>>(StringComparer.OrdinalIgnoreCase)
            {
                // string.concat: { "parts": ["a","b",...], "sep": " " }
                ["string.concat"] = (ctx, args) =>
                {
                    var parts = args?["parts"] as JArray;
                    string sep = Convert.ToString(args?["sep"] ?? "");
                    if (parts == null) return "";

                    var xs = parts.Select(p => Convert.ToString(p)).ToArray();
                    return string.Join(sep, xs);
                },

                // math.sum: { "values": [1,2,3] }
                ["math.sum"] = (ctx, args) =>
                {
                    var values = args?["values"] as JArray;
                    if (values == null) return 0;
                    decimal sum = 0;
                    foreach (var v in values)
                    {
                        if (v == null) continue;
                        if (decimal.TryParse(Convert.ToString(v), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                            sum += d;
                    }
                    return sum;
                },

                // json.pick: { "from": <obj>, "path": "a.b.c" }
                ["json.pick"] = (ctx, args) =>
                {
                    var from = args?["from"];
                    string path = Convert.ToString(args?["path"] ?? "");
                    if (from == null || string.IsNullOrWhiteSpace(path)) return JValue.CreateNull();

                    var tok = from.SelectToken(path);
                    return tok ?? JValue.CreateNull();
                }
            };

        public static bool TryInvoke(string name, ContextoEjecucion ctx, JObject args, out JToken result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "falta nombre de función";
                return false;
            }

            if (!_fns.TryGetValue(name.Trim(), out var fn) || fn == null)
            {
                error = $"función no registrada: '{name}'";
                return false;
            }

            try
            {
                result = fn(ctx, args ?? new JObject());
                if (result == null) result = JValue.CreateNull();
                return true;
            }
            catch (Exception ex)
            {
                error = $"error ejecutando '{name}': {ex.Message}";
                return false;
            }
        }
    }
}
