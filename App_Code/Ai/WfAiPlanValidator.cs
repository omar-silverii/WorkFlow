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

            result.Ok = result.Errors.Count == 0;
            return result;
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

            var paramsObj = action["params"] as JObject;
            if (paramsObj == null) return;

            foreach (var prop in paramsObj.Properties())
            {
                if (allowedParams.Count > 0 && !allowedParams.Contains(prop.Name))
                    result.Errors.Add("Parámetro no permitido para " + nodeType + ": " + prop.Name);
            }

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
