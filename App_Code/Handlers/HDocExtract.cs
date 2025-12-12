using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HDocExtract : IManejadorNodo
    {
        public string TipoNodo => "doc.extract";
        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var rulesJson = GetString(p, "rulesJson");

            // --- Determinar si es modo legacy o modo nuevo ---
            if (!string.IsNullOrWhiteSpace(rulesJson))
            {
                var trimmed = rulesJson.TrimStart();

                if (trimmed.StartsWith("["))
                {
                    var resLegacy = EjecutarModoLegacy(ctx, p, rulesJson);
                    return Task.FromResult(resLegacy);
                }

                if (trimmed.StartsWith("{"))
                {
                    try
                    {
                        var cfg = JsonConvert.DeserializeObject<Dictionary<string, object>>(rulesJson)
                                  ?? new Dictionary<string, object>();

                        var merged = new Dictionary<string, object>(p, StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in cfg)
                            merged[kv.Key] = kv.Value;

                        var resNuevo = EjecutarModoRegex(ctx, merged);
                        return Task.FromResult(resNuevo);
                    }
                    catch (Exception ex)
                    {
                        var msg = "[doc.extract/error] No se pudo interpretar 'rulesJson' como objeto: " + ex.Message;
                        ctx.Log(msg);
                        ctx.Estado["doc.extract.lastError"] = msg;
                        ctx.Estado["wf.error"] = true;
                        return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                    }
                }

                // Formato inválido
                {
                    var msg = "[doc.extract/error] 'rulesJson' debe comenzar con '[' o '{'.";
                    ctx.Log(msg);
                    ctx.Estado["doc.extract.lastError"] = msg;
                    ctx.Estado["wf.error"] = true;
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }
            }

            // Modo nuevo sin rulesJson
            var res = EjecutarModoRegex(ctx, p);
            return Task.FromResult(res);
        }

        // ============================================================
        // MODO NUEVO — REGEX
        // ============================================================

        private ResultadoEjecucion EjecutarModoRegex(
            ContextoEjecucion ctx,
            IDictionary<string, object> p)
        {
            // Antes: default era "archivo"
            // AHORA: TODO UNIFICADO EN input.text
            string origenKey = GetString(p, "origen");
            if (string.IsNullOrWhiteSpace(origenKey))
                origenKey = "input.text";

            if (!ctx.Estado.TryGetValue(origenKey, out var raw) || raw == null)
            {
                return Error(ctx, $"[doc.extract] No se encontró ctx.Estado[\"{origenKey}\"]");
            }

            string texto = Convert.ToString(raw) ?? "";

            // Destino — ahora por default es input.result
            string destino = GetString(p, "destino") ?? "input.result";

            string mode = (GetString(p, "mode") ?? "single").ToLowerInvariant();
            if (mode != "single" && mode != "multi")
                mode = "single";

            string pattern = GetString(p, "regex");
            if (string.IsNullOrWhiteSpace(pattern))
                return Error(ctx, "[doc.extract] Falta 'regex'.");

            var options = ParseRegexOptions(GetString(p, "regexOptions"));
            var fields = CargarFieldConfigs(p, ctx);

            Regex rx;
            try
            {
                rx = new Regex(pattern, options);
            }
            catch (Exception ex)
            {
                return Error(ctx, "[doc.extract] Regex inválida: " + ex.Message);
            }

            var matches = rx.Matches(texto);
            var lista = new List<Dictionary<string, object>>();

            var groupNames = rx.GetGroupNames();

            foreach (Match m in matches)
            {
                if (!m.Success) continue;

                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (fields.Count > 0)
                {
                    foreach (var f in fields)
                    {
                        string groupName = string.IsNullOrWhiteSpace(f.Group) ? f.Name : f.Group;

                        string rawVal = null;
                        if (m.Groups[groupName] != null && m.Groups[groupName].Success)
                            rawVal = m.Groups[groupName].Value;

                        row[f.Name] = ConvertByType(rawVal, f.Type);
                    }
                }
                else
                {
                    foreach (var gn in groupNames)
                    {
                        if (gn == "0") continue;
                        var g = m.Groups[gn];
                        if (g.Success)
                            row[gn] = g.Value;
                    }
                }

                lista.Add(row);
                if (mode == "single") break;
            }

            ctx.Estado["doc.extract.lastDestino"] = destino;
            ctx.Estado["doc.extract.lastMode"] = mode;
            ctx.Estado["doc.extract.lastMatches"] = lista.Count;

            if (mode == "single")
                SetEstadoPath(ctx.Estado, destino, lista.Count > 0 ? lista[0] : null);
            else
                SetEstadoPath(ctx.Estado, destino, lista);

            ctx.Log($"[doc.extract] ModoRegex: matches={lista.Count}, destino='{destino}'.");

            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        // ============================================================
        // MODO LEGACY
        // ============================================================

        private ResultadoEjecucion EjecutarModoLegacy(
            ContextoEjecucion ctx,
            IDictionary<string, object> p,
            string rulesJson)
        {
            // Antes: default era "archivo"
            // AHORA: TODO UNIFICADO EN input.text
            string origenKey = GetString(p, "origen");
            if (string.IsNullOrWhiteSpace(origenKey))
                origenKey = "input.text";

            if (!ctx.Estado.TryGetValue(origenKey, out var raw) || raw == null)
            {
                ctx.Log($"[doc.extract/error] (legacy) No se encontró ctx.Estado[\"{origenKey}\"]");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            string texto = Convert.ToString(raw) ?? "";
            var lineas = NormalizarLineas(texto);
            var reglas = CargarReglasDesdeJson(rulesJson, ctx);

            int fixedCount = 0, regexCount = 0;

            foreach (var reg in reglas)
            {
                if (string.IsNullOrWhiteSpace(reg.Campo))
                    continue;

                // Nueva convención: input.<campo>
                string key = "input." + reg.Campo.Trim();

                // Fixed
                if (reg.Linea.HasValue && reg.ColDesde.HasValue && reg.Largo.HasValue)
                {
                    string valor = ExtraerFixed(lineas, reg, ctx);
                    if (valor != null)
                    {
                        ctx.Estado[key] = valor;
                        fixedCount++;
                    }
                    continue;
                }

                // Regex
                if (!string.IsNullOrWhiteSpace(reg.Regex))
                {
                    string valor = ExtraerRegex(texto, reg, ctx);
                    if (valor != null)
                    {
                        ctx.Estado[key] = valor;
                        regexCount++;
                    }
                    continue;
                }
            }

            ctx.Log($"[doc.extract] (legacy) Reglas aplicadas: fixed={fixedCount}, regex={regexCount}.");
            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        // ============================================================
        // Helpers...
        // ============================================================

        private static string[] NormalizarLineas(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return Array.Empty<string>();
            texto = texto.Replace("\r\n", "\n").Replace('\r', '\n');
            return texto.Split('\n');
        }

        private class ReglaDocExtract
        {
            [JsonProperty("campo")]
            public string Campo { get; set; }

            [JsonProperty("linea")]
            public int? Linea { get; set; }

            [JsonProperty("colDesde")]
            public int? ColDesde { get; set; }

            [JsonProperty("largo")]
            public int? Largo { get; set; }

            [JsonProperty("regex")]
            public string Regex { get; set; }

            [JsonProperty("grupo")]
            public int? Grupo { get; set; }
        }

        private static List<ReglaDocExtract> CargarReglasDesdeJson(string json, ContextoEjecucion ctx)
        {
            var result = new List<ReglaDocExtract>();
            try
            {
                result = JsonConvert.DeserializeObject<List<ReglaDocExtract>>(json)
                         ?? new List<ReglaDocExtract>();
            }
            catch (Exception ex)
            {
                ctx?.Log("[doc.extract/error] (legacy) No se pudo parsear: " + ex.Message);
            }
            return result;
        }

        private static string ExtraerFixed(string[] lineas, ReglaDocExtract reg, ContextoEjecucion ctx)
        {
            int idxLinea = (reg.Linea ?? 0) - 1;
            if (idxLinea < 0 || idxLinea >= lineas.Length)
            {
                ctx.Log($"[doc.extract/fixed/skip] Línea {reg.Linea} fuera de rango.");
                return null;
            }

            string linea = lineas[idxLinea] ?? "";
            int start = Math.Max(0, (reg.ColDesde ?? 1) - 1);
            int largo = reg.Largo ?? 0;

            if (start >= linea.Length || largo <= 0)
            {
                ctx.Log($"[doc.extract/fixed/skip] Col/largo fuera de rango.");
                return null;
            }

            largo = Math.Min(largo, linea.Length - start);
            string valor = linea.Substring(start, largo).Trim();

            ctx.Log($"[doc.extract/fixed] input.{reg.Campo} = \"{valor}\"");
            return valor;
        }

        private static string ExtraerRegex(string texto, ReglaDocExtract reg, ContextoEjecucion ctx)
        {
            var match = Regex.Match(texto, reg.Regex, RegexOptions.Multiline);
            if (!match.Success)
            {
                ctx.Log($"[doc.extract/regex/skip] Sin match para '{reg.Regex}'");
                return null;
            }

            int grupo = reg.Grupo ?? 1;
            string valor = match.Groups.Count > grupo
                ? match.Groups[grupo].Value.Trim()
                : match.Value.Trim();

            ctx.Log($"[doc.extract/regex] input.{reg.Campo} = \"{valor}\"");
            return valor;
        }

        private static List<FieldConfig> CargarFieldConfigs(IDictionary<string, object> p, ContextoEjecucion ctx)
        {
            var list = new List<FieldConfig>();

            if (p == null || !p.TryGetValue("fields", out var raw) || raw == null)
                return list;

            var json = Convert.ToString(raw);
            if (string.IsNullOrWhiteSpace(json))
                return list;

            try
            {
                list = JsonConvert.DeserializeObject<List<FieldConfig>>(json)
                       ?? new List<FieldConfig>();
            }
            catch (Exception ex)
            {
                ctx?.Log("[doc.extract/error] No se pudo parsear 'fields': " + ex.Message);
            }

            return list;
        }

        private class FieldConfig
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("group")]
            public string Group { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }
        }

        private static RegexOptions ParseRegexOptions(string opts)
        {
            if (string.IsNullOrWhiteSpace(opts))
                return RegexOptions.None;

            var result = RegexOptions.None;
            var parts = opts.Split(new[] { '|', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var p in parts)
            {
                switch (p.Trim().ToLowerInvariant())
                {
                    case "multiline":
                    case "m":
                        result |= RegexOptions.Multiline;
                        break;

                    case "ignorecase":
                    case "i":
                        result |= RegexOptions.IgnoreCase;
                        break;

                    case "singleline":
                    case "s":
                        result |= RegexOptions.Singleline;
                        break;

                    case "explicitcapture":
                    case "n":
                        result |= RegexOptions.ExplicitCapture;
                        break;
                }
            }

            return result;
        }

        private static object ConvertByType(string value, string typeName)
        {
            if (value == null)
                return null;

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(typeName))
                return value;

            switch (typeName.Trim().ToLowerInvariant())
            {
                case "int":
                case "int32":
                    if (int.TryParse(value, out var i)) return i;
                    return value;

                case "long":
                case "int64":
                    if (long.TryParse(value, out var l)) return l;
                    return value;

                case "decimal":
                    if (decimal.TryParse(value, out var d)) return d;
                    return value;

                case "double":
                    if (double.TryParse(value, out var dbl)) return dbl;
                    return value;

                case "bool":
                case "boolean":
                    if (bool.TryParse(value, out var b)) return b;
                    if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(value, "N", StringComparison.OrdinalIgnoreCase)) return false;
                    return value;

                case "date":
                case "datetime":
                    if (DateTime.TryParse(value, out var dt)) return dt;
                    return value;

                default:
                    return value;
            }
        }

        private static ResultadoEjecucion Error(ContextoEjecucion ctx, string msg)
        {
            ctx.Log(msg);
            ctx.Estado["doc.extract.lastError"] = msg;
            ctx.Estado["wf.error"] = true;
            return new ResultadoEjecucion { Etiqueta = "error" };
        }

        /// <summary>
        /// Guarda un valor siguiendo rutas con puntos, ej. "input.cliente.nombre".
        /// </summary>
        private static void SetEstadoPath(IDictionary<string, object> root, string path, object value)
        {
            if (root == null || string.IsNullOrWhiteSpace(path)) return;

            var parts = path.Split('.');
            var current = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var key = parts[i];
                if (!current.TryGetValue(key, out var next) || !(next is IDictionary<string, object> dict))
                {
                    dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    current[key] = dict;
                }
                current = dict;
            }
            var lastKey = parts[parts.Length - 1];
            current[lastKey] = value;
            
        }
    }
}
