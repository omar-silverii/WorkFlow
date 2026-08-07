using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix84a: vocabulario estable para el contrato universal de construcción asistida.
    /// No ejecuta nodos ni modifica el plan legacy; describe cómo debe interpretarse cada dato.
    /// </summary>
    public static class WfAiInterpretationStatus
    {
        public const string Resolved = "resolved";
        public const string Inferred = "inferred";
        public const string Ambiguous = "ambiguous";
        public const string Missing = "missing";
        public const string Unrecognized = "unrecognized";
    }

    public static class WfAiInferencePolicy
    {
        public const string SafeDefault = "safe_default";
        public const string VisibleInference = "visible_inference";
        public const string AskIfMissing = "ask_if_missing";
        public const string NeverInfer = "never_infer";
    }

    public static class WfAiControlKind
    {
        public const string Text = "text";
        public const string Number = "number";
        public const string Select = "select";
        public const string Boolean = "boolean";
        public const string RoleOrUser = "role_or_user";
        public const string AvailableData = "available_data";
        public const string PayloadEditor = "payload_editor";
        public const string ConditionBuilder = "condition_builder";
    }

    public class WfAiParameterContract
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("dataType")]
        public string DataType { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("inferencePolicy")]
        public string InferencePolicy { get; set; }

        [JsonProperty("defaultValue")]
        public JToken DefaultValue { get; set; }

        [JsonProperty("clarificationQuestion")]
        public string ClarificationQuestion { get; set; }

        [JsonProperty("controlKind")]
        public string ControlKind { get; set; }

        [JsonProperty("options")]
        public List<string> Options { get; set; }

        [JsonProperty("importantDecision")]
        public bool ImportantDecision { get; set; }

        [JsonProperty("placeholderValues")]
        public List<string> PlaceholderValues { get; set; }

        public WfAiParameterContract()
        {
            Options = new List<string>();
            PlaceholderValues = new List<string>();
        }
    }

    /// <summary>
    /// Permite expresar alternativas de parámetros sin codificar la regla en la pantalla.
    /// Ejemplos: human.task necesita rol O usuarioAsignado; control.if necesita rules O expression O field+op.
    /// </summary>
    public class WfAiAlternativeRequirement
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("alternatives")]
        public List<List<string>> Alternatives { get; set; }

        [JsonProperty("clarificationQuestion")]
        public string ClarificationQuestion { get; set; }

        [JsonProperty("controlKind")]
        public string ControlKind { get; set; }

        [JsonProperty("blocking")]
        public bool Blocking { get; set; }

        public WfAiAlternativeRequirement()
        {
            Alternatives = new List<List<string>>();
            Blocking = true;
        }
    }


    public class WfAiAmbiguityRule
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("phraseFragments")]
        public List<string> PhraseFragments { get; set; }

        [JsonProperty("question")]
        public string Question { get; set; }

        [JsonProperty("controlKind")]
        public string ControlKind { get; set; }

        [JsonProperty("options")]
        public List<string> Options { get; set; }

        [JsonProperty("blocking")]
        public bool Blocking { get; set; }

        [JsonProperty("contextResolverKey")]
        public string ContextResolverKey { get; set; }

        public WfAiAmbiguityRule()
        {
            PhraseFragments = new List<string>();
            Options = new List<string>();
            Blocking = true;
        }
    }

    public class WfAiNodeConstructionContract
    {
        [JsonProperty("nodeType")]
        public string NodeType { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("humanIntent")]
        public string HumanIntent { get; set; }

        [JsonProperty("humanPhrases")]
        public List<string> HumanPhrases { get; set; }

        [JsonProperty("parameters")]
        public List<WfAiParameterContract> Parameters { get; set; }

        [JsonProperty("alternativeRequirements")]
        public List<WfAiAlternativeRequirement> AlternativeRequirements { get; set; }

        [JsonProperty("ambiguityRules")]
        public List<WfAiAmbiguityRule> AmbiguityRules { get; set; }

        [JsonProperty("inputFields")]
        public List<string> InputFields { get; set; }

        [JsonProperty("outputFields")]
        public List<string> OutputFields { get; set; }

        [JsonProperty("dynamicOutputPrefixes")]
        public List<string> DynamicOutputPrefixes { get; set; }

        [JsonProperty("validations")]
        public List<string> Validations { get; set; }

        [JsonProperty("summaryTemplate")]
        public string SummaryTemplate { get; set; }

        public WfAiNodeConstructionContract()
        {
            HumanPhrases = new List<string>();
            Parameters = new List<WfAiParameterContract>();
            AlternativeRequirements = new List<WfAiAlternativeRequirement>();
            AmbiguityRules = new List<WfAiAmbiguityRule>();
            InputFields = new List<string>();
            OutputFields = new List<string>();
            DynamicOutputPrefixes = new List<string>();
            Validations = new List<string>();
        }

        public WfAiParameterContract FindParameter(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (WfAiParameterContract item in Parameters)
            {
                if (item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }
    }

    public class WfAiParameterInterpretation
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("value")]
        public JToken Value { get; set; }

        [JsonProperty("inferencePolicy")]
        public string InferencePolicy { get; set; }

        [JsonProperty("blocking")]
        public bool Blocking { get; set; }

        [JsonProperty("explanation")]
        public string Explanation { get; set; }
    }

    public class WfAiNodeInterpretation
    {
        [JsonProperty("actionIndex")]
        public int ActionIndex { get; set; }

        [JsonProperty("nodeType")]
        public string NodeType { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("parameters")]
        public List<WfAiParameterInterpretation> Parameters { get; set; }

        [JsonProperty("outputFields")]
        public List<string> OutputFields { get; set; }

        [JsonProperty("dynamicOutputPrefixes")]
        public List<string> DynamicOutputPrefixes { get; set; }

        public WfAiNodeInterpretation()
        {
            Parameters = new List<WfAiParameterInterpretation>();
            OutputFields = new List<string>();
            DynamicOutputPrefixes = new List<string>();
        }
    }

    public class WfAiClarification
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("actionIndex")]
        public int ActionIndex { get; set; }

        [JsonProperty("nodeType")]
        public string NodeType { get; set; }

        [JsonProperty("nodeLabel")]
        public string NodeLabel { get; set; }

        [JsonProperty("parameter")]
        public string Parameter { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("question")]
        public string Question { get; set; }

        [JsonProperty("controlKind")]
        public string ControlKind { get; set; }

        [JsonProperty("options")]
        public List<string> Options { get; set; }

        [JsonProperty("currentValue")]
        public JToken CurrentValue { get; set; }

        [JsonProperty("blocking")]
        public bool Blocking { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        public WfAiClarification()
        {
            Options = new List<string>();
            Blocking = true;
        }
    }

    public class WfAiInterpretationDraft
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("sourceText")]
        public string SourceText { get; set; }

        [JsonProperty("fingerprint")]
        public string Fingerprint { get; set; }

        [JsonProperty("coveredNodeTypes")]
        public List<string> CoveredNodeTypes { get; set; }

        [JsonProperty("notCoveredNodeTypes")]
        public List<string> NotCoveredNodeTypes { get; set; }

        [JsonProperty("nodes")]
        public List<WfAiNodeInterpretation> Nodes { get; set; }

        [JsonProperty("clarifications")]
        public List<WfAiClarification> Clarifications { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        [JsonProperty("registryErrors")]
        public List<string> RegistryErrors { get; set; }

        [JsonProperty("blockingClarificationCount")]
        public int BlockingClarificationCount { get; set; }

        [JsonProperty("coveredNodesResolved")]
        public bool CoveredNodesResolved { get; set; }

        [JsonProperty("universalCoverageComplete")]
        public bool UniversalCoverageComplete { get; set; }

        public WfAiInterpretationDraft()
        {
            Version = "fix84b-dialog-v1";
            SourceText = string.Empty;
            Fingerprint = string.Empty;
            CoveredNodeTypes = new List<string>();
            NotCoveredNodeTypes = new List<string>();
            Nodes = new List<WfAiNodeInterpretation>();
            Clarifications = new List<WfAiClarification>();
            Warnings = new List<string>();
            RegistryErrors = new List<string>();
        }
    }
}
