using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix51: separador de cláusulas para el Constructor IA v2.
    ///
    /// No se conecta aún al parser actual. Sirve para que la próxima etapa deje de depender
    /// de un único bloque de texto gigante y pueda interpretar frase por frase.
    /// </summary>
    public static class WfAiPhraseClauseSplitter
    {
        public static List<WfAiPhraseClause> Split(string text)
        {
            var result = new List<WfAiPhraseClause>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var prepared = text.Replace("\r", " ").Replace("\n", ". ");

            // Separadores humanos habituales. No es un parser gramatical: solo crea piezas manejables.
            prepared = Regex.Replace(prepared, @"\b(despues|después)\b", ". después", RegexOptions.IgnoreCase);
            prepared = Regex.Replace(prepared, @"\bluego\b", ". luego", RegexOptions.IgnoreCase);
            prepared = Regex.Replace(prepared, @"\bcaso contrario\b", ". caso contrario", RegexOptions.IgnoreCase);
            prepared = Regex.Replace(prepared, @"\bsi no\b", ". si no", RegexOptions.IgnoreCase);
            prepared = Regex.Replace(prepared, @"\bsi\b", ". si", RegexOptions.IgnoreCase);

            var parts = Regex.Split(prepared, @"[\.\;]+");
            var index = 0;
            foreach (var raw in parts)
            {
                var t = (raw ?? string.Empty).Trim();
                if (t.Length == 0) continue;

                result.Add(new WfAiPhraseClause
                {
                    Index = index++,
                    Text = t,
                    NormalizedText = WfAiPhraseNormalizer.Normalize(t)
                });
            }

            return result;
        }
    }

    public class WfAiPhraseClause
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public string NormalizedText { get; set; }
    }
}
