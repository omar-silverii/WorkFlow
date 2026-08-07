using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix84b: aplica respuestas estructuradas de aclaración sobre una copia del plan.
    /// No vuelve a interpretar la frase ni toca handlers/runtime. El plan base se reconstruye
    /// en cada POST y estas decisiones se aplican de forma determinística encima.
    /// </summary>
    public class WfAiClarificationResolver
    {
        public WfAiClarificationResolutionResult Resolve(
            JObject basePlan,
            WfAiInterpretationDraft baseDraft,
            JObject answers,
            WfAiCatalog catalog)
        {
            var result = new WfAiClarificationResolutionResult
            {
                Plan = basePlan == null ? new JObject() : (JObject)basePlan.DeepClone()
            };

            if (answers == null || !answers.HasValues)
                return result;

            foreach (JProperty property in answers.Properties())
            {
                string id = (property.Name ?? string.Empty).Trim();
                if (id.Length == 0) continue;

                WfAiClarification clarification = FindClarification(baseDraft, id);
                if (clarification == null)
                {
                    result.Warnings.Add("La aclaración ya no pertenece al borrador actual: " + id);
                    continue;
                }

                ApplyAnswer(result, clarification, property.Value, catalog);
            }

            return result;
        }

        private static void ApplyAnswer(
            WfAiClarificationResolutionResult result,
            WfAiClarification clarification,
            JToken answer,
            WfAiCatalog catalog)
        {
            if (result == null || clarification == null) return;

            string source = clarification.Source ?? string.Empty;
            if (string.Equals(source, "contract_parameter", StringComparison.OrdinalIgnoreCase))
            {
                ApplyParameterAnswer(result, clarification, answer);
                return;
            }

            if (string.Equals(source, "contract_requirement", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRequirementAnswer(result, clarification, answer, catalog);
                return;
            }

            if (string.Equals(source, "contract_ambiguity", StringComparison.OrdinalIgnoreCase))
            {
                ApplyAmbiguityAnswer(result, clarification, answer);
                return;
            }

            result.Errors.Add("Esta duda todavía pertenece al mecanismo anterior y no puede resolverse con FIX84B: " + SafeQuestion(clarification));
        }

        private static void ApplyParameterAnswer(
            WfAiClarificationResolutionResult result,
            WfAiClarification clarification,
            JToken answer)
        {
            JObject action = ActionAt(result.Plan, clarification.ActionIndex);
            if (action == null)
            {
                result.Errors.Add("No encontré el nodo asociado a la aclaración " + clarification.Id + ".");
                return;
            }

            WfAiNodeConstructionContract contract = WfAiConstructionContractRegistry.Find(clarification.NodeType);
            WfAiParameterContract parameter = contract == null ? null : contract.FindParameter(clarification.Parameter);
            if (parameter == null)
            {
                result.Errors.Add("El contrato ya no contiene el parámetro " + clarification.Parameter + " de " + clarification.NodeType + ".");
                return;
            }

            string validationError;
            JToken normalized = NormalizeParameterAnswer(parameter, answer, out validationError);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                result.Errors.Add(validationError);
                return;
            }

            JObject parameters = action["params"] as JObject;
            if (parameters == null)
            {
                parameters = new JObject();
                action["params"] = parameters;
            }
            parameters[parameter.Name] = normalized == null ? JValue.CreateNull() : normalized;

            Accept(result, clarification, DisplayAnswer(normalized));
        }

        private static void ApplyRequirementAnswer(
            WfAiClarificationResolutionResult result,
            WfAiClarification clarification,
            JToken answer,
            WfAiCatalog catalog)
        {
            JObject action = ActionAt(result.Plan, clarification.ActionIndex);
            if (action == null)
            {
                result.Errors.Add("No encontré el nodo asociado a la aclaración " + clarification.Id + ".");
                return;
            }

            JObject parameters = action["params"] as JObject;
            if (parameters == null)
            {
                parameters = new JObject();
                action["params"] = parameters;
            }

            if (string.Equals(clarification.NodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                && string.Equals(clarification.Parameter, "taskDestination", StringComparison.OrdinalIgnoreCase))
            {
                JObject destination = answer as JObject;
                string kind = Text(destination == null ? null : destination["kind"]).ToLowerInvariant();
                string value = Text(destination == null ? null : destination["value"]);
                if (value.Length == 0 || (kind != "role" && kind != "user"))
                {
                    result.Errors.Add("Elegí un rol o un usuario real para la tarea.");
                    return;
                }

                if (kind == "role")
                {
                    if (!CatalogHasRole(catalog, value))
                    {
                        result.Errors.Add("El rol seleccionado ya no existe en el catálogo real: " + value);
                        return;
                    }
                    parameters["rol"] = value;
                    parameters.Remove("usuarioAsignado");
                    RemoveMissingData(result.Plan, "rolUsuario", "rolNoEncontrado");
                    Accept(result, clarification, "Rol: " + value);
                    return;
                }

                if (!CatalogHasUser(catalog, value))
                {
                    result.Errors.Add("El usuario seleccionado ya no existe en el catálogo real: " + value);
                    return;
                }
                parameters["usuarioAsignado"] = value;
                parameters.Remove("rol");
                RemoveMissingData(result.Plan, "rolUsuario", "rolNoEncontrado");
                Accept(result, clarification, "Usuario: " + value);
                return;
            }

            if (string.Equals(clarification.NodeType, "control.if", StringComparison.OrdinalIgnoreCase)
                && string.Equals(clarification.Parameter, "conditionDefinition", StringComparison.OrdinalIgnoreCase))
            {
                JObject condition = answer as JObject;
                string mode = Text(condition == null ? null : condition["mode"]).ToLowerInvariant();
                if (mode.Length == 0) mode = "simple";

                if (mode == "simple")
                {
                    string field = Text(condition == null ? null : condition["field"]);
                    string op = Text(condition == null ? null : condition["op"]);
                    JToken value = condition == null ? null : condition["value"];
                    if (field.Length == 0 || op.Length == 0)
                    {
                        result.Errors.Add("Elegí el dato y el operador de la condición.");
                        return;
                    }
                    if (!AllowedIfOperator(op))
                    {
                        result.Errors.Add("El operador de condición no es válido: " + op);
                        return;
                    }
                    if (OperatorNeedsValue(op) && !HasValue(value))
                    {
                        result.Errors.Add("Completá el valor contra el que querés comparar.");
                        return;
                    }

                    parameters.Remove("expression");
                    parameters.Remove("rules");
                    parameters["field"] = field;
                    parameters["op"] = op;
                    if (OperatorNeedsValue(op)) parameters["value"] = NormalizeScalar(value);
                    else parameters.Remove("value");
                    RemoveMissingData(result.Plan, "campoCae", "campoImporteTotal");

                    Accept(result, clarification, field + " " + op + (OperatorNeedsValue(op) ? " " + DisplayAnswer(value) : string.Empty));
                    return;
                }

                if (mode == "expression")
                {
                    string expression = Text(condition == null ? null : condition["expression"]);
                    if (expression.Length == 0)
                    {
                        result.Errors.Add("La expresión de la condición está vacía.");
                        return;
                    }
                    parameters.Remove("field");
                    parameters.Remove("op");
                    parameters.Remove("value");
                    parameters.Remove("rules");
                    parameters["expression"] = expression;
                    Accept(result, clarification, expression);
                    return;
                }

                result.Errors.Add("FIX84B todavía no admite ese modo de condición: " + mode);
                return;
            }

            result.Errors.Add("FIX84B todavía no tiene un resolvedor para " + clarification.NodeType + "/" + clarification.Parameter + ".");
        }

        private static void ApplyAmbiguityAnswer(
            WfAiClarificationResolutionResult result,
            WfAiClarification clarification,
            JToken answer)
        {
            string value = Text(answer);
            if (string.Equals(clarification.NodeType, "queue.publish", StringComparison.OrdinalIgnoreCase)
                && string.Equals(clarification.Parameter, "createQueueMeaning", StringComparison.OrdinalIgnoreCase))
            {
                if (value.IndexOf("publicar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Accept(result, clarification, "Publicar en una cola existente");
                    return;
                }

                result.Errors.Add("Workflow Studio hoy publica/consume mensajes de colas existentes; no tiene un nodo para crear infraestructura de cola. Elegí publicar en una cola existente o armá ese requisito fuera de este workflow.");
                return;
            }

            result.Errors.Add("No hay una resolución contractual implementada para esta ambigüedad: " + SafeQuestion(clarification));
        }

        private static JToken NormalizeParameterAnswer(WfAiParameterContract parameter, JToken answer, out string error)
        {
            error = string.Empty;
            if (parameter == null)
            {
                error = "No encontré el contrato del dato a completar.";
                return null;
            }

            if (!HasValue(answer))
            {
                error = "Completá " + (parameter.Label ?? parameter.Name ?? "el dato") + ".";
                return null;
            }

            string type = (parameter.DataType ?? string.Empty).ToLowerInvariant();
            if (type == "number")
            {
                decimal number;
                string raw = Text(answer).Replace(',', '.');
                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                {
                    error = "El valor de " + parameter.Label + " debe ser numérico.";
                    return null;
                }
                return new JValue(number);
            }

            if (type == "boolean")
            {
                bool boolean;
                if (answer.Type == JTokenType.Boolean) return new JValue(answer.Value<bool>());
                if (!bool.TryParse(Text(answer), out boolean))
                {
                    error = "El valor de " + parameter.Label + " debe ser Sí o No.";
                    return null;
                }
                return new JValue(boolean);
            }

            JToken normalized = answer.DeepClone();
            if (string.Equals(parameter.ControlKind, WfAiControlKind.PayloadEditor, StringComparison.OrdinalIgnoreCase))
            {
                string rawPayload = Text(answer);
                if (answer.Type == JTokenType.String && (rawPayload.StartsWith("{", StringComparison.Ordinal) || rawPayload.StartsWith("[", StringComparison.Ordinal)))
                {
                    try { normalized = JToken.Parse(rawPayload); }
                    catch { normalized = new JValue(rawPayload); }
                }
            }

            if (parameter.Options != null && parameter.Options.Count > 0)
            {
                string selected = Text(normalized);
                bool found = false;
                foreach (string option in parameter.Options)
                {
                    if (string.Equals(option ?? string.Empty, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = new JValue(option);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    error = "El valor elegido para " + parameter.Label + " no está permitido por el contrato.";
                    return null;
                }
            }

            return NormalizeScalar(normalized);
        }

        private static void RemoveMissingData(JObject plan, params string[] keys)
        {
            if (plan == null || keys == null || keys.Length == 0) return;
            JArray missing = plan["missingData"] as JArray;
            if (missing == null || missing.Count == 0) return;
            var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                JObject item = missing[i] as JObject;
                if (item == null) continue;
                string key = Text(item["key"]);
                if (wanted.Contains(key)) missing.RemoveAt(i);
            }
        }

        private static JObject ActionAt(JObject plan, int actionIndex)
        {
            if (plan == null || actionIndex < 0) return null;
            JArray actions = plan["actions"] as JArray;
            if (actions == null || actionIndex >= actions.Count) return null;
            return actions[actionIndex] as JObject;
        }

        private static WfAiClarification FindClarification(WfAiInterpretationDraft draft, string id)
        {
            if (draft == null || draft.Clarifications == null) return null;
            foreach (WfAiClarification item in draft.Clarifications)
            {
                if (item != null && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return null;
        }

        private static bool CatalogHasRole(WfAiCatalog catalog, string value)
        {
            if (catalog == null || catalog.Roles == null) return false;
            foreach (string role in catalog.Roles)
            {
                if (string.Equals(role ?? string.Empty, value ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool CatalogHasUser(WfAiCatalog catalog, string value)
        {
            if (catalog == null || catalog.Users == null) return false;
            foreach (WfAiUserInfo user in catalog.Users)
            {
                if (user == null) continue;
                if (string.Equals(user.UserKey ?? string.Empty, value ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool AllowedIfOperator(string op)
        {
            switch ((op ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "=":
                case "==":
                case "!=":
                case ">":
                case ">=":
                case "<":
                case "<=":
                case "contains":
                case "not_contains":
                case "empty":
                case "not_empty":
                    return true;
                default:
                    return false;
            }
        }

        private static bool OperatorNeedsValue(string op)
        {
            string value = (op ?? string.Empty).Trim().ToLowerInvariant();
            return value != "empty" && value != "not_empty";
        }

        private static bool HasValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return false;
            if (token.Type == JTokenType.String) return !string.IsNullOrWhiteSpace(token.ToString());
            if (token.Type == JTokenType.Array) return ((JArray)token).Count > 0;
            if (token.Type == JTokenType.Object) return ((JObject)token).HasValues;
            return true;
        }

        private static JToken NormalizeScalar(JToken token)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.String) return new JValue((token.ToString() ?? string.Empty).Trim());
            return token.DeepClone();
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return string.Empty;
            return Convert.ToString(token, CultureInfo.InvariantCulture).Trim();
        }

        private static string DisplayAnswer(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "(vacío)";
            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array) return token.ToString(Newtonsoft.Json.Formatting.None);
            return Text(token);
        }

        private static string SafeQuestion(WfAiClarification clarification)
        {
            return clarification == null || string.IsNullOrWhiteSpace(clarification.Question)
                ? "aclaración sin descripción"
                : clarification.Question;
        }

        private static void Accept(WfAiClarificationResolutionResult result, WfAiClarification clarification, string answerDisplay)
        {
            result.AcceptedAnswerIds.Add(clarification.Id);
            result.AppliedAnswers.Add(new WfAiResolvedClarification
            {
                Id = clarification.Id,
                NodeType = clarification.NodeType,
                NodeLabel = clarification.NodeLabel,
                Question = clarification.Question,
                Answer = answerDisplay ?? string.Empty
            });
        }
    }

    public class WfAiClarificationResolutionResult
    {
        public JObject Plan { get; set; }
        public List<string> AcceptedAnswerIds { get; set; }
        public List<WfAiResolvedClarification> AppliedAnswers { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }

        public WfAiClarificationResolutionResult()
        {
            AcceptedAnswerIds = new List<string>();
            AppliedAnswers = new List<WfAiResolvedClarification>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }
    }

    public class WfAiResolvedClarification
    {
        public string Id { get; set; }
        public string NodeType { get; set; }
        public string NodeLabel { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
    }
}
