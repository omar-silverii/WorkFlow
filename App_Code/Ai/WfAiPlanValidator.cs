using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    public class WfAiPlanValidator
    {
        private static readonly HashSet<string> AllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASK_USER",
            "ADD_NODE",
            "CONNECT_NODES",
            "SET_NODE_PARAM",
            "SUGGEST_DOCTYPE",
            "SUGGEST_ROLE",
            "VALIDATE_GRAPH",
            "EXPLAIN_GRAPH"
        };

        public WfAiValidationResult Validate(JObject plan, WfAiCatalog catalog)
        {
            var result = new WfAiValidationResult();

            if (plan == null)
            {
                result.Ok = false;
                result.Errors.Add("El plan está vacío.");
                return result;
            }

            string intent = Convert.ToString(plan["intent"] ?? "").Trim();
            if (intent.Length == 0)
                result.Warnings.Add("El plan no informó intent.");

            JToken actionsToken = plan["actions"];
            if (actionsToken != null && actionsToken.Type != JTokenType.Array)
                result.Errors.Add("actions debe ser un array.");

            var actions = actionsToken as JArray;
            if (actions != null)
            {
                foreach (JToken item in actions)
                {
                    var a = item as JObject;
                    if (a == null)
                    {
                        result.Errors.Add("Una acción no es un objeto JSON válido.");
                        continue;
                    }

                    string action = Convert.ToString(a["action"] ?? "").Trim();
                    if (!AllowedActions.Contains(action))
                    {
                        result.Errors.Add("Acción no permitida: " + action);
                        continue;
                    }

                    if (action.Equals("ADD_NODE", StringComparison.OrdinalIgnoreCase))
                        ValidateAddNode(a, catalog, result);

                    if (action.Equals("SET_NODE_PARAM", StringComparison.OrdinalIgnoreCase))
                        ValidateParams(a, catalog, result);
                }
            }

            JToken missingData = plan["missingData"];
            if (missingData != null && missingData.Type != JTokenType.Array)
                result.Errors.Add("missingData debe ser un array.");

            ValidateProposedConnections(plan, result);

            result.Ok = result.Errors.Count == 0;
            return result;
        }

        // fix49b: defensa estructural del grafo propuesto por el Asistente IA.
        // No alcanza con que los nodos tengan parámetros válidos: un control.if debe
        // tener SI y NO, y esas dos ramas no pueden terminar en el mismo destino.
        private static void ValidateProposedConnections(JObject plan, WfAiValidationResult result)
        {
            if (plan == null) return;

            JToken proposedToken = plan["proposedConnections"];
            if (proposedToken != null && proposedToken.Type != JTokenType.Array)
            {
                result.Errors.Add("proposedConnections debe ser un array.");
                return;
            }

            var actions = plan["actions"] as JArray;
            var proposed = proposedToken as JArray;
            if (actions == null || proposed == null) return;

            var addNodes = new List<JObject>();
            var knownRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null) continue;
                if (!Convert.ToString(action["action"] ?? "").Trim().Equals("ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;

                addNodes.Add(action);
                string label = Convert.ToString(action["label"] ?? "").Trim();
                string type = Convert.ToString(action["nodeType"] ?? "").Trim();

                if (label.Length > 0) knownRefs.Add(label);
                if (label.Length > 0 || type.Length > 0) knownRefs.Add(NodeRef(type, label));
            }

            foreach (JToken token in proposed)
            {
                JObject c = token as JObject;
                if (c == null)
                {
                    result.Errors.Add("Una conexión propuesta no es un objeto JSON válido.");
                    continue;
                }

                string from = Convert.ToString(c["from"] ?? "").Trim();
                string to = Convert.ToString(c["to"] ?? "").Trim();
                string fromType = Convert.ToString(c["fromNodeType"] ?? "").Trim();
                string toType = Convert.ToString(c["toNodeType"] ?? "").Trim();

                if (from.Length == 0 || to.Length == 0)
                {
                    result.Errors.Add("Conexión propuesta incompleta: falta origen o destino.");
                    continue;
                }

                if (!KnownNodeRef(knownRefs, fromType, from))
                    result.Errors.Add("Conexión propuesta con origen inexistente: " + from);

                if (!KnownNodeRef(knownRefs, toType, to))
                    result.Errors.Add("Conexión propuesta con destino inexistente: " + to);
            }

            foreach (JObject node in addNodes)
            {
                string type = Convert.ToString(node["nodeType"] ?? "").Trim();
                if (!type.Equals("control.if", StringComparison.OrdinalIgnoreCase)) continue;

                ValidateIfBranches(node, proposed, result);
            }
        }

        private static void ValidateIfBranches(JObject ifAction, JArray proposed, WfAiValidationResult result)
        {
            string label = Convert.ToString(ifAction["label"] ?? "").Trim();
            string type = Convert.ToString(ifAction["nodeType"] ?? "").Trim();
            bool humanResultIf = IsHumanTaskResultIf(ifAction);

            var trueTargets = new List<string>();
            var falseTargets = new List<string>();

            foreach (JToken token in proposed)
            {
                JObject c = token as JObject;
                if (c == null) continue;
                if (!ConnectionFromAction(c, type, label)) continue;

                string branch = NormalizeBranchCondition(Convert.ToString(c["condition"] ?? ""));
                string to = Convert.ToString(c["to"] ?? "").Trim();
                string toType = Convert.ToString(c["toNodeType"] ?? "").Trim();
                string targetRef = NodeRef(toType, to);

                if (branch.Equals("true", StringComparison.OrdinalIgnoreCase))
                    AddUnique(trueTargets, targetRef);
                else if (branch.Equals("false", StringComparison.OrdinalIgnoreCase))
                    AddUnique(falseTargets, targetRef);
            }

            if (trueTargets.Count == 0 || falseTargets.Count == 0)
            {
                if (humanResultIf)
                    result.Errors.Add("Resultado de tarea humana sin ambas ramas: " + SafeLabel(label) + ". Debe tener salida APTO/SI y NO APTO/NO.");
                else
                    result.Errors.Add("Condición sin ramas completas: " + SafeLabel(label) + ". Debe tener una salida SI/true y una salida NO/false.");
                return;
            }

            if (trueTargets.Count > 1)
                result.Errors.Add("Condición con más de una salida SI/true: " + SafeLabel(label) + ". Debe haber una sola rama SI.");

            if (falseTargets.Count > 1)
                result.Errors.Add("Condición con más de una salida NO/false: " + SafeLabel(label) + ". Debe haber una sola rama NO.");

            if (trueTargets.Count == 1 && falseTargets.Count == 1
                && string.Equals(trueTargets[0], falseTargets[0], StringComparison.OrdinalIgnoreCase))
            {
                if (humanResultIf)
                    result.Errors.Add("Resultado de tarea humana con APTO y NO APTO al mismo destino: " + SafeLabel(label) + ". Deben ser ramas distintas.");
                else
                    result.Errors.Add("Condición con SI y NO al mismo destino: " + SafeLabel(label) + ". Deben ser ramas distintas.");
            }
        }

        private static bool IsHumanTaskResultIf(JObject ifAction)
        {
            JObject paramsObj = ifAction["params"] as JObject;
            if (paramsObj == null) return false;

            string field = Convert.ToString(paramsObj["field"] ?? "").Trim();
            return field.Equals("wf.tarea.resultado", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ConnectionFromAction(JObject connection, string nodeType, string label)
        {
            string from = Convert.ToString(connection["from"] ?? "").Trim();
            string fromType = Convert.ToString(connection["fromNodeType"] ?? "").Trim();

            if (!from.Equals(label, StringComparison.OrdinalIgnoreCase)) return false;
            if (fromType.Length == 0) return true;
            return fromType.Equals(nodeType, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBranchCondition(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == "si" || v == "sí" || v == "true") return "true";
            if (v == "no" || v == "false") return "false";
            return v;
        }

        private static bool KnownNodeRef(HashSet<string> knownRefs, string nodeType, string label)
        {
            if (knownRefs == null) return false;
            if (knownRefs.Contains(label)) return true;
            return knownRefs.Contains(NodeRef(nodeType, label));
        }

        private static string NodeRef(string nodeType, string label)
        {
            return (nodeType ?? "").Trim() + "|" + (label ?? "").Trim();
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list == null || value == null) return;
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(value);
        }

        private static string SafeLabel(string label)
        {
            label = (label ?? "").Trim();
            return label.Length == 0 ? "control.if" : label;
        }

        private static void ValidateAddNode(JObject action, WfAiCatalog catalog, WfAiValidationResult result)
        {
            string nodeType = Convert.ToString(action["nodeType"] ?? "").Trim();
            if (nodeType.Length == 0)
            {
                result.Errors.Add("ADD_NODE sin nodeType.");
                return;
            }

            var node = FindNode(catalog, nodeType);
            if (node == null)
            {
                result.Errors.Add("Nodo no permitido o inexistente para Asistente IA v1: " + nodeType);
                return;
            }

            ValidateParams(action, catalog, result);
            ValidateHumanTaskDestination(nodeType, action["params"] as JObject, result);
        }

        private static void ValidateHumanTaskDestination(string nodeType, JObject paramsObj, WfAiValidationResult result)
        {
            if (!nodeType.Equals("human.task", StringComparison.OrdinalIgnoreCase)) return;

            if (paramsObj == null)
            {
                result.Errors.Add("Tarea humana sin parámetros: debe indicar rol o usuario asignado.");
                return;
            }

            string rol = Convert.ToString(paramsObj["rol"] ?? "").Trim();
            string usuario = Convert.ToString(paramsObj["usuarioAsignado"] ?? "").Trim();

            if (rol.Length == 0 && usuario.Length == 0)
                result.Errors.Add("Tarea humana sin destino: debe indicar rol o usuario asignado.");
        }

        private static void ValidateParams(JObject action, WfAiCatalog catalog, WfAiValidationResult result)
        {
            string nodeType = Convert.ToString(action["nodeType"] ?? "").Trim();
            var node = FindNode(catalog, nodeType);
            var allowedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (node != null)
            {
                foreach (string p in node.Params)
                    allowedParams.Add(p);
            }

            // fix43b: el IF compuesto usa parámetros propios ya soportados por HIf.cs
            // y por el Constructor IA guiado/libre: rulesMode + rules.
            // Se agregan también acá como defensa si el catálogo viniera cacheado o incompleto.
            if (nodeType.Equals("control.if", StringComparison.OrdinalIgnoreCase))
            {
                allowedParams.Add("rulesMode");
                allowedParams.Add("rules");
            }

            var paramsObj = action["params"] as JObject;
            if (paramsObj == null) return;

            foreach (var prop in paramsObj.Properties())
            {
                if (allowedParams.Count > 0 && !allowedParams.Contains(prop.Name))
                    result.Errors.Add("Parámetro no permitido para " + nodeType + ": " + prop.Name);
            }

            ValidateIfCompoundRules(nodeType, paramsObj, catalog, result);

            string docTipo = Convert.ToString(paramsObj["docTipoCodigo"] ?? "").Trim();
            if (docTipo.Length > 0 && !DocTypeExists(catalog, docTipo))
                result.Errors.Add("DocTipo inexistente o inactivo: " + docTipo);

            string rol = Convert.ToString(paramsObj["rol"] ?? "").Trim();
            if (rol.Length > 0 && !RoleExists(catalog, rol))
                result.Errors.Add("Rol inexistente o inactivo: " + rol);

            string field = Convert.ToString(paramsObj["field"] ?? "").Trim();
            if (field.Length > 0 && !KnownField(catalog, field))
                result.Warnings.Add("Campo no encontrado en catálogo: " + field + ". Se deja como advertencia porque puede ser una variable dinámica válida.");
        }

        private static void ValidateIfCompoundRules(string nodeType, JObject paramsObj, WfAiCatalog catalog, WfAiValidationResult result)
        {
            if (!nodeType.Equals("control.if", StringComparison.OrdinalIgnoreCase)) return;
            if (paramsObj == null) return;

            JToken rulesToken = paramsObj["rules"];
            if (rulesToken == null) return;

            if (rulesToken.Type != JTokenType.Array)
            {
                result.Errors.Add("Parámetro rules de control.if debe ser un array.");
                return;
            }

            var rules = rulesToken as JArray;
            if (rules == null || rules.Count == 0)
            {
                result.Errors.Add("Condición compuesta sin reglas.");
                return;
            }

            foreach (JToken ruleToken in rules)
            {
                var rule = ruleToken as JObject;
                if (rule == null)
                {
                    result.Errors.Add("Una regla de control.if no es un objeto JSON válido.");
                    continue;
                }

                string field = Convert.ToString(rule["field"] ?? rule["fieldPath"] ?? "").Trim();
                if (field.Length == 0)
                {
                    result.Errors.Add("Una regla de control.if no informó field.");
                    continue;
                }

                if (!KnownField(catalog, field))
                    result.Warnings.Add("Campo no encontrado en catálogo: " + field + ". Se deja como advertencia porque puede ser una variable dinámica válida.");
            }
        }

        private static WfAiNodeInfo FindNode(WfAiCatalog catalog, string nodeType)
        {
            if (catalog == null || catalog.Nodes == null) return null;
            foreach (var n in catalog.Nodes)
            {
                if (string.Equals(n.Type, nodeType, StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return null;
        }

        private static bool DocTypeExists(WfAiCatalog catalog, string codigo)
        {
            if (catalog == null || catalog.DocTypes == null) return false;
            foreach (var d in catalog.DocTypes)
            {
                if (string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool RoleExists(WfAiCatalog catalog, string rol)
        {
            if (catalog == null || catalog.Roles == null) return false;
            foreach (var r in catalog.Roles)
            {
                if (string.Equals(r, rol, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool KnownField(WfAiCatalog catalog, string field)
        {
            if (field.StartsWith("wf.", StringComparison.OrdinalIgnoreCase)) return true;
            if (field.StartsWith("input.", StringComparison.OrdinalIgnoreCase)) return true;
            if (field.StartsWith("payload.", StringComparison.OrdinalIgnoreCase)) return true;
            if (field.StartsWith("sql.", StringComparison.OrdinalIgnoreCase)) return true;

            if (catalog == null || catalog.Fields == null) return false;
            foreach (var f in catalog.Fields)
            {
                if (string.Equals(f.Path, field, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
