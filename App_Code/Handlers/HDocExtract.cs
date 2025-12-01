using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "doc.extract".
    /// Lee texto desde ctx.Estado[origen] y aplica una lista de reglas dinámicas
    /// definidas en JSON (parámetro "rulesJson").
    ///
    /// Cada regla puede ser:
    ///   - fija:   { "campo": "...", "linea": 3, "colDesde": 9, "largo": 11 }
    ///   - regex:  { "campo": "...", "regex": "patrón", "grupo": 1 }
    ///
    /// Los valores se guardan en ctx.Estado["doc.{campo}"].
    /// </summary>
    public class HDocExtract : IManejadorNodo
    {
        public string TipoNodo => "doc.extract";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // 1) De dónde saco el texto (key en ctx.Estado)
            string origenKey = GetString(p, "origen");
            if (string.IsNullOrWhiteSpace(origenKey))
                origenKey = "archivo"; // por defecto, lo que deja file.read

            if (!ctx.Estado.TryGetValue(origenKey, out var raw) || raw == null)
            {
                ctx.Log($"[doc.extract/error] No se encontró ctx.Estado[\"{origenKey}\"] o es null.");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            string texto = Convert.ToString(raw) ?? string.Empty;
            var lineas = NormalizarLineas(texto);

            // 2) Cargar reglas dinámicas desde JSON
            var reglas = CargarReglasDesdeJson(p, ctx);

            int fixedCount = 0;
            int regexCount = 0;

            foreach (var reg in reglas)
            {
                if (string.IsNullOrWhiteSpace(reg.Campo))
                    continue;

                string key = "doc." + reg.Campo.Trim();

                // Regla "fixed"
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

                // Regla "regex"
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

            ctx.Log($"[doc.extract] Reglas aplicadas: fixed={fixedCount}, regex={regexCount}.");

            // Por ahora, siempre seguimos por "always"
            return Task.FromResult(new ResultadoEjecucion
            {
                Etiqueta = "always"
            });
        }

        // ============================================================
        // Helpers
        // ============================================================

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static string[] NormalizarLineas(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return Array.Empty<string>();

            // Unificar saltos de línea
            texto = texto.Replace("\r\n", "\n").Replace('\r', '\n');
            return texto.Split('\n');
        }

        private static List<ReglaDocExtract> CargarReglasDesdeJson(
            IDictionary<string, object> p,
            ContextoEjecucion ctx)
        {
            var result = new List<ReglaDocExtract>();

            if (p == null || !p.TryGetValue("rulesJson", out var raw) || raw == null)
            {
                ctx?.Log("[doc.extract] No se encontró parámetro 'rulesJson'.");
                return result;
            }

            var json = Convert.ToString(raw);
            if (string.IsNullOrWhiteSpace(json))
            {
                ctx?.Log("[doc.extract] 'rulesJson' está vacío.");
                return result;
            }

            try
            {
                result = JsonConvert.DeserializeObject<List<ReglaDocExtract>>(json)
                         ?? new List<ReglaDocExtract>();
            }
            catch (Exception ex)
            {
                ctx?.Log("[doc.extract/error] No se pudo parsear rulesJson: " + ex.Message);
            }

            return result;
        }

        private static string ExtraerFixed(
            string[] lineas,
            ReglaDocExtract reg,
            ContextoEjecucion ctx)
        {
            int idxLinea = (reg.Linea ?? 0) - 1; // 1-based → 0-based
            if (idxLinea < 0 || idxLinea >= lineas.Length)
            {
                ctx.Log($"[doc.extract/fixed/skip] Línea {reg.Linea} fuera de rango (total={lineas.Length}).");
                return null;
            }

            string linea = lineas[idxLinea] ?? string.Empty;

            int col1 = Math.Max(1, reg.ColDesde ?? 1);
            int start = col1 - 1;
            int largo = Math.Max(0, reg.Largo ?? 0);

            if (start >= linea.Length || largo <= 0)
            {
                ctx.Log($"[doc.extract/fixed/skip] colDesde/largo fuera de rango (línea={reg.Linea}, col={reg.ColDesde}, largo={reg.Largo}).");
                return null;
            }

            int lenReal = Math.Min(largo, linea.Length - start);
            string valor = linea.Substring(start, lenReal).Trim();

            ctx.Log($"[doc.extract/fixed] doc.{reg.Campo} = \"{valor}\" (línea={reg.Linea}, col={reg.ColDesde}, largo={reg.Largo}).");
            return valor;
        }

        private static string ExtraerRegex(
            string texto,
            ReglaDocExtract reg,
            ContextoEjecucion ctx)
        {
            string pattern = reg.Regex;
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            var match = Regex.Match(texto, pattern, RegexOptions.Multiline);
            if (!match.Success)
            {
                ctx.Log($"[doc.extract/regex/skip] Sin match para regex '{pattern}'.");
                return null;
            }

            int grupo = reg.Grupo ?? 1;
            string valor;

            if (grupo >= 0 && grupo < match.Groups.Count)
                valor = match.Groups[grupo].Value.Trim();
            else
                valor = match.Value.Trim();

            ctx.Log($"[doc.extract/regex] doc.{reg.Campo} = \"{valor}\" (grupo={grupo}).");
            return valor;
        }

        // Clase que modela cada regla del JSON
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
    }
}
