using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix51: normalizador central para el futuro Constructor IA v2.
    ///
    /// No cambia el comportamiento actual por sí solo. Su objetivo es sacar del proveedor
    /// principal las operaciones repetidas de normalización de frases humanas.
    ///
    /// Mantener simple y determinístico: no interpreta workflows, solo prepara texto.
    /// </summary>
    public static class WfAiPhraseNormalizer
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var s = RemoveDiacritics(text).ToLowerInvariant();

            s = s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            s = s.Replace("→", " -> ").Replace("=>", " -> ");
            s = s.Replace("/", " / ");
            s = s.Replace("$", " ");

            // Mantener puntos/comas útiles para números, pero separar puntuación textual.
            s = Regex.Replace(s, "[\\(\\)\\[\\]\\{\\}\"']", " ");
            s = Regex.Replace(s, "\\s+", " ").Trim();

            return s;
        }

        public static bool ContainsAny(string normalizedText, IEnumerable<string> normalizedPhrases)
        {
            if (string.IsNullOrWhiteSpace(normalizedText) || normalizedPhrases == null) return false;

            foreach (var p in normalizedPhrases)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (normalizedText.Contains(Normalize(p))) return true;
            }

            return false;
        }

        public static string ExtractFirstNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var m = Regex.Match(text, @"(?<![A-Za-z0-9])(?:\d{1,3}(?:[\.\s]\d{3})+|\d+)(?:,\d+)?");
            if (!m.Success) return null;

            var n = m.Value.Trim();
            n = n.Replace(" ", string.Empty).Replace(".", string.Empty);
            n = n.Replace(",", ".");
            return n;
        }

        public static string NormalizeRole(string text)
        {
            var n = Normalize(text);
            if (string.IsNullOrWhiteSpace(n)) return null;

            if (n.Contains("compras")) return "COMPRAS";
            if (n.Contains("adm_fin") || n.Contains("administracion") || n.Contains("admin fin") || n.Contains("administracion financiera")) return "ADM_FIN";
            if (n.Contains("dir_general") || n.Contains("direccion") || n.Contains("direccion general") || n.Contains("gerencia")) return "DIR_GENERAL";
            if (n.Contains("legales") || n.Contains("legal")) return "LEGALES";

            return null;
        }

        public static string NormalizeHumanOutcome(string text)
        {
            var n = Normalize(text);
            if (string.IsNullOrWhiteSpace(n)) return null;

            if (n.Contains("no apto") || n.Contains("rechaza") || n.Contains("rechazada") || n.Contains("rechazado") || n.Contains("desaprueba")) return "no_apto";
            if (n.Contains("apto") || n.Contains("aprueba") || n.Contains("aprobada") || n.Contains("aprobado")) return "apto";

            return null;
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            for (int i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
