using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix52: primera integración controlada del Phrase Engine.
    ///
    /// Esta clase NO reemplaza todavía a WfAiMlnetProvider.cs ni decide workflows por sí sola.
    /// Su objetivo es centralizar un análisis léxico/cláusulas reutilizable para que las próximas
    /// etapas puedan dejar de crecer dentro de un único archivo gigante.
    /// </summary>
    public static class WfAiPhraseEngine
    {
        public static WfAiPhraseEngineResult Analyze(string text)
        {
            var result = new WfAiPhraseEngineResult();
            result.OriginalText = text ?? string.Empty;
            result.NormalizedText = WfAiPhraseNormalizer.Normalize(text);
            result.Clauses = WfAiPhraseClauseSplitter.Split(text);

            var patterns = WfAiPhraseLexicon.FindPatterns(text);
            foreach (var pattern in patterns)
            {
                result.AddConcept(pattern.Concept);
                result.AddNodeType(pattern.NodeType);
            }

            result.PrimaryRole = WfAiPhraseNormalizer.NormalizeRole(text) ?? string.Empty;
            result.PrimaryHumanOutcome = WfAiPhraseNormalizer.NormalizeHumanOutcome(text) ?? string.Empty;
            result.FirstNumber = WfAiPhraseNormalizer.ExtractFirstNumber(text) ?? string.Empty;

            return result;
        }
    }

    public class WfAiPhraseEngineResult
    {
        public string OriginalText { get; set; }
        public string NormalizedText { get; set; }
        public List<WfAiPhraseClause> Clauses { get; set; }
        public List<string> Concepts { get; set; }
        public List<string> NodeTypes { get; set; }
        public string PrimaryRole { get; set; }
        public string PrimaryHumanOutcome { get; set; }
        public string FirstNumber { get; set; }

        public WfAiPhraseEngineResult()
        {
            OriginalText = string.Empty;
            NormalizedText = string.Empty;
            Clauses = new List<WfAiPhraseClause>();
            Concepts = new List<string>();
            NodeTypes = new List<string>();
            PrimaryRole = string.Empty;
            PrimaryHumanOutcome = string.Empty;
            FirstNumber = string.Empty;
        }

        public bool HasConcept(string concept)
        {
            if (string.IsNullOrWhiteSpace(concept)) return false;
            foreach (string c in Concepts)
            {
                if (string.Equals(c, concept, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool HasNodeType(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType)) return false;
            foreach (string n in NodeTypes)
            {
                if (string.Equals(n, nodeType, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public void AddConcept(string concept)
        {
            if (string.IsNullOrWhiteSpace(concept)) return;
            foreach (string c in Concepts)
            {
                if (string.Equals(c, concept, StringComparison.OrdinalIgnoreCase)) return;
            }
            Concepts.Add(concept);
        }

        public void AddNodeType(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType)) return;
            foreach (string n in NodeTypes)
            {
                if (string.Equals(n, nodeType, StringComparison.OrdinalIgnoreCase)) return;
            }
            NodeTypes.Add(nodeType);
        }

        public List<WfAiPhraseClauseDebug> DebugClauses()
        {
            var list = new List<WfAiPhraseClauseDebug>();
            foreach (var c in Clauses)
            {
                if (c == null) continue;
                list.Add(new WfAiPhraseClauseDebug
                {
                    Index = c.Index,
                    Text = c.Text,
                    NormalizedText = c.NormalizedText
                });
            }
            return list;
        }
    }

    public class WfAiPhraseClauseDebug
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public string NormalizedText { get; set; }
    }
}
