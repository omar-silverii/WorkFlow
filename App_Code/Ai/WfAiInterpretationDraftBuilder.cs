using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix84a: adapta el plan actual a un borrador de interpretación contractual.
    /// Es deliberadamente side-effect free: no cambia actions, no crea conexiones y no aplica al canvas.
    /// </summary>
    public class WfAiInterpretationDraftBuilder
    {
        public WfAiInterpretationDraft Build(string sourceText, JObject plan, WfAiCatalog catalog)
        {
            return Build(sourceText, plan, catalog, null, null);
        }

        public WfAiInterpretationDraft Build(
            string sourceText,
            JObject plan,
            WfAiCatalog catalog,
            ICollection<string> acceptedClarificationIds,
            string fingerprintOverride)
        {
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (acceptedClarificationIds != null)
            {
                foreach (string id in acceptedClarificationIds)
                {
                    if (!string.IsNullOrWhiteSpace(id)) accepted.Add(id);
                }
            }

            var draft = new WfAiInterpretationDraft
            {
                Version = "fix84b-dialog-v1",
                SourceText = sourceText ?? string.Empty,
                Fingerprint = string.IsNullOrWhiteSpace(fingerprintOverride) ? BuildFingerprint(sourceText, plan) : fingerprintOverride,
                CoveredNodeTypes = WfAiConstructionContractRegistry.CoveredNodeTypes(),
                RegistryErrors = WfAiConstructionContractRegistry.ValidateAgainstCatalog(catalog)
            };

            if (plan == null)
            {
                draft.Warnings.Add("No hay plan para adaptar al contrato universal.");
                draft.CoveredNodesResolved = false;
                draft.UniversalCoverageComplete = false;
                return draft;
            }

            JArray actions = plan["actions"] as JArray;
            if (actions == null)
            {
                draft.Warnings.Add("El plan no contiene actions[].");
                draft.CoveredNodesResolved = false;
                draft.UniversalCoverageComplete = false;
                return draft;
            }

            int index = 0;
            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null)
                {
                    index++;
                    continue;
                }

                string actionKind = Text(action["action"]);
                string nodeType = Text(action["nodeType"]);
                string label = Text(action["label"]);

                if (string.Equals(actionKind, "ASK_USER", StringComparison.OrdinalIgnoreCase))
                {
                    string clarificationId = "legacy-ask-" + index.ToString(CultureInfo.InvariantCulture);
                    if (!accepted.Contains(clarificationId))
                    {
                        AddClarification(draft, new WfAiClarification
                        {
                            Id = clarificationId,
                            ActionIndex = index,
                            NodeType = nodeType,
                            NodeLabel = label,
                            Status = WfAiInterpretationStatus.Unrecognized,
                            Question = "Necesito una aclaración para continuar.",
                            ControlKind = WfAiControlKind.Text,
                            Blocking = true,
                            Source = "legacy_action"
                        });
                    }
                    index++;
                    continue;
                }

                if (!string.Equals(actionKind, "ADD_NODE", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }

                WfAiNodeConstructionContract contract = WfAiConstructionContractRegistry.Find(nodeType);
                if (contract == null)
                {
                    if (!IsTechnicalBoundary(nodeType))
                        AddUnique(draft.NotCoveredNodeTypes, nodeType);
                    index++;
                    continue;
                }

                WfAiNodeInterpretation node = BuildNode(sourceText, action, contract, index, draft, accepted);
                draft.Nodes.Add(node);
                index++;
            }

            ImportLegacyMissingData(plan, draft, accepted);
            ImportFutureAmbiguities(plan, draft, accepted);

            draft.BlockingClarificationCount = 0;
            foreach (WfAiClarification clarification in draft.Clarifications)
            {
                if (clarification != null && clarification.Blocking)
                    draft.BlockingClarificationCount++;
            }

            draft.CoveredNodesResolved = draft.RegistryErrors.Count == 0 && draft.BlockingClarificationCount == 0;
            draft.UniversalCoverageComplete = draft.NotCoveredNodeTypes.Count == 0;

            if (!draft.UniversalCoverageComplete)
                draft.Warnings.Add("FIX84B todavía cubre solamente la muestra contractual inicial. Los nodos restantes continúan por el mecanismo validado anterior.");

            return draft;
        }

        private static WfAiNodeInterpretation BuildNode(
            string sourceText,
            JObject action,
            WfAiNodeConstructionContract contract,
            int actionIndex,
            WfAiInterpretationDraft draft,
            HashSet<string> acceptedClarificationIds)
        {
            var node = new WfAiNodeInterpretation
            {
                ActionIndex = actionIndex,
                NodeType = contract.NodeType,
                Label = Text(action["label"]),
                Status = WfAiInterpretationStatus.Resolved,
                Summary = BuildSummary(action, contract),
                OutputFields = new List<string>(contract.OutputFields),
                DynamicOutputPrefixes = new List<string>(contract.DynamicOutputPrefixes)
            };

            JObject parameters = action["params"] as JObject ?? new JObject();

            foreach (WfAiParameterContract parameterContract in contract.Parameters)
            {
                WfAiParameterInterpretation parameter = InterpretParameter(sourceText, parameters, parameterContract);
                node.Parameters.Add(parameter);

                if (parameter.Blocking)
                {
                    AddClarification(draft, new WfAiClarification
                    {
                        Id = ClarificationId(actionIndex, parameterContract.Name),
                        ActionIndex = actionIndex,
                        NodeType = contract.NodeType,
                        NodeLabel = node.Label,
                        Parameter = parameterContract.Name,
                        Status = parameter.Status,
                        Question = SafeQuestion(parameterContract, contract),
                        ControlKind = parameterContract.ControlKind,
                        Options = new List<string>(parameterContract.Options),
                        CurrentValue = Clone(parameter.Value),
                        Blocking = true,
                        Source = "contract_parameter"
                    });
                }
            }

            foreach (WfAiAlternativeRequirement requirement in contract.AlternativeRequirements)
            {
                if (requirement == null || RequirementSatisfied(parameters, requirement)) continue;

                AddClarification(draft, new WfAiClarification
                {
                    Id = ClarificationId(actionIndex, requirement.Key),
                    ActionIndex = actionIndex,
                    NodeType = contract.NodeType,
                    NodeLabel = node.Label,
                    Parameter = requirement.Key,
                    Status = WfAiInterpretationStatus.Missing,
                    Question = string.IsNullOrWhiteSpace(requirement.ClarificationQuestion)
                        ? "Falta completar " + requirement.Label + "."
                        : requirement.ClarificationQuestion,
                    ControlKind = requirement.ControlKind,
                    Blocking = requirement.Blocking,
                    Source = "contract_requirement"
                });
            }

            foreach (WfAiAmbiguityRule ambiguity in contract.AmbiguityRules)
            {
                if (ambiguity == null || !SourceContainsAny(sourceText, ambiguity.PhraseFragments)) continue;
                string ambiguityId = ClarificationId(actionIndex, "ambiguity-" + ambiguity.Key);
                if (acceptedClarificationIds != null && acceptedClarificationIds.Contains(ambiguityId)) continue;

                AddClarification(draft, new WfAiClarification
                {
                    Id = ambiguityId,
                    ActionIndex = actionIndex,
                    NodeType = contract.NodeType,
                    NodeLabel = node.Label,
                    Parameter = ambiguity.Key,
                    Status = WfAiInterpretationStatus.Ambiguous,
                    Question = ambiguity.Question,
                    ControlKind = ambiguity.ControlKind,
                    Options = new List<string>(ambiguity.Options),
                    Blocking = ambiguity.Blocking,
                    Source = "contract_ambiguity"
                });
            }

            node.Status = AggregateNodeStatus(node, draft, actionIndex);
            return node;
        }

        private static WfAiParameterInterpretation InterpretParameter(
            string sourceText,
            JObject parameters,
            WfAiParameterContract contract)
        {
            JToken value = parameters == null ? null : parameters[contract.Name];
            bool hasValue = HasValue(value);
            bool placeholder = hasValue
                && IsPlaceholder(value, contract.PlaceholderValues)
                && !SourceMentionsValue(sourceText, value);

            var result = new WfAiParameterInterpretation
            {
                Name = contract.Name,
                Label = contract.Label,
                InferencePolicy = contract.InferencePolicy,
                Value = Clone(value),
                Status = WfAiInterpretationStatus.Resolved,
                Source = "plan",
                Blocking = false,
                Explanation = string.Empty
            };

            if (!hasValue || placeholder)
            {
                if (contract.DefaultValue != null && string.Equals(contract.InferencePolicy, WfAiInferencePolicy.SafeDefault, StringComparison.OrdinalIgnoreCase))
                {
                    result.Value = Clone(contract.DefaultValue);
                    result.Status = WfAiInterpretationStatus.Inferred;
                    result.Source = "safe_default";
                    result.Explanation = "Valor predeterminado seguro del contrato.";
                    return result;
                }

                if (contract.Required)
                {
                    result.Status = WfAiInterpretationStatus.Missing;
                    result.Source = placeholder ? "placeholder_rejected" : "not_supplied";
                    result.Blocking = true;
                    result.Explanation = placeholder
                        ? "El plan contiene un valor genérico que el contrato no acepta como decisión del usuario."
                        : "Dato necesario no informado.";
                    return result;
                }

                result.Status = WfAiInterpretationStatus.Resolved;
                result.Source = "optional_empty";
                result.Explanation = "Parámetro opcional no informado.";
                return result;
            }

            if (contract.DefaultValue != null
                && JToken.DeepEquals(NormalizeValue(value), NormalizeValue(contract.DefaultValue))
                && !SourceMentionsValue(sourceText, value))
            {
                result.Status = WfAiInterpretationStatus.Inferred;
                result.Source = "plan_default";
                result.Explanation = "El plan usa el valor predeterminado del contrato y la frase no lo menciona explícitamente.";
                return result;
            }

            if (string.Equals(contract.InferencePolicy, WfAiInferencePolicy.VisibleInference, StringComparison.OrdinalIgnoreCase)
                && !SourceMentionsValue(sourceText, value))
            {
                result.Status = WfAiInterpretationStatus.Inferred;
                result.Source = "visible_inference";
                result.Explanation = "Valor generado a partir del contexto; debe mostrarse al usuario antes de confirmar.";
                return result;
            }

            result.Explanation = "Valor presente en el plan actual.";
            return result;
        }

        private static bool RequirementSatisfied(JObject parameters, WfAiAlternativeRequirement requirement)
        {
            if (parameters == null || requirement == null || requirement.Alternatives == null) return false;

            foreach (List<string> alternative in requirement.Alternatives)
            {
                if (alternative == null || alternative.Count == 0) continue;
                bool allPresent = true;
                foreach (string name in alternative)
                {
                    if (!HasValue(parameters[name]))
                    {
                        allPresent = false;
                        break;
                    }
                }
                if (allPresent) return true;
            }
            return false;
        }

        private static string AggregateNodeStatus(WfAiNodeInterpretation node, WfAiInterpretationDraft draft, int actionIndex)
        {
            bool inferred = false;
            foreach (WfAiParameterInterpretation parameter in node.Parameters)
            {
                if (parameter == null) continue;
                if (parameter.Status == WfAiInterpretationStatus.Unrecognized) return WfAiInterpretationStatus.Unrecognized;
                if (parameter.Status == WfAiInterpretationStatus.Ambiguous) return WfAiInterpretationStatus.Ambiguous;
                if (parameter.Status == WfAiInterpretationStatus.Missing) return WfAiInterpretationStatus.Missing;
                if (parameter.Status == WfAiInterpretationStatus.Inferred) inferred = true;
            }

            foreach (WfAiClarification clarification in draft.Clarifications)
            {
                if (clarification == null) continue;
                string prefix = "a" + actionIndex.ToString(CultureInfo.InvariantCulture) + ":";
                if (!string.IsNullOrWhiteSpace(clarification.Id) && clarification.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (clarification.Status == WfAiInterpretationStatus.Ambiguous) return WfAiInterpretationStatus.Ambiguous;
                    if (clarification.Blocking) return WfAiInterpretationStatus.Missing;
                }
            }

            return inferred ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved;
        }

        private static void ImportLegacyMissingData(JObject plan, WfAiInterpretationDraft draft, HashSet<string> acceptedClarificationIds)
        {
            JArray missing = plan["missingData"] as JArray;
            if (missing == null) return;

            int index = 0;
            foreach (JToken token in missing)
            {
                JObject item = token as JObject;
                if (item == null) continue;
                string key = Text(item["key"]);
                string question = Text(item["question"]);
                if (question.Length == 0) continue;

                string clarificationId = "legacy-missing:" + (key.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : key);
                if (acceptedClarificationIds != null && acceptedClarificationIds.Contains(clarificationId))
                {
                    index++;
                    continue;
                }

                // human.task ya tiene una pregunta contractual más rica (rol/usuario) para el mismo faltante.
                if (string.Equals(key, "rolUsuario", StringComparison.OrdinalIgnoreCase) && HasPendingParameter(draft, "taskDestination"))
                {
                    index++;
                    continue;
                }

                AddClarification(draft, new WfAiClarification
                {
                    Id = clarificationId,
                    ActionIndex = -1,
                    Parameter = key,
                    Status = WfAiInterpretationStatus.Missing,
                    Question = question,
                    ControlKind = WfAiControlKind.Text,
                    Blocking = true,
                    Source = "legacy_missingData"
                });
                index++;
            }
        }

        /// <summary>
        /// El provider actual todavía no publica una colección universal de ambigüedades.
        /// Este importador deja listo el contrato para que fix84b pueda entregarlas sin cambiar el modelo.
        /// </summary>
        private static void ImportFutureAmbiguities(JObject plan, WfAiInterpretationDraft draft, HashSet<string> acceptedClarificationIds)
        {
            JArray ambiguities = plan["ambiguities"] as JArray;
            if (ambiguities == null)
                ambiguities = plan.SelectToken("mlnet.resolved.ambiguities") as JArray;
            if (ambiguities == null) return;

            int index = 0;
            foreach (JToken token in ambiguities)
            {
                JObject item = token as JObject;
                if (item == null) continue;

                string clarificationId = "ambiguity:" + index.ToString(CultureInfo.InvariantCulture);
                if (acceptedClarificationIds != null && acceptedClarificationIds.Contains(clarificationId))
                {
                    index++;
                    continue;
                }

                AddClarification(draft, new WfAiClarification
                {
                    Id = clarificationId,
                    ActionIndex = ReadInt(item["actionIndex"], -1),
                    NodeType = Text(item["nodeType"]),
                    NodeLabel = Text(item["nodeLabel"]),
                    Parameter = Text(item["parameter"]),
                    Status = WfAiInterpretationStatus.Ambiguous,
                    Question = Text(item["question"]),
                    ControlKind = string.IsNullOrWhiteSpace(Text(item["controlKind"])) ? WfAiControlKind.Select : Text(item["controlKind"]),
                    Options = StringList(item["options"] as JArray),
                    CurrentValue = Clone(item["currentValue"]),
                    Blocking = true,
                    Source = "plan_ambiguities"
                });
                index++;
            }
        }

        private static bool HasPendingParameter(WfAiInterpretationDraft draft, string parameter)
        {
            if (draft == null || draft.Clarifications == null) return false;
            foreach (WfAiClarification item in draft.Clarifications)
            {
                if (item != null && item.Blocking && string.Equals(item.Parameter, parameter, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string BuildFingerprint(string sourceText, JObject plan)
        {
            var material = new JObject
            {
                ["sourceText"] = NormalizeText(sourceText),
                ["actions"] = plan == null || plan["actions"] == null ? new JArray() : plan["actions"].DeepClone(),
                ["proposedConnections"] = plan == null || plan["proposedConnections"] == null ? new JArray() : plan["proposedConnections"].DeepClone(),
                ["branchPlan"] = plan == null || plan["branchPlan"] == null ? JValue.CreateNull() : plan["branchPlan"].DeepClone(),
                ["missingData"] = plan == null || plan["missingData"] == null ? new JArray() : plan["missingData"].DeepClone()
            };

            byte[] bytes = Encoding.UTF8.GetBytes(material.ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static int ReadInt(JToken token, int fallback)
        {
            int value;
            return int.TryParse(Text(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static string BuildSummary(JObject action, WfAiNodeConstructionContract contract)
        {
            string label = Text(action["label"]);
            if (label.Length > 0) return label;
            return string.IsNullOrWhiteSpace(contract.SummaryTemplate) ? contract.Label : contract.SummaryTemplate;
        }

        private static string SafeQuestion(WfAiParameterContract parameter, WfAiNodeConstructionContract contract)
        {
            if (!string.IsNullOrWhiteSpace(parameter.ClarificationQuestion))
                return parameter.ClarificationQuestion;
            return "Falta completar " + parameter.Label + " para " + contract.Label + ".";
        }

        private static bool IsTechnicalBoundary(string nodeType)
        {
            return string.Equals(nodeType, "util.start", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeType, "util.end", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return false;
            if (token.Type == JTokenType.String) return !string.IsNullOrWhiteSpace(token.ToString());
            if (token.Type == JTokenType.Array) return ((JArray)token).Count > 0;
            if (token.Type == JTokenType.Object) return ((JObject)token).HasValues;
            return true;
        }

        private static bool IsPlaceholder(JToken value, List<string> placeholders)
        {
            if (value == null || placeholders == null || placeholders.Count == 0) return false;
            string text = Text(value);
            foreach (string placeholder in placeholders)
            {
                if (string.Equals(text, placeholder ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool SourceMentionsValue(string sourceText, JToken value)
        {
            if (string.IsNullOrWhiteSpace(sourceText) || value == null) return false;
            if (value.Type != JTokenType.String && value.Type != JTokenType.Integer && value.Type != JTokenType.Float && value.Type != JTokenType.Boolean)
                return false;

            string source = NormalizeText(sourceText);
            string wanted = NormalizeText(value.ToString());
            return wanted.Length > 0 && source.Contains(wanted);
        }

        private static bool SourceContainsAny(string sourceText, List<string> fragments)
        {
            if (string.IsNullOrWhiteSpace(sourceText) || fragments == null || fragments.Count == 0) return false;
            string source = NormalizeText(sourceText);
            foreach (string fragment in fragments)
            {
                string wanted = NormalizeText(fragment);
                if (wanted.Length > 0 && source.Contains(wanted)) return true;
            }
            return false;
        }

        private static string NormalizeText(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static JToken NormalizeValue(JToken value)
        {
            if (value == null) return null;
            if (value.Type == JTokenType.String)
                return new JValue((value.ToString() ?? string.Empty).Trim());
            return value;
        }

        private static string ClarificationId(int actionIndex, string key)
        {
            return "a" + actionIndex.ToString(CultureInfo.InvariantCulture) + ":" + (key ?? string.Empty);
        }

        private static void AddClarification(WfAiInterpretationDraft draft, WfAiClarification item)
        {
            if (draft == null || item == null) return;
            foreach (WfAiClarification existing in draft.Clarifications)
            {
                if (existing != null && string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            draft.Clarifications.Add(item);
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return;
            foreach (string existing in list)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) return;
            }
            list.Add(value);
        }

        private static List<string> StringList(JArray array)
        {
            var result = new List<string>();
            if (array == null) return result;
            foreach (JToken item in array)
            {
                string value = Text(item);
                if (value.Length > 0) result.Add(value);
            }
            return result;
        }

        private static JToken Clone(JToken token)
        {
            return token == null ? null : token.DeepClone();
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return string.Empty;
            return Convert.ToString(token, CultureInfo.InvariantCulture).Trim();
        }
    }
}
