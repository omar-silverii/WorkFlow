using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// FIX84C1/FIX84C2A/FIX84C2Ab/FIX84C2B/FIX84C2Bb/FIX84C2Bc/FIX84C2Bd/FIX84C2Bf: punto común de construcción semántica para nodos cubiertos.
    /// Frase y Paso a paso entregan candidatos; esta capa aplica contrato, defaults,
    /// reglas derivadas y validaciones antes de devolver el ADD_NODE normalizado.
    /// No ejecuta handlers, no toca canvas y deja nodos fuera de la cobertura sin modificar.
    /// </summary>
    public class WfAiResolvedNodeBuilder
    {
        private readonly WfAiCatalog _catalog;

        public WfAiResolvedNodeBuilder()
            : this(null)
        {
        }

        public WfAiResolvedNodeBuilder(WfAiCatalog catalog)
        {
            _catalog = catalog;
        }

        private static readonly HashSet<string> Covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "util.logger",
            "queue.consume",
            "queue.publish",
            "human.task"
        };

        public WfAiResolvedPlanResult ResolvePlan(JObject plan, string sourceText, string sourceKind)
        {
            var result = new WfAiResolvedPlanResult
            {
                Version = "fix84c2bf-common-node-v5",
                SourceKind = string.IsNullOrWhiteSpace(sourceKind) ? "unknown" : sourceKind.Trim(),
                Plan = plan == null ? new JObject() : (JObject)plan.DeepClone()
            };

            JArray actions = result.Plan["actions"] as JArray;
            if (actions == null)
            {
                result.Errors.Add("El plan no contiene actions[] para resolver.");
                return result;
            }

            var labelRenames = new List<WfAiNodeLabelRename>();
            int actionIndex = 0;
            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null)
                {
                    actionIndex++;
                    continue;
                }

                string actionKind = Text(action["action"]);
                string nodeType = Text(action["nodeType"]);
                if (!string.Equals(actionKind, "ADD_NODE", StringComparison.OrdinalIgnoreCase) || !Covered.Contains(nodeType))
                {
                    actionIndex++;
                    continue;
                }

                string oldLabel = Text(action["label"]);
                WfAiResolvedNode node = ResolveAction(action, actionIndex, sourceText ?? string.Empty, result.SourceKind, _catalog);
                string newLabel = Text(action["label"]);
                if (!string.Equals(oldLabel, newLabel, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(oldLabel)
                    && !string.IsNullOrWhiteSpace(newLabel))
                {
                    labelRenames.Add(new WfAiNodeLabelRename
                    {
                        NodeType = nodeType,
                        OldLabel = oldLabel,
                        NewLabel = newLabel
                    });
                }

                result.Nodes.Add(node);
                foreach (string error in node.Errors) AddUnique(result.Errors, error);
                foreach (string warning in node.Warnings) AddUnique(result.Warnings, warning);
                actionIndex++;
            }

            // FIX84C2Bd: si la capa común cambia la identidad visual de un nodo (human.task usa
            // params.titulo como label), el plan completo debe seguir siendo coherente. El provider
            // legacy ya había construido proposedConnections con el label anterior, por lo que
            // actualizamos esas referencias antes de validar. No tocamos conexiones del canvas real.
            ReconcileProposedConnectionLabels(result.Plan, labelRenames, result);

            // FIX84C2Bb: el provider legacy puede dejar missingData["rolUsuario"] aunque
            // la capa común ya haya resuelto un único destino explícito (Rol = ... o Usuario = ...).
            // El plan resuelto es la fuente semántica para la UI; limpiamos solamente ese faltante
            // legado cuando TODAS las human.task del plan tienen exactamente un destino.
            ReconcileLegacyHumanTaskMissingData(result.Plan);

            result.Ok = result.Errors.Count == 0;
            return result;
        }

        public static bool IsCovered(string nodeType)
        {
            return !string.IsNullOrWhiteSpace(nodeType) && Covered.Contains(nodeType.Trim());
        }

        private static WfAiResolvedNode ResolveAction(JObject action, int actionIndex, string sourceText, string sourceKind, WfAiCatalog catalog)
        {
            string nodeType = Text(action["nodeType"]);
            WfAiNodeConstructionContract contract = WfAiConstructionContractRegistry.Find(nodeType);
            var node = new WfAiResolvedNode
            {
                ActionIndex = actionIndex,
                NodeType = nodeType,
                Label = Text(action["label"]),
                Status = WfAiInterpretationStatus.Resolved
            };

            if (contract == null)
            {
                node.Status = WfAiInterpretationStatus.Unrecognized;
                node.Errors.Add("La capa común no encontró contrato para " + nodeType + ".");
                return node;
            }

            JObject parameters = action["params"] as JObject;
            if (parameters == null)
            {
                parameters = new JObject();
                action["params"] = parameters;
            }

            if (string.Equals(nodeType, "util.logger", StringComparison.OrdinalIgnoreCase))
                ResolveLogger(parameters, contract, sourceText, sourceKind, node);
            else if (string.Equals(nodeType, "queue.consume", StringComparison.OrdinalIgnoreCase))
                ResolveQueueConsume(parameters, contract, sourceText, sourceKind, node);
            else if (string.Equals(nodeType, "queue.publish", StringComparison.OrdinalIgnoreCase))
                ResolveQueuePublish(parameters, contract, sourceText, sourceKind, node);
            else if (string.Equals(nodeType, "human.task", StringComparison.OrdinalIgnoreCase))
            {
                ResolveHumanTask(parameters, contract, sourceText, sourceKind, node, catalog);

                // FIX84C2Bc: el título funcional de la tarea también es su identidad visual.
                // Frase y Paso a paso deben terminar mostrando el mismo label en el canvas;
                // no dejamos "Tarea humana" si ya existe un título resuelto.
                string resolvedTitle = Text(FindValue(parameters, "titulo"));
                if (!string.IsNullOrWhiteSpace(resolvedTitle))
                {
                    action["label"] = resolvedTitle;
                    node.Label = resolvedTitle;
                }
            }

            node.OutputFields.AddRange(contract.OutputFields ?? new List<string>());
            node.DynamicOutputPrefixes.AddRange(contract.DynamicOutputPrefixes ?? new List<string>());
            node.Status = node.Errors.Count == 0 ? AggregateStatus(node.Parameters) : WfAiInterpretationStatus.Missing;
            return node;
        }

        private static void ResolveLogger(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node)
        {
            WfAiParameterContract messageContract = contract.FindParameter("message");
            JToken messageToken = FindValue(p, "message");
            string message = Text(messageToken);
            bool messagePlaceholder = IsImplicitPlaceholder(messageContract, messageToken, sourceText, sourceKind);

            if (string.IsNullOrWhiteSpace(message) || messagePlaceholder)
            {
                string inferred = IsPhraseSource(sourceKind) ? InferStandaloneLoggerMessage(sourceText) : string.Empty;
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    p["message"] = inferred;
                    AddParameter(node, messageContract, new JValue(inferred), WfAiInterpretationStatus.Inferred, "natural_inference");
                }
                else
                {
                    RemoveProperty(p, "message");
                    AddParameter(node, messageContract, null, WfAiInterpretationStatus.Missing, messagePlaceholder ? "placeholder_rejected" : "not_supplied");
                    node.Errors.Add("util.logger: indicá qué querés registrar.");
                }
            }
            else
            {
                p["message"] = message;
                AddParameter(node, messageContract, new JValue(message), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract levelContract = contract.FindParameter("level");
            JToken levelToken = FindValue(p, "level");
            string level = NormalizeLoggerLevel(Text(levelToken));
            if (string.IsNullOrWhiteSpace(level))
            {
                level = Text(levelContract == null ? null : levelContract.DefaultValue);
                if (string.IsNullOrWhiteSpace(level)) level = "Info";
                p["level"] = level;
                AddParameter(node, levelContract, new JValue(level), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else if (!ContractOptionAllowed(levelContract, level))
            {
                p["level"] = level;
                AddParameter(node, levelContract, new JValue(level), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("util.logger: nivel no permitido '" + level + "'.");
            }
            else
            {
                p["level"] = level;
                bool implicitDefault = IsPhraseSource(sourceKind)
                    && string.Equals(level, "Info", StringComparison.OrdinalIgnoreCase)
                    && !PhraseMentionsLoggerLevel(sourceText);
                AddParameter(node, levelContract, new JValue(level), implicitDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                    implicitDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
            }
        }

        private static void ResolveQueuePublish(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node)
        {
            // FIX84C2Ab: antes de aplicar defaults, una frase puede usar la ayuda opcional
            // "Nombre = valor". Los nombres que coinciden con parámetros humanos del contrato
            // se aplican al nodo; los demás se consideran campos del mensaje de negocio.
            ApplyExplicitPublishAssignments(p, contract, sourceText, sourceKind);

            ResolveSafeString(p, contract.FindParameter("broker"), "sql", sourceKind, node);

            WfAiParameterContract queueContract = contract.FindParameter("queue");
            JToken queueToken = FindValue(p, "queue");
            string queue = Text(queueToken);
            bool queuePlaceholder = IsImplicitPlaceholder(queueContract, queueToken, sourceText, sourceKind);
            if (string.IsNullOrWhiteSpace(queue) || queuePlaceholder)
            {
                RemoveProperty(p, "queue");
                AddParameter(node, queueContract, null, WfAiInterpretationStatus.Missing,
                    queuePlaceholder ? "placeholder_rejected" : "not_supplied");
                node.Errors.Add("queue.publish: indicá en qué cola querés publicar.");
            }
            else
            {
                p["queue"] = queue;
                AddParameter(node, queueContract, new JValue(queue), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract payloadContract = contract.FindParameter("payload");
            JToken payloadToken = FindValue(p, "payload");
            bool payloadPlaceholder = IsImplicitPlaceholder(payloadContract, payloadToken, sourceText, sourceKind);
            if (!HasPayloadValue(payloadToken) || payloadPlaceholder)
            {
                RemoveProperty(p, "payload");
                AddParameter(node, payloadContract, null, WfAiInterpretationStatus.Missing,
                    payloadPlaceholder ? "placeholder_rejected" : "not_supplied");
                node.Errors.Add("queue.publish: indicá qué información querés publicar.");
            }
            else
            {
                JToken normalizedPayload = NormalizePublishPayload(payloadToken, sourceText, sourceKind);
                p["payload"] = normalizedPayload;
                AddParameter(node, payloadContract, normalizedPayload, WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            ResolveSafeString(p, contract.FindParameter("connectionStringName"), "DefaultConnection", sourceKind, node);
        }

        private static void ResolveQueueConsume(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node)
        {
            ResolveSafeString(p, contract.FindParameter("broker"), "sql", sourceKind, node);

            WfAiParameterContract queueContract = contract.FindParameter("queue");
            JToken queueToken = FindValue(p, "queue");
            string queue = Text(queueToken);
            bool queuePlaceholder = IsImplicitPlaceholder(queueContract, queueToken, sourceText, sourceKind);
            if (string.IsNullOrWhiteSpace(queue) || queuePlaceholder)
            {
                RemoveProperty(p, "queue");
                AddParameter(node, queueContract, null, WfAiInterpretationStatus.Missing,
                    queuePlaceholder ? "placeholder_rejected" : "not_supplied");
                node.Errors.Add("queue.consume: indicá de qué cola querés leer.");
            }
            else
            {
                p["queue"] = queue;
                AddParameter(node, queueContract, new JValue(queue), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract takeContract = contract.FindParameter("take");
            int take;
            JToken takeToken = FindValue(p, "take");
            if (!TryPositiveInt(takeToken, out take))
            {
                take = 1;
                p["take"] = take;
                AddParameter(node, takeContract, new JValue(take), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else
            {
                if (take > 100)
                {
                    take = 100;
                    node.Warnings.Add("queue.consume: take se normalizó al máximo de construcción guiada (100).");
                }
                p["take"] = take;
                bool implicitDefault = IsPhraseSource(sourceKind) && take == 1 && !PhraseMentionsConsumeCount(sourceText);
                AddParameter(node, takeContract, new JValue(take), implicitDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                    implicitDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract prefetchContract = contract.FindParameter("prefetch");
            p["prefetch"] = take;
            AddParameter(node, prefetchContract, new JValue(take), WfAiInterpretationStatus.Inferred, "derived_from:take");

            ResolveSafeString(p, contract.FindParameter("connectionStringName"), "DefaultConnection", sourceKind, node);
            ResolveSafeString(p, contract.FindParameter("outputPrefix"), "queue.consume", sourceKind, node);

            WfAiParameterContract debugContract = contract.FindParameter("debug");
            bool debug;
            JToken debugToken = FindValue(p, "debug");
            if (!TryBool(debugToken, out debug))
            {
                debug = false;
                p["debug"] = false;
                AddParameter(node, debugContract, new JValue(false), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else
            {
                p["debug"] = debug;
                bool implicitDefault = !debug && (debugToken == null || debugToken.Type == JTokenType.Null);
                AddParameter(node, debugContract, new JValue(debug), implicitDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                    implicitDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
            }
        }

        private static void ResolveHumanTask(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node, WfAiCatalog catalog)
        {
            ApplyExplicitHumanTaskAssignments(p, contract, sourceText, sourceKind);

            WfAiParameterContract roleContract = contract.FindParameter("rol");
            WfAiParameterContract userContract = contract.FindParameter("usuarioAsignado");
            string role = Text(FindValue(p, "rol"));
            string user = Text(FindValue(p, "usuarioAsignado"));

            // FIX84C2Bf: el usuario puede expresarse de forma humana (Usuario1, DisplayName o UserKey completo).
            // Se resuelve siempre contra WF_User ya cargado en el catálogo; nunca fabricamos el dominio.
            if (user.Length > 0 && catalog != null)
            {
                WfAiUserReferenceResolution userResolution = WfAiUserReferenceResolver.Resolve(catalog, user);
                if (userResolution.IsResolved)
                {
                    user = userResolution.UserKey;
                    p["usuarioAsignado"] = user;
                }
                else
                {
                    string originalUser = user;
                    user = string.Empty;
                    RemoveProperty(p, "usuarioAsignado");

                    // Si la frase había expresado también un rol, no lo usamos silenciosamente como salida
                    // mientras la referencia de usuario quedó sin resolver: forzamos una decisión visible.
                    if (role.Length > 0)
                    {
                        role = string.Empty;
                        RemoveProperty(p, "rol");
                    }

                    if (string.Equals(userResolution.Status, WfAiUserReferenceStatus.Ambiguous, StringComparison.OrdinalIgnoreCase))
                        node.Warnings.Add("human.task: el usuario '" + originalUser + "' coincide con más de un usuario activo; elegí el destinatario exacto.");
                    else
                        node.Warnings.Add("human.task: no encontré un usuario activo que coincida con '" + originalUser + "'; elegí un destinatario del catálogo.");
                }
            }

            if (role.Length > 0 && user.Length > 0)
            {
                p["rol"] = role;
                p["usuarioAsignado"] = user;
                AddParameter(node, roleContract, new JValue(role), WfAiInterpretationStatus.Ambiguous, SourceForExplicitValue(sourceKind));
                AddParameter(node, userContract, new JValue(user), WfAiInterpretationStatus.Ambiguous, SourceForExplicitValue(sourceKind));
                node.Errors.Add("human.task: se indicó rol y usuario al mismo tiempo; elegí un único destino.");
            }
            else if (role.Length > 0)
            {
                p["rol"] = role;
                RemoveProperty(p, "usuarioAsignado");
                AddParameter(node, roleContract, new JValue(role), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                AddParameter(node, userContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }
            else if (user.Length > 0)
            {
                p["usuarioAsignado"] = user;
                RemoveProperty(p, "rol");
                AddParameter(node, roleContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
                AddParameter(node, userContract, new JValue(user), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "rol");
                RemoveProperty(p, "usuarioAsignado");
                AddParameter(node, roleContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                AddParameter(node, userContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                node.Errors.Add("human.task: indicá un rol o un usuario destino.");
            }

            WfAiParameterContract titleContract = contract.FindParameter("titulo");
            bool explicitTitleInPhrase = HasExplicitHumanTaskAssignment(sourceText, "titulo");
            if (role.Length > 0 && user.Length > 0 && !explicitTitleInPhrase)
                p["titulo"] = "Tarea humana";

            JToken titleToken = FindValue(p, "titulo");
            string title = Text(titleToken);
            bool titlePlaceholder = IsImplicitPlaceholder(titleContract, titleToken, sourceText, sourceKind);
            if (title.Length == 0 || titlePlaceholder)
            {
                string inferredTitle = HumanTaskDefaultTitle(role, user);
                p["titulo"] = inferredTitle;
                AddParameter(node, titleContract, new JValue(inferredTitle), WfAiInterpretationStatus.Inferred, "visible_inference");
            }
            else
            {
                p["titulo"] = title;
                AddParameter(node, titleContract, new JValue(title), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract descriptionContract = contract.FindParameter("descripcion");
            JToken descriptionToken = FindValue(p, "descripcion");
            string description = Text(descriptionToken);
            bool descriptionPlaceholder = IsImplicitPlaceholder(descriptionContract, descriptionToken, sourceText, sourceKind);
            if (description.Length == 0 || descriptionPlaceholder)
            {
                RemoveProperty(p, "descripcion");
                AddParameter(node, descriptionContract, null, WfAiInterpretationStatus.Resolved,
                    descriptionPlaceholder ? "placeholder_rejected" : "optional_empty");
            }
            else
            {
                p["descripcion"] = description;
                AddParameter(node, descriptionContract, new JValue(description), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract deadlineContract = contract.FindParameter("deadlineMinutes");
            JToken deadlineToken = FindValue(p, "deadlineMinutes");
            int deadline;
            if (deadlineToken == null || deadlineToken.Type == JTokenType.Null || string.IsNullOrWhiteSpace(Text(deadlineToken)))
            {
                p["deadlineMinutes"] = 0;
                AddParameter(node, deadlineContract, new JValue(0), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else if (!TryNonNegativeInt(deadlineToken, out deadline))
            {
                AddParameter(node, deadlineContract, deadlineToken.DeepClone(), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("human.task: el vencimiento debe ser una cantidad de minutos mayor o igual a 0.");
            }
            else
            {
                p["deadlineMinutes"] = deadline;
                AddParameter(node, deadlineContract, new JValue(deadline), deadline == 0 ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                    deadline == 0 ? "safe_default" : SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract stateContract = contract.FindParameter("estadoNegocioPendiente");
            string pendingState = Text(FindValue(p, "estadoNegocioPendiente"));
            if (pendingState.Length > 0)
            {
                p["estadoNegocioPendiente"] = pendingState;
                AddParameter(node, stateContract, new JValue(pendingState), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "estadoNegocioPendiente");
                string derivedState = role.Length > 0 ? "Pendiente de " + role : "Pendiente";
                AddParameter(node, stateContract, new JValue(derivedState), WfAiInterpretationStatus.Inferred, "runtime_safe_default");
            }
        }

        private static void ApplyExplicitHumanTaskAssignments(
            JObject p,
            WfAiNodeConstructionContract contract,
            string sourceText,
            string sourceKind)
        {
            if (!ShouldApplyPhraseAssignments(sourceKind) || string.IsNullOrWhiteSpace(sourceText) || contract == null) return;

            string block = ExtractExplicitHumanTaskAssignmentBlock(sourceText);
            List<WfAiExplicitAssignment> assignments = WfAiExplicitAssignmentParser.ParseBlock(block);
            if (assignments == null || assignments.Count == 0) return;

            foreach (WfAiExplicitAssignment assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.Name)) continue;
                WfAiParameterContract parameter = HumanTaskParameterForAssignment(contract, assignment.Name);
                if (parameter == null) continue;
                p[parameter.Name] = CoerceExplicitAssignmentValue(assignment.Value, parameter);
            }
        }

        private static WfAiParameterContract HumanTaskParameterForAssignment(WfAiNodeConstructionContract contract, string humanName)
        {
            WfAiParameterContract direct = contract == null ? null : contract.FindParameterByHumanName(humanName);
            if (direct != null) return direct;

            string key = WfAiExplicitAssignmentParser.NormalizeHumanKey(humanName);
            if (key == "destino" || key == "rol") return contract.FindParameter("rol");
            if (key == "usuario" || key == "user" || key == "asignado") return contract.FindParameter("usuarioAsignado");
            if (key == "titulo" || key == "tarea") return contract.FindParameter("titulo");
            if (key == "descripcion" || key == "detalle") return contract.FindParameter("descripcion");
            if (key == "vencimiento" || key == "deadline" || key == "minutos") return contract.FindParameter("deadlineMinutes");
            if (key == "estado" || key == "estadopendiente") return contract.FindParameter("estadoNegocioPendiente");
            return null;
        }

        private static bool HasExplicitHumanTaskAssignment(string sourceText, string parameterName)
        {
            string block = ExtractExplicitHumanTaskAssignmentBlock(sourceText);
            List<WfAiExplicitAssignment> assignments = WfAiExplicitAssignmentParser.ParseBlock(block);
            foreach (WfAiExplicitAssignment assignment in assignments ?? new List<WfAiExplicitAssignment>())
            {
                if (assignment == null) continue;
                string key = WfAiExplicitAssignmentParser.NormalizeHumanKey(assignment.Name);
                string wanted = WfAiExplicitAssignmentParser.NormalizeHumanKey(parameterName);
                if (key == wanted) return true;
            }
            return false;
        }

        private static string ExtractExplicitHumanTaskAssignmentBlock(string sourceText)
        {
            string text = (sourceText ?? string.Empty).Trim();
            if (text.Length == 0 || text.IndexOf('=') < 0) return string.Empty;

            Match marker = Regex.Match(text,
                @"\b(?:crear|crea|creá|mandar|manda|mandá|enviar|envia|enviá|asignar|asigna|asigná)\b(?:\s+(?:una|la))?\s+tarea(?:\s+humana)?\b",
                RegexOptions.IgnoreCase);
            if (!marker.Success) return string.Empty;

            string tail = text.Substring(marker.Index + marker.Length);
            Match nextAction = Regex.Match(tail,
                @"(?:[.;,]\s*|\s+)(?:y\s+)?(?:luego|despues|después|finalmente)\s+(?=(?:publicar|consumir|leer|registrar|notificar|finalizar|terminar|crear\s+otra\s+tarea|mandar\s+otra\s+tarea|enviar\s+otra\s+tarea)\b)",
                RegexOptions.IgnoreCase);
            if (nextAction.Success) tail = tail.Substring(0, nextAction.Index);

            Match firstAssignment = Regex.Match(tail,
                @"(?<name>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ_][A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ_\.\-]*)\s*=",
                RegexOptions.IgnoreCase);
            if (!firstAssignment.Success) return string.Empty;
            return tail.Substring(firstAssignment.Groups["name"].Index).Trim();
        }

        private static string HumanTaskDefaultTitle(string role, string user)
        {
            if (!string.IsNullOrWhiteSpace(user)) return "Enviar a " + user.Trim();
            string value = (role ?? string.Empty).Trim();
            if (value.Length == 0) return "Tarea humana";
            if (value.Equals("COMPRAS", StringComparison.OrdinalIgnoreCase)) return "Enviar a Compras";
            if (value.Equals("DIR_GENERAL", StringComparison.OrdinalIgnoreCase)) return "Aprobación Dirección";
            if (value.Equals("ADM_FIN", StringComparison.OrdinalIgnoreCase)) return "Enviar a Administración";
            if (value.Equals("OPERACIONES", StringComparison.OrdinalIgnoreCase)) return "Enviar a Operaciones";
            if (value.Equals("IT", StringComparison.OrdinalIgnoreCase)) return "Enviar a IT";
            return "Enviar a " + value;
        }

        private static void ApplyExplicitPublishAssignments(
            JObject p,
            WfAiNodeConstructionContract contract,
            string sourceText,
            string sourceKind)
        {
            if (!ShouldApplyPhraseAssignments(sourceKind) || string.IsNullOrWhiteSpace(sourceText) || contract == null) return;

            string block = ExtractExplicitPublishAssignmentBlock(sourceText);
            List<WfAiExplicitAssignment> assignments = WfAiExplicitAssignmentParser.ParseBlock(block);
            if (assignments == null || assignments.Count == 0) return;

            var payloadFields = new JObject();
            foreach (WfAiExplicitAssignment assignment in assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.Name)) continue;

                WfAiParameterContract parameter = contract.FindParameterByHumanName(assignment.Name);
                if (parameter == null)
                {
                    payloadFields[assignment.Name] = assignment.Value == null
                        ? JValue.CreateNull()
                        : assignment.Value.DeepClone();
                    continue;
                }

                if (string.Equals(parameter.Name, "payload", StringComparison.OrdinalIgnoreCase))
                {
                    p["payload"] = assignment.Value == null
                        ? JValue.CreateNull()
                        : assignment.Value.DeepClone();
                    continue;
                }

                p[parameter.Name] = CoerceExplicitAssignmentValue(assignment.Value, parameter);
            }

            // Los campos libres describen contenido estructurado del mensaje. Si hay al menos
            // uno, tienen prioridad sobre el texto provisional fabricado por el provider legacy.
            if (payloadFields.Count > 0)
                p["payload"] = payloadFields;
        }

        private static string ExtractExplicitPublishAssignmentBlock(string sourceText)
        {
            string text = (sourceText ?? string.Empty).Trim();
            if (text.Length == 0 || text.IndexOf('=') < 0) return string.Empty;

            Match marker = Regex.Match(text,
                @"\b(?:publicar|publica|publicá|encolar)\b|\b(?:mandar|enviar)\s+(?:un\s+mensaje\s+)?a\s+(?:la\s+)?cola\b",
                RegexOptions.IgnoreCase);
            if (!marker.Success) return string.Empty;

            string tail = text.Substring(marker.Index + marker.Length);

            // No se considera ';' un fin de contenido por sí solo: en la sintaxis de precisión
            // separa campos. Sólo corta cuando introduce una acción posterior inequívoca.
            Match nextAction = Regex.Match(tail,
                @"(?:[.;,]\s*|\s+)(?:y\s+)?(?:luego|despues|después|finalmente)\s+(?=(?:consumir|leer|tomar|registrar|notificar|finalizar|terminar|crear\s+una\s+tarea|mandar\s+una\s+tarea|enviar\s+una\s+tarea)\b)",
                RegexOptions.IgnoreCase);
            if (nextAction.Success)
                tail = tail.Substring(0, nextAction.Index);

            Match firstAssignment = Regex.Match(tail,
                @"(?<name>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ_][A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ_\.\-]*)\s*=",
                RegexOptions.IgnoreCase);
            if (!firstAssignment.Success) return string.Empty;

            return tail.Substring(firstAssignment.Groups["name"].Index).Trim();
        }

        private static JToken CoerceExplicitAssignmentValue(JToken value, WfAiParameterContract parameter)
        {
            if (value == null || parameter == null) return value == null ? JValue.CreateNull() : value.DeepClone();

            string dataType = (parameter.DataType ?? string.Empty).Trim().ToLowerInvariant();
            string text = Text(value);

            if (dataType == "number")
            {
                decimal number;
                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out number)
                    || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                    return new JValue(number);
            }
            else if (dataType == "boolean")
            {
                bool boolean;
                if (TryBool(value, out boolean))
                    return new JValue(boolean);
            }
            else if (dataType == "object"
                && ((text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
                    || (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))))
            {
                try { return JToken.Parse(text); }
                catch { }
            }

            return value.DeepClone();
        }

        private static bool HasPayloadValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return false;
            if (token.Type == JTokenType.String) return !string.IsNullOrWhiteSpace(token.Value<string>());
            return true;
        }

        private static JToken NormalizePublishPayload(JToken token, string sourceText, string sourceKind)
        {
            if (token == null) return JValue.CreateNull();
            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return token.DeepClone();

            string raw = Text(token);
            if (raw.Length == 0) return new JValue(string.Empty);

            // FIX84C2A: si la frase expresa campos del mensaje como
            // "origen: Prueba e instanceId: ${wf.instanceId}", se conserva una
            // estructura de negocio sin exigir JSON ni la palabra técnica payload.
            if (IsPhraseSource(sourceKind))
            {
                JObject naturalObject;
                if (TryParseNaturalPublishFields(raw, out naturalObject))
                    return naturalObject;
            }

            if ((raw.StartsWith("{", StringComparison.Ordinal) && raw.EndsWith("}", StringComparison.Ordinal))
                || (raw.StartsWith("[", StringComparison.Ordinal) && raw.EndsWith("]", StringComparison.Ordinal)))
            {
                try { return JToken.Parse(raw); }
                catch { }
            }

            return new JValue(raw);
        }

        private static bool TryParseNaturalPublishFields(string raw, out JObject obj)
        {
            obj = null;
            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0 || text.IndexOf(':') < 0) return false;

            MatchCollection matches = Regex.Matches(text,
                @"(?:^|[,;]\s*|\s+(?:y|e)\s+)(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<value>.+?)(?=(?:\s+(?:y|e)\s+[A-Za-z_][A-Za-z0-9_]*\s*:)|[,;]\s*[A-Za-z_][A-Za-z0-9_]*\s*:|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (matches.Count < 2) return false;

            var result = new JObject();
            foreach (Match match in matches)
            {
                string name = (match.Groups["name"].Value ?? string.Empty).Trim();
                string value = (match.Groups["value"].Value ?? string.Empty).Trim().TrimEnd('.', ',', ';').Trim();
                if (name.Length == 0 || value.Length == 0 || result[name] != null) return false;
                result[name] = new JValue(value);
            }

            if (result.Count < 2) return false;
            obj = result;
            return true;
        }

        private static void ResolveSafeString(JObject p, WfAiParameterContract contract, string fallback, string sourceKind, WfAiResolvedNode node)
        {
            if (contract == null) return;
            JToken token = FindValue(p, contract.Name);
            string value = Text(token);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Text(contract.DefaultValue);
                if (string.IsNullOrWhiteSpace(value)) value = fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            p[contract.Name] = value;
            bool sameAsDefault = contract.DefaultValue != null
                && string.Equals(value, Text(contract.DefaultValue), StringComparison.OrdinalIgnoreCase);
            AddParameter(node, contract, new JValue(value),
                sameAsDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                sameAsDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static void AddParameter(WfAiResolvedNode node, WfAiParameterContract contract, JToken value, string status, string source)
        {
            node.Parameters.Add(new WfAiResolvedParameter
            {
                Name = contract == null ? string.Empty : contract.Name,
                Label = contract == null ? string.Empty : contract.Label,
                Value = value == null ? null : value.DeepClone(),
                Status = status,
                Source = source
            });
        }

        private static string AggregateStatus(List<WfAiResolvedParameter> parameters)
        {
            bool inferred = false;
            foreach (WfAiResolvedParameter parameter in parameters)
            {
                if (parameter == null) continue;
                if (string.Equals(parameter.Status, WfAiInterpretationStatus.Missing, StringComparison.OrdinalIgnoreCase)) return WfAiInterpretationStatus.Missing;
                if (string.Equals(parameter.Status, WfAiInterpretationStatus.Unrecognized, StringComparison.OrdinalIgnoreCase)) return WfAiInterpretationStatus.Unrecognized;
                if (string.Equals(parameter.Status, WfAiInterpretationStatus.Ambiguous, StringComparison.OrdinalIgnoreCase)) return WfAiInterpretationStatus.Ambiguous;
                if (string.Equals(parameter.Status, WfAiInterpretationStatus.Inferred, StringComparison.OrdinalIgnoreCase)) inferred = true;
            }
            return inferred ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved;
        }

        private static string InferStandaloneLoggerMessage(string sourceText)
        {
            string text = Regex.Replace(sourceText ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length == 0) return string.Empty;

            Match m = Regex.Match(text,
                @"^\s*(?:registrar|registrá|registra|dejar\s+constancia(?:\s+de)?)\s+(?:un\s+)?(?:log(?:ger)?|evento)?\s*(?:informativo|info|advertencia|warning|warn|error|debug|trace|fatal)?\s*(?:con\s+(?:el\s+)?mensaje\s+|indicando(?:\s+que)?\s+)?(?<msg>.+?)\s*[\.;,]?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return string.Empty;

            string msg = (m.Groups["msg"].Value ?? string.Empty).Trim();
            while (msg.EndsWith(".", StringComparison.Ordinal) || msg.EndsWith(",", StringComparison.Ordinal) || msg.EndsWith(";", StringComparison.Ordinal))
                msg = msg.Substring(0, msg.Length - 1).Trim();

            if (msg.Length == 0) return string.Empty;
            string normalized = msg.ToLowerInvariant();
            if (normalized == "un log" || normalized == "log" || normalized == "logger" || normalized == "evento") return string.Empty;
            return msg;
        }

        private static bool PhraseMentionsLoggerLevel(string sourceText)
        {
            return Regex.IsMatch(sourceText ?? string.Empty, @"\b(info|informativo|warn|warning|advertencia|error|debug|trace|fatal)\b", RegexOptions.IgnoreCase);
        }

        private static bool PhraseMentionsConsumeCount(string sourceText)
        {
            return Regex.IsMatch(sourceText ?? string.Empty, @"\b(?:leer|consumir|tomar)\s+(?:\d+|un|una|dos|tres|cuatro|cinco)\s+mensajes?\b", RegexOptions.IgnoreCase);
        }

        private static string NormalizeLoggerLevel(string value)
        {
            string raw = (value ?? string.Empty).Trim();
            if (raw.Length == 0) return string.Empty;
            if (raw.Equals("warning", StringComparison.OrdinalIgnoreCase)) return "Warn";
            if (raw.Equals("warn", StringComparison.OrdinalIgnoreCase)) return "Warn";
            if (raw.Equals("info", StringComparison.OrdinalIgnoreCase)) return "Info";
            if (raw.Equals("error", StringComparison.OrdinalIgnoreCase)) return "Error";
            if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)) return "Debug";
            if (raw.Equals("trace", StringComparison.OrdinalIgnoreCase)) return "Trace";
            if (raw.Equals("fatal", StringComparison.OrdinalIgnoreCase)) return "Fatal";
            return raw;
        }

        private static bool ContractOptionAllowed(WfAiParameterContract contract, string value)
        {
            if (contract == null || contract.Options == null || contract.Options.Count == 0) return true;
            foreach (string option in contract.Options)
                if (string.Equals((option ?? string.Empty).Trim(), (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsImplicitPlaceholder(WfAiParameterContract contract, JToken value, string sourceText, string sourceKind)
        {
            if (!IsPhraseSource(sourceKind)) return false;
            if (contract == null || value == null || contract.PlaceholderValues == null) return false;

            string text = Text(value);
            bool listed = false;
            foreach (string placeholder in contract.PlaceholderValues)
            {
                if (string.Equals((placeholder ?? string.Empty).Trim(), text, StringComparison.OrdinalIgnoreCase))
                {
                    listed = true;
                    break;
                }
            }
            if (!listed) return false;

            string source = (sourceText ?? string.Empty).Trim().ToLowerInvariant();
            string wanted = text.Trim().ToLowerInvariant();
            return wanted.Length == 0 || !source.Contains(wanted);
        }

        private static JToken FindValue(JObject obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return null;
            JToken direct = obj[name];
            if (direct != null) return direct;
            foreach (JProperty prop in obj.Properties())
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) return prop.Value;
            return null;
        }

        private static void RemoveProperty(JObject obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return;
            JProperty found = null;
            foreach (JProperty prop in obj.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = prop;
                    break;
                }
            }
            if (found != null) found.Remove();
        }

        private static bool TryNonNegativeInt(JToken token, out int value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null) return false;
            return int.TryParse(Convert.ToString(token, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool TryPositiveInt(JToken token, out int value)
        {
            value = 0;
            if (token == null || token.Type == JTokenType.Null) return false;
            return int.TryParse(Convert.ToString(token, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static bool TryBool(JToken token, out bool value)
        {
            value = false;
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }
            return bool.TryParse(Convert.ToString(token, CultureInfo.InvariantCulture), out value);
        }


        private sealed class WfAiNodeLabelRename
        {
            public string NodeType { get; set; }
            public string OldLabel { get; set; }
            public string NewLabel { get; set; }
        }

        private static void ReconcileProposedConnectionLabels(
            JObject plan,
            List<WfAiNodeLabelRename> renames,
            WfAiResolvedPlanResult result)
        {
            if (plan == null || renames == null || renames.Count == 0) return;
            JArray proposed = plan["proposedConnections"] as JArray;
            if (proposed == null || proposed.Count == 0) return;

            foreach (WfAiNodeLabelRename rename in renames)
            {
                if (rename == null || string.IsNullOrWhiteSpace(rename.OldLabel) || string.IsNullOrWhiteSpace(rename.NewLabel)) continue;

                int sameSourceCount = 0;
                foreach (WfAiNodeLabelRename candidate in renames)
                {
                    if (candidate == null) continue;
                    if (string.Equals(candidate.NodeType, rename.NodeType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(candidate.OldLabel, rename.OldLabel, StringComparison.OrdinalIgnoreCase))
                        sameSourceCount++;
                }

                // Si un plan futuro trae dos nodos del mismo tipo con exactamente el mismo label
                // viejo y ambos se renombran, proposedConnections no contiene ids suficientes para
                // distinguirlos con seguridad. No adivinamos.
                if (sameSourceCount > 1)
                {
                    AddUnique(result.Warnings, "No se pudieron reconciliar automáticamente conexiones para el label duplicado '"
                        + rename.OldLabel + "' de " + rename.NodeType + ".");
                    continue;
                }

                foreach (JToken token in proposed)
                {
                    JObject connection = token as JObject;
                    if (connection == null) continue;

                    string from = Text(connection["from"]);
                    string fromType = Text(connection["fromNodeType"]);
                    if (string.Equals(from, rename.OldLabel, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(fromType) || string.Equals(fromType, rename.NodeType, StringComparison.OrdinalIgnoreCase)))
                    {
                        connection["from"] = rename.NewLabel;
                    }

                    string to = Text(connection["to"]);
                    string toType = Text(connection["toNodeType"]);
                    if (string.Equals(to, rename.OldLabel, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(toType) || string.Equals(toType, rename.NodeType, StringComparison.OrdinalIgnoreCase)))
                    {
                        connection["to"] = rename.NewLabel;
                    }
                }
            }
        }

        private static void ReconcileLegacyHumanTaskMissingData(JObject plan)
        {
            if (plan == null) return;

            JArray actions = plan["actions"] as JArray;
            if (actions == null) return;

            bool hasHumanTask = false;
            bool allHaveSingleDestination = true;

            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null) continue;
                if (!string.Equals(Text(action["action"]), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Text(action["nodeType"]), "human.task", StringComparison.OrdinalIgnoreCase)) continue;

                hasHumanTask = true;
                JObject parameters = action["params"] as JObject ?? new JObject();
                bool hasRole = !string.IsNullOrWhiteSpace(Text(FindValue(parameters, "rol")));
                bool hasUser = !string.IsNullOrWhiteSpace(Text(FindValue(parameters, "usuarioAsignado")));
                if (hasRole == hasUser)
                {
                    allHaveSingleDestination = false;
                    break;
                }
            }

            if (!hasHumanTask || !allHaveSingleDestination) return;

            JArray missing = plan["missingData"] as JArray;
            if (missing == null) return;

            for (int i = missing.Count - 1; i >= 0; i--)
            {
                JObject item = missing[i] as JObject;
                if (item == null) continue;
                if (string.Equals(Text(item["key"]), "rolUsuario", StringComparison.OrdinalIgnoreCase))
                    missing.RemoveAt(i);
            }
        }

        private static bool IsPhraseSource(string sourceKind)
        {
            return string.Equals(sourceKind, "phrase", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "phrase_resolved", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldApplyPhraseAssignments(string sourceKind)
        {
            // Las asignaciones Nombre = valor se aplican una sola vez sobre el candidato original.
            // Después de una aclaración, el plan resuelto tiene prioridad sobre la frase original.
            return string.Equals(sourceKind, "phrase", StringComparison.OrdinalIgnoreCase);
        }

        private static string SourceForExplicitValue(string sourceKind)
        {
            return IsPhraseSource(sourceKind) ? "phrase_plan" : "step_by_step";
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            return token.Type == JTokenType.String ? (token.Value<string>() ?? string.Empty).Trim() : Convert.ToString(token, CultureInfo.InvariantCulture).Trim();
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return;
            foreach (string item in list)
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(value);
        }
    }

    public class WfAiResolvedPlanResult
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("sourceKind")]
        public string SourceKind { get; set; }

        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("plan")]
        public JObject Plan { get; set; }

        [JsonProperty("nodes")]
        public List<WfAiResolvedNode> Nodes { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        public WfAiResolvedPlanResult()
        {
            Nodes = new List<WfAiResolvedNode>();
            Errors = new List<string>();
            Warnings = new List<string>();
            Ok = true;
        }
    }

    public class WfAiResolvedNode
    {
        [JsonProperty("actionIndex")]
        public int ActionIndex { get; set; }

        [JsonProperty("nodeType")]
        public string NodeType { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("parameters")]
        public List<WfAiResolvedParameter> Parameters { get; set; }

        [JsonProperty("outputFields")]
        public List<string> OutputFields { get; set; }

        [JsonProperty("dynamicOutputPrefixes")]
        public List<string> DynamicOutputPrefixes { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; }

        public WfAiResolvedNode()
        {
            Parameters = new List<WfAiResolvedParameter>();
            OutputFields = new List<string>();
            DynamicOutputPrefixes = new List<string>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }
    }

    public class WfAiResolvedParameter
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
    }
}
