using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// FIX84C1/FIX84C2A/FIX84C2Ab/FIX84C2B/FIX84C2Bb/FIX84C2Bc/FIX84C2Bd/FIX84C2Bf/FIX84C2C1/FIX84C2D1/FIX84C2D2/FIX84C2D3/FIX84C2D4: punto común de construcción semántica para nodos cubiertos.
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
            "human.task",
            "control.if",
            "util.notify",
            "file.write",
            "file.read",
            "state.vars"
        };

        public WfAiResolvedPlanResult ResolvePlan(JObject plan, string sourceText, string sourceKind)
        {
            var result = new WfAiResolvedPlanResult
            {
                Version = "fix84c2d4-common-node-v10",
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
            else if (string.Equals(nodeType, "control.if", StringComparison.OrdinalIgnoreCase))
                ResolveControlIf(parameters, contract, sourceText, sourceKind, node);
            else if (string.Equals(nodeType, "file.read", StringComparison.OrdinalIgnoreCase))
            {
                ResolveFileRead(parameters, contract, sourceText, sourceKind, node);

                string currentLabel = Text(action["label"]);
                if (currentLabel.Length == 0 || currentLabel.Equals("Archivo: Leer", StringComparison.OrdinalIgnoreCase))
                {
                    action["label"] = "Leer archivo";
                    node.Label = "Leer archivo";
                }
            }
            else if (string.Equals(nodeType, "file.write", StringComparison.OrdinalIgnoreCase))
            {
                ResolveFileWrite(parameters, contract, sourceText, sourceKind, node, catalog);

                // FIX84C2D2: Paso a paso usaba el rótulo técnico "Archivo: Escribir" mientras
                // Frase usa "Escribir archivo". Son la misma intención; unificamos únicamente
                // el rótulo genérico sin cambiar labels específicos ya existentes en ramas.
                string currentLabel = Text(action["label"]);
                if (currentLabel.Length == 0 || currentLabel.Equals("Archivo: Escribir", StringComparison.OrdinalIgnoreCase))
                {
                    action["label"] = "Escribir archivo";
                    node.Label = "Escribir archivo";
                }
            }
            else if (string.Equals(nodeType, "state.vars", StringComparison.OrdinalIgnoreCase))
            {
                ResolveStateVars(parameters, contract, sourceText, sourceKind, node);

                // FIX84C2D4: con datos resueltos preservamos el label histórico "Definir variables"
                // (L_STATE_VARS_LOGGER) y hacemos que Paso a paso muestre la misma identidad visual.
                // Si todavía falta el cambio, conservamos Guardar/Quitar para que el diálogo sepa
                // qué operación pidió la persona sin inventar un parámetro técnico adicional.
                bool hasResolvedStateChange = HasMeaningfulToken(FindValue(parameters, "set"))
                    || HasMeaningfulToken(FindValue(parameters, "remove"));
                string currentStateLabel = Text(action["label"]);
                if (hasResolvedStateChange
                    || (currentStateLabel.IndexOf("Guardar", StringComparison.OrdinalIgnoreCase) < 0
                        && currentStateLabel.IndexOf("Quitar", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    action["label"] = "Definir variables";
                    node.Label = "Definir variables";
                }
            }
            else if (string.Equals(nodeType, "util.notify", StringComparison.OrdinalIgnoreCase))
            {
                ResolveNotify(parameters, contract, sourceText, sourceKind, node, catalog);

                string destination = Text(FindValue(parameters, "usuarioDestino"));
                if (destination.Length == 0) destination = Text(FindValue(parameters, "rolDestino"));
                string currentLabel = Text(action["label"]);
                if (destination.Length > 0
                    && (currentLabel.Length == 0
                        || currentLabel.Equals("Notificar", StringComparison.OrdinalIgnoreCase)
                        || currentLabel.Equals("Notificación", StringComparison.OrdinalIgnoreCase)))
                {
                    action["label"] = "Notificar a " + destination;
                    node.Label = Text(action["label"]);
                }
            }
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

        // FIX84C2D3: file.read usa el mismo contrato para Frase y Paso a paso.
        // La ruta es una decisión real y nunca se fabrica. El nombre de salida y el resto
        // reciben sólo defaults técnicos compatibles con HFileRead.
        private static void ResolveFileRead(
            JObject p,
            WfAiNodeConstructionContract contract,
            string sourceText,
            string sourceKind,
            WfAiResolvedNode node)
        {
            WfAiParameterContract pathContract = contract.FindParameter("path");
            JToken pathToken = FindValue(p, "path");
            string path = Text(pathToken);
            bool pathPlaceholder = IsImplicitPlaceholder(pathContract, pathToken, sourceText, sourceKind);
            if (path.Length == 0 || pathPlaceholder)
            {
                string inferredPath = IsPhraseSource(sourceKind) ? InferFileReadPath(sourceText) : string.Empty;
                if (inferredPath.Length > 0)
                {
                    path = inferredPath;
                    p["path"] = path;
                    AddParameter(node, pathContract, new JValue(path), WfAiInterpretationStatus.Inferred, "natural_inference");
                }
                else
                {
                    RemoveProperty(p, "path");
                    AddParameter(node, pathContract, null, WfAiInterpretationStatus.Missing,
                        pathPlaceholder ? "placeholder_rejected" : "not_supplied");
                    node.Errors.Add("file.read: indicá la ruta del archivo a leer.");
                }
            }
            else
            {
                p["path"] = path;
                AddParameter(node, pathContract, new JValue(path), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            // HFileRead acepta output como alias de salida. La representación contractual
            // común conserva sólo salida para que Frase y Paso a paso comparen lo mismo.
            WfAiParameterContract outputContract = contract.FindParameter("salida");
            string salida = Text(FindValue(p, "salida"));
            if (salida.Length == 0) salida = Text(FindValue(p, "output"));
            RemoveProperty(p, "output");
            if (salida.Length == 0)
            {
                salida = Text(outputContract == null ? null : outputContract.DefaultValue);
                if (salida.Length == 0) salida = "archivo";
                p["salida"] = salida;
                AddParameter(node, outputContract, new JValue(salida), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else if (!IsValidStatePath(salida))
            {
                p["salida"] = salida;
                AddParameter(node, outputContract, new JValue(salida), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.read: la salida debe ser una clave válida de contexto, por ejemplo archivo o biz.archivo.texto.");
            }
            else if (salida.StartsWith("file.read.", StringComparison.OrdinalIgnoreCase))
            {
                p["salida"] = salida;
                AddParameter(node, outputContract, new JValue(salida), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.read: la salida no puede usar file.read.* porque esas claves están reservadas para metadatos técnicos.");
            }
            else
            {
                p["salida"] = salida;
                bool defaultOutput = outputContract != null && outputContract.DefaultValue != null
                    && string.Equals(salida, Text(outputContract.DefaultValue), StringComparison.OrdinalIgnoreCase);
                AddParameter(node, outputContract, new JValue(salida), defaultOutput ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                    defaultOutput ? "safe_default" : SourceForExplicitValue(sourceKind));
            }

            ResolveFileReadSafeBoolean(p, contract.FindParameter("asJson"), false, sourceKind, node);
            ResolveFileReadSafeString(p, contract.FindParameter("encoding"), "utf-8", sourceKind, node, false);
            ResolveFileReadSafeString(p, contract.FindParameter("zipMode"), "auto", sourceKind, node, true);
            ResolveFileReadSafeBoolean(p, contract.FindParameter("useCache"), true, sourceKind, node);

            WfAiParameterContract zipEntryContract = contract.FindParameter("zipEntry");
            string zipEntry = Text(FindValue(p, "zipEntry"));
            if (zipEntry.Length > 0)
            {
                p["zipEntry"] = zipEntry;
                AddParameter(node, zipEntryContract, new JValue(zipEntry), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "zipEntry");
                AddParameter(node, zipEntryContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }
        }

        private static void ResolveFileReadSafeString(
            JObject p,
            WfAiParameterContract contract,
            string fallback,
            string sourceKind,
            WfAiResolvedNode node,
            bool enforceOptions)
        {
            if (contract == null) return;
            string value = Text(FindValue(p, contract.Name));
            if (value.Length == 0)
            {
                value = Text(contract.DefaultValue);
                if (value.Length == 0) value = fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            if (string.Equals(contract.Name, "zipMode", StringComparison.OrdinalIgnoreCase))
                value = value.Trim().ToLowerInvariant();
            p[contract.Name] = value;

            if (enforceOptions && !ContractOptionAllowed(contract, value))
            {
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.read: " + contract.Label + " no permitido '" + value + "'.");
                return;
            }

            bool sameAsDefault = contract.DefaultValue != null
                && string.Equals(value, Text(contract.DefaultValue), StringComparison.OrdinalIgnoreCase);
            AddParameter(node, contract, new JValue(value), sameAsDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                sameAsDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static void ResolveFileReadSafeBoolean(
            JObject p,
            WfAiParameterContract contract,
            bool fallback,
            string sourceKind,
            WfAiResolvedNode node)
        {
            if (contract == null) return;
            JToken token = FindValue(p, contract.Name);
            bool value;
            if (token == null || token.Type == JTokenType.Null || Text(token).Length == 0)
            {
                value = contract.DefaultValue != null && contract.DefaultValue.Type == JTokenType.Boolean
                    ? contract.DefaultValue.Value<bool>()
                    : fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            if (token.Type == JTokenType.Boolean) value = token.Value<bool>();
            else if (!bool.TryParse(Text(token), out value))
            {
                AddParameter(node, contract, token.DeepClone(), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.read: " + contract.Label + " debe ser Sí o No.");
                return;
            }

            p[contract.Name] = value;
            bool defaultValue = contract.DefaultValue != null && contract.DefaultValue.Type == JTokenType.Boolean
                ? contract.DefaultValue.Value<bool>()
                : fallback;
            AddParameter(node, contract, new JValue(value), value == defaultValue ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                value == defaultValue ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static string InferFileReadPath(string sourceText)
        {
            string text = sourceText ?? string.Empty;
            Match token = Regex.Match(text,
                @"\b(?:leer|abrir)\s+(?:el\s+)?archivo\s+(?<path>\$\{[^}]+\})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (token.Success) return (token.Groups["path"].Value ?? string.Empty).Trim();

            Match path = Regex.Match(text,
                @"\b(?:leer|abrir)\s+(?:el\s+)?archivo\s+(?<path>[A-Za-z]:\\[^\r\n,;]+?\.[A-Za-z0-9]{1,8}|/[^\r\n,;]+?\.[A-Za-z0-9]{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return path.Success ? (path.Groups["path"].Value ?? string.Empty).Trim() : string.Empty;
        }

        // FIX84C2D2: file.write usa el mismo contrato para Frase y Paso a paso.
        // Ruta y contenido/origen son decisiones declarativas; no se fabrican. Solamente
        // encoding, overwrite y zipMode reciben defaults técnicos seguros.
        private static void ResolveFileWrite(
            JObject p,
            WfAiNodeConstructionContract contract,
            string sourceText,
            string sourceKind,
            WfAiResolvedNode node,
            WfAiCatalog catalog)
        {
            WfAiParameterContract pathContract = contract.FindParameter("path");
            JToken pathToken = FindValue(p, "path");
            string path = Text(pathToken);
            bool pathPlaceholder = IsImplicitPlaceholder(pathContract, pathToken, sourceText, sourceKind);
            if (path.Length == 0 || pathPlaceholder)
            {
                string inferredPath = IsPhraseSource(sourceKind) ? InferFileWritePath(sourceText) : string.Empty;
                if (inferredPath.Length > 0)
                {
                    p["path"] = inferredPath;
                    AddParameter(node, pathContract, new JValue(inferredPath), WfAiInterpretationStatus.Inferred, "natural_inference");
                }
                else
                {
                    RemoveProperty(p, "path");
                    AddParameter(node, pathContract, null, WfAiInterpretationStatus.Missing,
                        pathPlaceholder ? "placeholder_rejected" : "not_supplied");
                    node.Errors.Add("file.write: indicá la ruta del archivo a escribir.");
                }
            }
            else
            {
                p["path"] = path;
                AddParameter(node, pathContract, new JValue(path), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract contentContract = contract.FindParameter("content");
            WfAiParameterContract originContract = contract.FindParameter("origen");
            JToken contentToken = FindValue(p, "content");
            string content = Text(contentToken);
            bool contentPlaceholder = IsImplicitPlaceholder(contentContract, contentToken, sourceText, sourceKind);
            string origin = Text(FindValue(p, "origen"));

            if (contentPlaceholder)
            {
                content = string.Empty;
                RemoveProperty(p, "content");
            }

            if (content.Length == 0 && origin.Length == 0 && IsPhraseSource(sourceKind))
            {
                string inferredContent = InferFileWriteContentFromPhrase(sourceText, catalog);
                if (inferredContent.Length > 0)
                {
                    content = inferredContent;
                    p["content"] = content;
                    AddParameter(node, contentContract, new JValue(content), WfAiInterpretationStatus.Inferred, "available_data_inference");
                }
            }

            if (content.Length > 0)
            {
                p["content"] = content;
                // content tiene precedencia real en HFileWrite. Si llega un origen redundante de
                // una capa legacy lo retiramos para que el nodo canónico tenga una sola fuente.
                RemoveProperty(p, "origen");
                if (!node.Parameters.Exists(x => string.Equals(x.Name, "content", StringComparison.OrdinalIgnoreCase)))
                    AddParameter(node, contentContract, new JValue(content), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                AddParameter(node, originContract, null, WfAiInterpretationStatus.Resolved, "not_used_content_present");
            }
            else if (origin.Length > 0)
            {
                p["origen"] = origin;
                RemoveProperty(p, "content");
                AddParameter(node, contentContract, null, WfAiInterpretationStatus.Resolved, "not_used_origin_present");
                if (!IsValidStatePath(origin))
                {
                    AddParameter(node, originContract, new JValue(origin), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                    node.Errors.Add("file.write: la variable origen no tiene un formato válido: '" + origin + "'.");
                }
                else
                {
                    AddParameter(node, originContract, new JValue(origin), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                }
            }
            else
            {
                RemoveProperty(p, "content");
                RemoveProperty(p, "origen");
                AddParameter(node, contentContract, null, WfAiInterpretationStatus.Missing,
                    contentPlaceholder ? "placeholder_rejected" : "not_supplied");
                AddParameter(node, originContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                node.Errors.Add("file.write: indicá qué contenido o dato querés escribir.");
            }

            ResolveFileWriteSafeString(p, contract.FindParameter("encoding"), "utf-8", sourceKind, node, false);
            ResolveFileWriteSafeBoolean(p, contract.FindParameter("overwrite"), true, sourceKind, node);
            ResolveFileWriteSafeString(p, contract.FindParameter("zipMode"), "none", sourceKind, node, true);

            // El runtime acepta entryName o zipEntryName. El contrato común usa entryName.
            string entryName = Text(FindValue(p, "entryName"));
            if (entryName.Length == 0) entryName = Text(FindValue(p, "zipEntryName"));
            RemoveProperty(p, "zipEntryName");
            WfAiParameterContract entryContract = contract.FindParameter("entryName");
            if (entryName.Length > 0)
            {
                p["entryName"] = entryName;
                AddParameter(node, entryContract, new JValue(entryName), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "entryName");
                AddParameter(node, entryContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }
        }

        private static void ResolveFileWriteSafeString(
            JObject p,
            WfAiParameterContract contract,
            string fallback,
            string sourceKind,
            WfAiResolvedNode node,
            bool enforceOptions)
        {
            if (contract == null) return;
            string value = Text(FindValue(p, contract.Name));
            if (value.Length == 0)
            {
                value = Text(contract.DefaultValue);
                if (value.Length == 0) value = fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            if (string.Equals(contract.Name, "zipMode", StringComparison.OrdinalIgnoreCase))
                value = value.Trim().ToLowerInvariant();
            p[contract.Name] = value;

            if (enforceOptions && !ContractOptionAllowed(contract, value))
            {
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.write: " + contract.Label + " no permitido '" + value + "'.");
                return;
            }

            bool sameAsDefault = contract.DefaultValue != null
                && string.Equals(value, Text(contract.DefaultValue), StringComparison.OrdinalIgnoreCase);
            AddParameter(node, contract, new JValue(value), sameAsDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                sameAsDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static void ResolveFileWriteSafeBoolean(
            JObject p,
            WfAiParameterContract contract,
            bool fallback,
            string sourceKind,
            WfAiResolvedNode node)
        {
            if (contract == null) return;
            JToken token = FindValue(p, contract.Name);
            bool value;
            if (token == null || token.Type == JTokenType.Null || Text(token).Length == 0)
            {
                value = contract.DefaultValue != null ? contract.DefaultValue.Value<bool>() : fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            if (!TryBool(token, out value))
            {
                AddParameter(node, contract, token.DeepClone(), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("file.write: " + contract.Label + " debe ser Sí o No.");
                return;
            }

            p[contract.Name] = value;
            bool defaultValue = contract.DefaultValue != null && contract.DefaultValue.Type == JTokenType.Boolean
                ? contract.DefaultValue.Value<bool>()
                : fallback;
            AddParameter(node, contract, new JValue(value), value == defaultValue ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                value == defaultValue ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static string InferFileWritePath(string sourceText)
        {
            Match match = Regex.Match(sourceText ?? string.Empty,
                @"(?<path>[A-Za-z]:\\[^\r\n,;]+?\.[A-Za-z0-9]{1,8}|/[^\r\n,;]+?\.[A-Za-z0-9]{1,8})",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? (match.Groups["path"].Value ?? string.Empty).Trim() : string.Empty;
        }

        private static string InferFileWriteContentFromPhrase(string sourceText, WfAiCatalog catalog)
        {
            string text = (sourceText ?? string.Empty).Trim();
            if (text.Length == 0) return string.Empty;

            Match match = Regex.Match(text,
                @"\b(?:escribir|guardar)\s+(?<what>.+?)\s+en\s+(?:(?:el|un)\s+archivo\s+)?(?:[A-Za-z]:\\|/)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return string.Empty;

            string what = Regex.Replace(match.Groups["what"].Value ?? string.Empty, @"\s+", " ").Trim();
            string whatKey = WfAiExplicitAssignmentParser.NormalizeHumanKey(what);
            if (whatKey.Length == 0 || whatKey == "archivo" || whatKey == "unarchivo" || whatKey == "elarchivo")
                return string.Empty;

            Match direct = Regex.Match(what, @"^\$\{(?<path>[^}]+)\}$");
            if (direct.Success) return "${" + direct.Groups["path"].Value.Trim() + "}";

            if (catalog == null || catalog.Fields == null) return string.Empty;
            WfAiFieldInfo best = null;
            int bestScore = 0;
            foreach (WfAiFieldInfo field in catalog.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Path) || string.IsNullOrWhiteSpace(field.Label)) continue;
                string labelKey = WfAiExplicitAssignmentParser.NormalizeHumanKey(field.Label);
                string pathKey = WfAiExplicitAssignmentParser.NormalizeHumanKey(field.Path);
                int score = 0;
                if (whatKey == labelKey || whatKey == pathKey) score = 1000 + labelKey.Length;
                else if (whatKey.EndsWith(labelKey, StringComparison.OrdinalIgnoreCase)) score = 700 + labelKey.Length;
                else if (labelKey.EndsWith(whatKey, StringComparison.OrdinalIgnoreCase) && whatKey.Length >= 5) score = 500 + whatKey.Length;
                if (score > bestScore)
                {
                    best = field;
                    bestScore = score;
                }
            }

            return best == null ? string.Empty : "${" + best.Path.Trim() + "}";
        }

        private static bool IsValidStatePath(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0 || text.IndexOf("${", StringComparison.Ordinal) >= 0) return false;
            return Regex.IsMatch(text, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");
        }

        // FIX84C2D4: state.vars converge en el mismo contrato para Frase y Paso a paso.
        // set/remove son datos declarativos. No inventamos claves ni valores.
        private static void ResolveStateVars(
            JObject p,
            WfAiNodeConstructionContract contract,
            string sourceText,
            string sourceKind,
            WfAiResolvedNode node)
        {
            WfAiParameterContract setContract = contract.FindParameter("set");
            WfAiParameterContract removeContract = contract.FindParameter("remove");

            JToken rawSet = FindValue(p, "set");
            JObject normalizedSet = null;
            bool setWasSupplied = rawSet != null && rawSet.Type != JTokenType.Null && HasMeaningfulToken(rawSet);
            if (setWasSupplied)
            {
                if (rawSet is JObject)
                {
                    normalizedSet = new JObject();
                    foreach (JProperty prop in ((JObject)rawSet).Properties())
                    {
                        string key = (prop.Name ?? string.Empty).Trim();
                        if (!IsValidStatePath(key))
                        {
                            node.Errors.Add("state.vars: la variable destino no tiene formato válido: '" + key + "'.");
                            continue;
                        }
                        normalizedSet[key] = prop.Value == null ? JValue.CreateNull() : prop.Value.DeepClone();
                        AddUnique(node.OutputFields, key);
                    }
                }
                else if (rawSet.Type == JTokenType.String)
                {
                    try
                    {
                        JObject parsed = JObject.Parse(Text(rawSet));
                        normalizedSet = new JObject();
                        foreach (JProperty prop in parsed.Properties())
                        {
                            string key = (prop.Name ?? string.Empty).Trim();
                            if (!IsValidStatePath(key))
                            {
                                node.Errors.Add("state.vars: la variable destino no tiene formato válido: '" + key + "'.");
                                continue;
                            }
                            normalizedSet[key] = prop.Value == null ? JValue.CreateNull() : prop.Value.DeepClone();
                            AddUnique(node.OutputFields, key);
                        }
                    }
                    catch
                    {
                        node.Errors.Add("state.vars: set debe ser un objeto de variables válido.");
                    }
                }
                else
                {
                    node.Errors.Add("state.vars: set debe ser un objeto de variables válido.");
                }
            }

            if (normalizedSet != null && normalizedSet.HasValues)
            {
                p["set"] = normalizedSet;
                AddParameter(node, setContract, normalizedSet.DeepClone(),
                    node.Errors.Count == 0 ? WfAiInterpretationStatus.Resolved : WfAiInterpretationStatus.Unrecognized,
                    SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "set");
                AddParameter(node, setContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }

            JToken rawRemove = FindValue(p, "remove");
            var normalizedRemove = new JArray();
            var seenRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool removeWasSupplied = rawRemove != null && rawRemove.Type != JTokenType.Null && HasMeaningfulToken(rawRemove);
            if (removeWasSupplied)
            {
                var values = new List<string>();
                if (rawRemove is JArray)
                {
                    foreach (JToken token in (JArray)rawRemove)
                        values.Add(Text(token));
                }
                else
                {
                    foreach (string part in Text(rawRemove).Split(','))
                        values.Add((part ?? string.Empty).Trim());
                }

                foreach (string raw in values)
                {
                    string key = (raw ?? string.Empty).Trim();
                    if (key.Length == 0) continue;
                    if (!IsValidStatePath(key))
                    {
                        node.Errors.Add("state.vars: la variable a quitar no tiene formato válido: '" + key + "'.");
                        continue;
                    }
                    if (seenRemove.Add(key)) normalizedRemove.Add(key);
                }
            }

            if (normalizedRemove.Count > 0)
            {
                p["remove"] = normalizedRemove;
                AddParameter(node, removeContract, normalizedRemove.DeepClone(),
                    node.Errors.Count == 0 ? WfAiInterpretationStatus.Resolved : WfAiInterpretationStatus.Unrecognized,
                    SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "remove");
                AddParameter(node, removeContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }

            if ((normalizedSet == null || !normalizedSet.HasValues) && normalizedRemove.Count == 0)
                node.Errors.Add("state.vars: indicá al menos una variable para guardar o quitar.");
        }

        private static bool HasMeaningfulToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token is JObject) return ((JObject)token).HasValues;
            if (token is JArray) return ((JArray)token).Count > 0;
            return Text(token).Length > 0;
        }

        // FIX84C2D1: util.notify converge en el mismo contrato para Frase y Paso a paso.
        // El Constructor sólo completa defaults técnicos seguros; destinatario y mensaje son decisiones de negocio.
        private static void ResolveNotify(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node, WfAiCatalog catalog)
        {
            ResolveNotifySafeOption(p, contract.FindParameter("tipo"), "sistema", sourceKind, node);
            ResolveNotifySafeOption(p, contract.FindParameter("canal"), "sistema", sourceKind, node);
            ResolveNotifySafeOption(p, contract.FindParameter("nivel"), "info", sourceKind, node);
            ResolveNotifySafeOption(p, contract.FindParameter("prioridad"), "normal", sourceKind, node);

            WfAiParameterContract roleContract = contract.FindParameter("rolDestino");
            WfAiParameterContract userContract = contract.FindParameter("usuarioDestino");
            WfAiParameterContract typeContract = contract.FindParameter("destinoTipo");
            WfAiParameterContract destinationContract = contract.FindParameter("destino");

            string role = Text(FindValue(p, "rolDestino"));
            string user = Text(FindValue(p, "usuarioDestino"));
            string destination = Text(FindValue(p, "destino"));
            string destinationType = Text(FindValue(p, "destinoTipo")).ToLowerInvariant();

            // Compatibilidad conservadora con candidatos legacy: destino + destinoTipo se traduce
            // a la representación canónica, pero no inventamos destinatarios cuando no existe ninguno.
            if (role.Length == 0 && user.Length == 0 && destination.Length > 0)
            {
                if (destinationType == "usuario") user = destination;
                else if (destinationType == "rol") role = destination;
                else if (destination.Contains("\\") || destination.Contains("@")) user = destination;
                else role = destination;
            }

            // La frase "al usuario USUARIO1" podía llegar del provider legacy como rolDestino=USUARIO1
            // porque el texto corto no contiene dominio. La palabra usuario es una señal explícita;
            // resolvemos contra WF_User real y nunca fabricamos el dominio.
            if (IsPhraseSource(sourceKind) && user.Length == 0 && role.Length > 0 && PhraseExplicitlyTargetsUser(sourceText))
            {
                WfAiUserReferenceResolution explicitUser = WfAiUserReferenceResolver.Resolve(catalog, role);
                if (explicitUser.IsResolved)
                {
                    user = explicitUser.UserKey;
                    role = string.Empty;
                }
                else
                {
                    node.Warnings.Add("util.notify: la referencia de usuario '" + role + "' no pudo resolverse de forma única; elegí un usuario real del catálogo.");
                    role = string.Empty;
                }
            }

            if (user.Length > 0 && catalog != null)
            {
                WfAiUserReferenceResolution userResolution = WfAiUserReferenceResolver.Resolve(catalog, user);
                if (userResolution.IsResolved)
                {
                    user = userResolution.UserKey;
                }
                else
                {
                    string originalUser = user;
                    user = string.Empty;
                    node.Warnings.Add(string.Equals(userResolution.Status, WfAiUserReferenceStatus.Ambiguous, StringComparison.OrdinalIgnoreCase)
                        ? "util.notify: el usuario '" + originalUser + "' coincide con más de un usuario activo; elegí el destinatario exacto."
                        : "util.notify: no encontré un usuario activo que coincida con '" + originalUser + "'; elegí un destinatario del catálogo.");
                }
            }

            if (role.Length > 0 && user.Length > 0)
            {
                p["rolDestino"] = role;
                p["usuarioDestino"] = user;
                RemoveProperty(p, "destinoTipo");
                RemoveProperty(p, "destino");
                AddParameter(node, roleContract, new JValue(role), WfAiInterpretationStatus.Ambiguous, SourceForExplicitValue(sourceKind));
                AddParameter(node, userContract, new JValue(user), WfAiInterpretationStatus.Ambiguous, SourceForExplicitValue(sourceKind));
                AddParameter(node, typeContract, null, WfAiInterpretationStatus.Ambiguous, "conflicting_destination");
                AddParameter(node, destinationContract, null, WfAiInterpretationStatus.Ambiguous, "conflicting_destination");
                node.Errors.Add("util.notify: se indicó rol y usuario al mismo tiempo; elegí un único destino.");
            }
            else if (role.Length > 0)
            {
                p["rolDestino"] = role;
                RemoveProperty(p, "usuarioDestino");
                p["destinoTipo"] = "rol";
                p["destino"] = role;
                AddParameter(node, roleContract, new JValue(role), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                AddParameter(node, userContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
                AddParameter(node, typeContract, new JValue("rol"), WfAiInterpretationStatus.Inferred, "derived_from:rolDestino");
                AddParameter(node, destinationContract, new JValue(role), WfAiInterpretationStatus.Inferred, "derived_from:rolDestino");
            }
            else if (user.Length > 0)
            {
                p["usuarioDestino"] = user;
                RemoveProperty(p, "rolDestino");
                p["destinoTipo"] = "usuario";
                p["destino"] = user;
                AddParameter(node, roleContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
                AddParameter(node, userContract, new JValue(user), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                AddParameter(node, typeContract, new JValue("usuario"), WfAiInterpretationStatus.Inferred, "derived_from:usuarioDestino");
                AddParameter(node, destinationContract, new JValue(user), WfAiInterpretationStatus.Inferred, "derived_from:usuarioDestino");
            }
            else
            {
                RemoveProperty(p, "rolDestino");
                RemoveProperty(p, "usuarioDestino");
                RemoveProperty(p, "destinoTipo");
                RemoveProperty(p, "destino");
                AddParameter(node, roleContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                AddParameter(node, userContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                AddParameter(node, typeContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                AddParameter(node, destinationContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                node.Errors.Add("util.notify: indicá un rol o un usuario destino.");
            }

            WfAiParameterContract subjectContract = contract.FindParameter("asunto");
            JToken subjectToken = FindValue(p, "asunto");
            string subject = Text(subjectToken);
            bool subjectPlaceholder = IsImplicitPlaceholder(subjectContract, subjectToken, sourceText, sourceKind);
            if (subject.Length == 0 || subjectPlaceholder)
            {
                subject = Text(subjectContract == null ? null : subjectContract.DefaultValue);
                if (subject.Length == 0) subject = "Notificación";
                p["asunto"] = subject;
                AddParameter(node, subjectContract, new JValue(subject), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else
            {
                p["asunto"] = subject;
                AddParameter(node, subjectContract, new JValue(subject), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract messageContract = contract.FindParameter("mensaje");
            JToken messageToken = FindValue(p, "mensaje");
            string message = Text(messageToken);
            bool messagePlaceholder = IsImplicitPlaceholder(messageContract, messageToken, sourceText, sourceKind);
            if (message.Length == 0 || messagePlaceholder)
            {
                string inferred = IsPhraseSource(sourceKind) ? InferNotifyMessage(sourceText) : string.Empty;
                if (inferred.Length > 0)
                {
                    p["mensaje"] = inferred;
                    AddParameter(node, messageContract, new JValue(inferred), WfAiInterpretationStatus.Inferred, "natural_inference");
                }
                else
                {
                    RemoveProperty(p, "mensaje");
                    AddParameter(node, messageContract, null, WfAiInterpretationStatus.Missing, messagePlaceholder ? "placeholder_rejected" : "not_supplied");
                    node.Errors.Add("util.notify: indicá qué mensaje querés enviar.");
                }
            }
            else
            {
                p["mensaje"] = message;
                AddParameter(node, messageContract, new JValue(message), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            WfAiParameterContract urlContract = contract.FindParameter("urlAccion");
            string url = Text(FindValue(p, "urlAccion"));
            if (url.Length > 0)
            {
                p["urlAccion"] = url;
                AddParameter(node, urlContract, new JValue(url), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
            else
            {
                RemoveProperty(p, "urlAccion");
                AddParameter(node, urlContract, null, WfAiInterpretationStatus.Resolved, "runtime_current_instance");
            }
        }

        private static void ResolveNotifySafeOption(JObject p, WfAiParameterContract contract, string fallback, string sourceKind, WfAiResolvedNode node)
        {
            if (contract == null) return;
            string value = Text(FindValue(p, contract.Name));
            if (value.Length == 0)
            {
                value = Text(contract.DefaultValue);
                if (value.Length == 0) value = fallback;
                p[contract.Name] = value;
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Inferred, "safe_default");
                return;
            }

            value = value.Trim().ToLowerInvariant();
            p[contract.Name] = value;
            if (!ContractOptionAllowed(contract, value))
            {
                AddParameter(node, contract, new JValue(value), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("util.notify: " + contract.Label + " no permitido '" + value + "'.");
                return;
            }

            bool sameAsDefault = contract.DefaultValue != null
                && string.Equals(value, Text(contract.DefaultValue), StringComparison.OrdinalIgnoreCase);
            AddParameter(node, contract, new JValue(value), sameAsDefault ? WfAiInterpretationStatus.Inferred : WfAiInterpretationStatus.Resolved,
                sameAsDefault ? "safe_default" : SourceForExplicitValue(sourceKind));
        }

        private static bool PhraseExplicitlyTargetsUser(string sourceText)
        {
            return Regex.IsMatch(sourceText ?? string.Empty,
                @"\b(?:notificar|avisar)\b.*?\b(?:al|a|para)\s+(?:el\s+|la\s+)?usuario\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string InferNotifyMessage(string sourceText)
        {
            string text = Regex.Replace(sourceText ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length == 0) return string.Empty;

            Match match = Regex.Match(text,
                @"\b(?:notificar|avisar)\b.*?(?:\bmensaje\b\s*(?:=|:)?\s*|\bindicando(?:\s+que)?\s+)(?<msg>.+?)(?=(?:\.\s|;\s|,\s*)?(?:luego|despues|después|finalmente|finalizar|terminar|si\s|caso\s+contrario)\b|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return string.Empty;

            string message = Regex.Replace(match.Groups["msg"].Value ?? string.Empty, @"\s+", " ").Trim();
            while (message.EndsWith(".", StringComparison.Ordinal) || message.EndsWith(",", StringComparison.Ordinal) || message.EndsWith(";", StringComparison.Ordinal))
                message = message.Substring(0, message.Length - 1).Trim();
            return message;
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

        // FIX84C2C1: control.if entra al mismo camino contractual que Logger/Queue/Human Task.
        // La capa común NO decide las ramas SI/NO: solamente normaliza qué condición evalúa el nodo.
        // HIf conserva la ejecución; acá se hace canónica la representación para Frase y Paso a paso.
        private static void ResolveControlIf(JObject p, WfAiNodeConstructionContract contract, string sourceText, string sourceKind, WfAiResolvedNode node)
        {
            JToken rulesToken = FindValue(p, "rules");
            string expression = Text(FindValue(p, "expression"));
            string field = NormalizeIfField(Text(FindValue(p, "field")));
            string rawOp = Text(FindValue(p, "op"));
            JToken valueToken = FindValue(p, "value");

            bool rulesDeclared = rulesToken != null && rulesToken.Type != JTokenType.Null && rulesToken.Type != JTokenType.Undefined;
            bool simpleDeclared = field.Length > 0 || rawOp.Length > 0;
            bool expressionDeclared = expression.Length > 0;

            // El runtime ya tiene precedencia rules > simple > expression. La capa común conserva
            // esa semántica pero elimina parámetros de modos alternativos para que quede un solo contrato.
            if (rulesDeclared)
            {
                ResolveCompoundIf(p, contract, rulesToken, sourceKind, node);
                return;
            }

            if (simpleDeclared)
            {
                ResolveSimpleIf(p, contract, field, rawOp, valueToken, sourceKind, node);
                return;
            }

            if (expressionDeclared)
            {
                RemoveProperty(p, "field");
                RemoveProperty(p, "op");
                RemoveProperty(p, "value");
                RemoveProperty(p, "transform");
                RemoveProperty(p, "rules");
                RemoveProperty(p, "rulesMode");
                p["expression"] = expression;
                AddParameter(node, contract.FindParameter("expression"), new JValue(expression), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                return;
            }

            RemoveProperty(p, "field");
            RemoveProperty(p, "op");
            RemoveProperty(p, "value");
            RemoveProperty(p, "expression");
            RemoveProperty(p, "transform");
            RemoveProperty(p, "rules");
            RemoveProperty(p, "rulesMode");
            AddParameter(node, contract.FindParameter("field"), null, WfAiInterpretationStatus.Missing, "not_supplied");
            AddParameter(node, contract.FindParameter("op"), null, WfAiInterpretationStatus.Missing, "not_supplied");
            node.Errors.Add("control.if: indicá una condición simple, varias reglas o una expresión.");
        }

        private static void ResolveSimpleIf(
            JObject p,
            WfAiNodeConstructionContract contract,
            string field,
            string rawOp,
            JToken valueToken,
            string sourceKind,
            WfAiResolvedNode node)
        {
            RemoveProperty(p, "rules");
            RemoveProperty(p, "rulesMode");
            RemoveProperty(p, "expression");

            WfAiParameterContract fieldContract = contract.FindParameter("field");
            WfAiParameterContract opContract = contract.FindParameter("op");
            WfAiParameterContract valueContract = contract.FindParameter("value");
            WfAiParameterContract transformContract = contract.FindParameter("transform");

            if (field.Length == 0)
            {
                RemoveProperty(p, "field");
                AddParameter(node, fieldContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                node.Errors.Add("control.if: indicá qué dato querés evaluar.");
            }
            else
            {
                p["field"] = field;
                AddParameter(node, fieldContract, new JValue(field), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            JToken normalizedValue = valueToken == null ? null : valueToken.DeepClone();
            string op = NormalizeIfOperator(rawOp, ref normalizedValue);
            if (op.Length == 0)
            {
                RemoveProperty(p, "op");
                AddParameter(node, opContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                node.Errors.Add("control.if: indicá cómo querés comparar el dato.");
            }
            else if (!AllowedIfOperator(op))
            {
                p["op"] = op;
                AddParameter(node, opContract, new JValue(op), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("control.if: operador no permitido '" + op + "'.");
            }
            else
            {
                p["op"] = op;
                AddParameter(node, opContract, new JValue(op), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }

            if (op.Length > 0 && AllowedIfOperator(op) && IfOperatorNeedsValue(op))
            {
                if (!HasIfValue(normalizedValue))
                {
                    RemoveProperty(p, "value");
                    AddParameter(node, valueContract, null, WfAiInterpretationStatus.Missing, "not_supplied");
                    node.Errors.Add("control.if: el operador '" + op + "' necesita un valor de comparación.");
                }
                else
                {
                    JToken scalar = NormalizeIfScalar(normalizedValue);
                    p["value"] = scalar;
                    AddParameter(node, valueContract, scalar, WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
                }
            }
            else
            {
                RemoveProperty(p, "value");
                AddParameter(node, valueContract, null, WfAiInterpretationStatus.Resolved, "not_required");
            }

            string transform = Text(FindValue(p, "transform"));
            if (transform.Length == 0 || string.Equals(transform, "none", StringComparison.OrdinalIgnoreCase))
            {
                RemoveProperty(p, "transform");
                AddParameter(node, transformContract, null, WfAiInterpretationStatus.Resolved, "optional_empty");
            }
            else
            {
                transform = transform.ToLowerInvariant();
                p["transform"] = transform;
                AddParameter(node, transformContract, new JValue(transform), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
        }

        private static void ResolveCompoundIf(
            JObject p,
            WfAiNodeConstructionContract contract,
            JToken rulesToken,
            string sourceKind,
            WfAiResolvedNode node)
        {
            RemoveProperty(p, "field");
            RemoveProperty(p, "op");
            RemoveProperty(p, "value");
            RemoveProperty(p, "expression");
            RemoveProperty(p, "transform");

            WfAiParameterContract rulesContract = contract.FindParameter("rules");
            WfAiParameterContract modeContract = contract.FindParameter("rulesMode");

            JArray sourceRules = null;
            if (rulesToken is JArray)
            {
                sourceRules = (JArray)rulesToken;
            }
            else if (rulesToken != null && rulesToken.Type == JTokenType.String)
            {
                try { sourceRules = JArray.Parse(Text(rulesToken)); }
                catch { }
            }

            if (sourceRules == null)
            {
                RemoveProperty(p, "rules");
                AddParameter(node, rulesContract, null, WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("control.if: rules debe ser una lista de reglas.");
                return;
            }

            if (sourceRules.Count == 0)
            {
                p["rules"] = new JArray();
                AddParameter(node, rulesContract, new JArray(), WfAiInterpretationStatus.Missing, SourceForExplicitValue(sourceKind));
                node.Errors.Add("control.if: la condición compuesta debe tener al menos una regla.");
                return;
            }

            var normalizedRules = new JArray();
            int ruleIndex = 0;
            foreach (JToken token in sourceRules)
            {
                ruleIndex++;
                JObject sourceRule = token as JObject;
                if (sourceRule == null)
                {
                    node.Errors.Add("control.if: la regla " + ruleIndex.ToString(CultureInfo.InvariantCulture) + " no es válida.");
                    continue;
                }

                string field = NormalizeIfField(Text(FindValue(sourceRule, "field")));
                if (field.Length == 0) field = NormalizeIfField(Text(FindValue(sourceRule, "fieldPath")));
                string rawOp = Text(FindValue(sourceRule, "op"));
                if (rawOp.Length == 0) rawOp = Text(FindValue(sourceRule, "operator"));
                JToken value = FindValue(sourceRule, "value");
                value = value == null ? null : value.DeepClone();
                string op = NormalizeIfOperator(rawOp, ref value);

                if (field.Length == 0)
                {
                    node.Errors.Add("control.if: la regla " + ruleIndex.ToString(CultureInfo.InvariantCulture) + " necesita un dato a evaluar.");
                    continue;
                }
                if (op.Length == 0)
                {
                    node.Errors.Add("control.if: la regla " + ruleIndex.ToString(CultureInfo.InvariantCulture) + " necesita un operador.");
                    continue;
                }
                if (!AllowedIfOperator(op))
                {
                    node.Errors.Add("control.if: la regla " + ruleIndex.ToString(CultureInfo.InvariantCulture) + " usa un operador no permitido '" + op + "'.");
                    continue;
                }
                if (IfOperatorNeedsValue(op) && !HasIfValue(value))
                {
                    node.Errors.Add("control.if: la regla " + ruleIndex.ToString(CultureInfo.InvariantCulture) + " necesita un valor de comparación.");
                    continue;
                }

                var normalized = new JObject
                {
                    ["field"] = field,
                    ["op"] = op
                };
                if (IfOperatorNeedsValue(op)) normalized["value"] = NormalizeIfScalar(value);

                string transform = Text(FindValue(sourceRule, "transform"));
                if (transform.Length > 0 && !string.Equals(transform, "none", StringComparison.OrdinalIgnoreCase))
                    normalized["transform"] = transform.ToLowerInvariant();

                normalizedRules.Add(normalized);
            }

            p["rules"] = normalizedRules;
            AddParameter(node, rulesContract, normalizedRules.DeepClone(), node.Errors.Count == 0 ? WfAiInterpretationStatus.Resolved : WfAiInterpretationStatus.Missing, SourceForExplicitValue(sourceKind));

            string rawMode = Text(FindValue(p, "rulesMode"));
            string mode = NormalizeIfRulesMode(rawMode);
            if (rawMode.Length == 0)
            {
                mode = "all";
                p["rulesMode"] = mode;
                AddParameter(node, modeContract, new JValue(mode), WfAiInterpretationStatus.Inferred, "safe_default");
            }
            else if (mode.Length == 0)
            {
                p["rulesMode"] = rawMode;
                AddParameter(node, modeContract, new JValue(rawMode), WfAiInterpretationStatus.Unrecognized, SourceForExplicitValue(sourceKind));
                node.Errors.Add("control.if: rulesMode debe ser all o any.");
            }
            else
            {
                p["rulesMode"] = mode;
                AddParameter(node, modeContract, new JValue(mode), WfAiInterpretationStatus.Resolved, SourceForExplicitValue(sourceKind));
            }
        }

        private static string NormalizeIfField(string field)
        {
            field = (field ?? string.Empty).Trim();
            if (field.StartsWith("${", StringComparison.Ordinal) && field.EndsWith("}", StringComparison.Ordinal) && field.Length > 3)
                field = field.Substring(2, field.Length - 3).Trim();
            return field;
        }

        private static string NormalizeIfOperator(string rawOp, ref JToken value)
        {
            string op = (rawOp ?? string.Empty).Trim().ToLowerInvariant();
            if (op == "=" || op == "eq") return "==";
            if (op == "neq") return "!=";
            if (op == "true")
            {
                value = new JValue("true");
                return "==";
            }
            if (op == "false")
            {
                value = new JValue("false");
                return "==";
            }
            return op;
        }

        private static bool AllowedIfOperator(string op)
        {
            switch ((op ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "==":
                case "!=":
                case ">":
                case ">=":
                case "<":
                case "<=":
                case "contains":
                case "not_contains":
                case "starts_with":
                case "ends_with":
                case "exists":
                case "not_exists":
                case "empty":
                case "not_empty":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IfOperatorNeedsValue(string op)
        {
            string normalized = (op ?? string.Empty).Trim().ToLowerInvariant();
            return normalized != "exists" && normalized != "not_exists" && normalized != "empty" && normalized != "not_empty";
        }

        private static string NormalizeIfRulesMode(string raw)
        {
            string mode = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (mode.Length == 0) return string.Empty;
            if (mode == "all" || mode == "and" || mode == "y" || mode == "todas" || mode == "todos") return "all";
            if (mode == "any" || mode == "or" || mode == "o" || mode == "cualquiera") return "any";
            return string.Empty;
        }

        private static bool HasIfValue(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return false;
            if (token.Type == JTokenType.String) return !string.IsNullOrWhiteSpace(token.Value<string>());
            return true;
        }

        private static JToken NormalizeIfScalar(JToken token)
        {
            if (token == null) return JValue.CreateNull();
            if (token.Type == JTokenType.String) return new JValue((token.Value<string>() ?? string.Empty).Trim());
            return token.DeepClone();
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

            // FIX84C2Bg2: las asignaciones explícitas pertenecen al nodo human.task, no a
            // las oraciones posteriores que describen sus resultados. Ejemplo:
            // "Título = Revisar factura. Si Compras la rechaza, ..." debe resolver el título
            // como "Revisar factura" y conservar la cláusula "Si Compras..." para el flujo.
            Match resultClause = Regex.Match(tail,
                @"[.;]\s*(?=(?:si|cuando)\b)",
                RegexOptions.IgnoreCase);
            if (resultClause.Success) tail = tail.Substring(0, resultClause.Index);

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
