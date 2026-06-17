using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.ML;
using Microsoft.ML.Data;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Proveedor local del Asistente IA basado en ML.NET.
    /// Workflow Studio NO entrena el modelo: solo carga el modelo .zip generado por Tools/WorkflowStudio.AITrainer.
    /// No genera código, no toca SQL y no aplica cambios al canvas.
    /// </summary>
    public class WfAiMlnetProvider
    {
        private static readonly object Sync = new object();
        private static PredictionEngine<WfAiIntentInput, WfAiIntentOutput> _engine;
        private static ITransformer _model;
        private static DateTime _modelLoadedAtUtc;
        private static string _loadedModelPath;

        public WfAiLocalModelResult Interpret(string userText, WfAiCatalog catalog, string workflowJson)
        {
            var result = new WfAiLocalModelResult
            {
                Provider = "mlnet",
                Model = "ML.NET modelo externo"
            };

            try
            {
                EnsureModelLoaded();

                var predictions = PredictSegments(userText);
                JObject plan = BuildPlan(userText, predictions, catalog);

                result.Ok = plan != null;
                result.Plan = plan;
                result.RawText = plan == null ? "" : plan.ToString();
                if (!result.Ok)
                    result.ErrorMessage = "ML.NET no pudo construir una propuesta válida.";
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static void EnsureModelLoaded()
        {
            string modelPath = MapConfiguredPath("WF_AI_MLNET_MODEL_PATH", "App_Data/WF_AI/workflow_intent_model.zip");

            lock (Sync)
            {
                if (_engine != null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException(
                        "No se encontró el modelo entrenado del Asistente IA. Ejecute Tools\\WorkflowStudio.AITrainer, genere workflow_intent_model.zip y copie el archivo en App_Data\\WF_AI.",
                        modelPath);
                }

                var ml = new MLContext(seed: 1);
                DataViewSchema schema;
                _model = ml.Model.Load(modelPath, out schema);

                _engine = ml.Model.CreatePredictionEngine<WfAiIntentInput, WfAiIntentOutput>(_model);
                _loadedModelPath = modelPath;
                _modelLoadedAtUtc = DateTime.UtcNow;
            }
        }

        private static List<WfAiPredictedIntent> PredictSegments(string userText)
        {
            EnsureModelLoaded();

            var list = new List<WfAiPredictedIntent>();
            foreach (string segment in SplitSegments(userText))
            {
                if (string.IsNullOrWhiteSpace(segment)) continue;

                WfAiIntentOutput pred;
                lock (Sync)
                {
                    pred = _engine.Predict(new WfAiIntentInput { Texto = segment });
                }

                list.Add(new WfAiPredictedIntent
                {
                    Text = segment,
                    Intent = pred.PredictedLabel ?? "",
                    Score = MaxScore(pred.Score)
                });
            }

            if (list.Count == 0)
            {
                WfAiIntentOutput pred;
                lock (Sync)
                {
                    pred = _engine.Predict(new WfAiIntentInput { Texto = userText ?? "" });
                }

                list.Add(new WfAiPredictedIntent
                {
                    Text = userText ?? "",
                    Intent = pred.PredictedLabel ?? "",
                    Score = MaxScore(pred.Score)
                });
            }

            return list;
        }

        private static JObject BuildPlan(string userText, List<WfAiPredictedIntent> predictions, WfAiCatalog catalog)
        {
            string norm = Normalize(userText);
            string docTipo = ResolveDocTipo(norm, catalog);
            string prefix = ResolvePrefix(docTipo, catalog);
            string role = ResolveRole(norm, catalog);
            string userKey = ResolveUser(norm, catalog);
            string amount = ExtractAmount(norm);
            List<EmailRequest> emailRequests = AnalyzeEmailRequests(userText, norm, catalog);
            EmailRequest emailRequest = emailRequests.Count > 0 ? emailRequests[0] : new EmailRequest();
            string preBranchRole = ExtractPreBranchHumanTaskRole(norm, catalog);
            if (!string.IsNullOrWhiteSpace(preBranchRole))
            {
                role = preBranchRole;
                userKey = "";
            }
            else if (emailRequest.WantsEmail && UserMentionAppearsOnlyInEmailContext(norm, userKey))
            {
                userKey = "";
            }

            bool wantsCaeValidation = ContainsToken(norm, "cae") || ContainsToken(norm, "cai");
            bool wantsDocument = docTipo.Length > 0 || HasIntent(predictions, "CARGAR_DOCUMENTO") || ContainsAny(norm, "cargar", "subir", "leer", "documento", "factura", "nota credito", " nc ");
            bool wantsHumanTask = role.Length > 0 || userKey.Length > 0 || HasIntent(predictions, "CREAR_TAREA_ROL") || HasIntent(predictions, "CONDICION_Y_TAREA") || ContainsAny(norm, "enviar a", "mandar a", "derivar a", "pasar a", "aprobar", "revision");
            if (emailRequest.WantsEmail && !HasExplicitHumanTaskSignal(norm))
                wantsHumanTask = false;
            bool wantsLogger = HasIntent(predictions, "REGISTRAR_LOG") || ContainsAny(norm, "log", "registrar", "dejar constancia");
            bool wantsEnd = HasIntent(predictions, "FINALIZAR_FLUJO") || ContainsAny(norm, "finalizar", "terminar", "fin del flujo");

            BranchAnalysis branches = AnalyzeBranches(norm, catalog, amount);
            bool branchTasksCreated = false;

            var actions = new JArray();
            var missing = new JArray();
            var warnings = new JArray();
            string unknownRoleMention = DetectUnknownRoleMention(norm, catalog);

            if (!string.IsNullOrWhiteSpace(unknownRoleMention))
            {
                missing.Add(new JObject
                {
                    ["key"] = "rolNoEncontrado",
                    ["question"] = "No encontré el rol o sector \"" + unknownRoleMention + "\" en el catálogo real. Seleccioná un rol válido."
                });
            }

            if (wantsDocument)
            {
                var p = new JObject();
                if (docTipo.Length > 0) p["docTipoCodigo"] = docTipo;
                p["path"] = "${input.filePath}";
                p["mode"] = "auto";

                actions.Add(AddNode("doc.load", DocLoadLabel(docTipo), p));

                if (docTipo.Length == 0)
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "docTipoCodigo",
                        ["question"] = "¿Qué tipo de documento querés cargar?"
                    });
                }
            }

            if (emailRequests.Count > 0)
                AddEmailRequestAction(actions, missing, emailRequests[0], emailRequests.Count > 1 ? 1 : 0);

            bool preBranchTaskCreated = false;
            if (!string.IsNullOrWhiteSpace(preBranchRole))
            {
                actions.Add(AddNode("human.task", HumanTaskTitle(preBranchRole, ""), HumanTaskParams(preBranchRole, HumanTaskTitle(preBranchRole, ""), "Revisión previa generada por el Asistente IA.")));
                preBranchTaskCreated = true;
            }

            if (wantsCaeValidation)
            {
                string field = prefix.Length > 0 ? "biz." + prefix + ".cae" : "";
                var p = new JObject();
                if (field.Length > 0) p["field"] = field;
                p["op"] = "not_empty";

                actions.Add(AddNode("control.if", "Validar CAE informado", p));

                if (field.Length == 0)
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "campoCae",
                        ["question"] = "No pude determinar el campo CAE porque falta confirmar el DocTipo."
                    });
                }

                if (!string.IsNullOrWhiteSpace(branches.CaeFalseRole))
                {
                    EnsureHumanTaskAction(actions, branches.CaeFalseRole, "Corregir en " + RoleFriendlyName(branches.CaeFalseRole), "Rama negativa de CAE generada por el Asistente IA.");
                    branchTasksCreated = true;
                }
                else
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "caeFalsePath",
                        ["question"] = "¿A qué rol o usuario querés enviar la tarea cuando falte el CAE?"
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(amount))
            {
                string field = prefix.Length > 0 ? "biz." + prefix + ".total" : "";
                var p = new JObject();
                if (field.Length > 0) p["field"] = field;
                p["op"] = ">";
                p["value"] = amount;

                actions.Add(AddNode("control.if", "Total mayor a " + amount, p));

                if (field.Length == 0)
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "campoImporteTotal",
                        ["question"] = "No pude determinar el campo total porque falta confirmar el DocTipo."
                    });
                }

                string trueRole = branches.TotalTrueRole;
                string falseRole = branches.TotalFalseRole;

                if (string.IsNullOrWhiteSpace(trueRole) && !string.IsNullOrWhiteSpace(role) && HasIntent(predictions, "CONDICION_Y_TAREA"))
                    trueRole = role;

                if (!string.IsNullOrWhiteSpace(trueRole))
                {
                    EnsureHumanTaskAction(actions, trueRole, HumanTaskTitle(trueRole, ""), "Rama positiva de importe generada por el Asistente IA.");
                    branchTasksCreated = true;
                }
                else
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "onTruePath",
                        ["question"] = "¿Qué querés hacer si el total supera " + amount + "?"
                    });
                }

                if (!string.IsNullOrWhiteSpace(falseRole))
                {
                    EnsureHumanTaskAction(actions, falseRole, HumanTaskTitle(falseRole, ""), "Rama negativa de importe generada por el Asistente IA.");
                    branchTasksCreated = true;
                }
                else
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "onFalsePath",
                        ["question"] = "¿Qué querés hacer si el total NO supera " + amount + "?"
                    });
                }
            }

            for (int i = 1; i < emailRequests.Count; i++)
                AddEmailRequestAction(actions, missing, emailRequests[i], i + 1);

            if (wantsHumanTask && !branchTasksCreated && !preBranchTaskCreated)
            {
                var p = new JObject
                {
                    ["titulo"] = HumanTaskTitle(role, userKey),
                    ["descripcion"] = "Revisión generada por el Asistente IA."
                };
                if (userKey.Length > 0) p["usuarioAsignado"] = userKey;
                else if (role.Length > 0) p["rol"] = role;

                actions.Add(AddNode("human.task", HumanTaskTitle(role, userKey), p));

                if (role.Length == 0 && userKey.Length == 0)
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "rolUsuario",
                        ["question"] = "¿A qué rol o usuario querés enviar la tarea?"
                    });
                }
            }

            if (wantsLogger)
            {
                actions.Add(AddNode("util.logger", "Registrar evento", new JObject
                {
                    ["level"] = "Info",
                    ["message"] = "Paso agregado por Asistente IA"
                }));
            }

            if (wantsEnd)
            {
                actions.Add(AddNode("util.end", "Fin", new JObject()));
            }

            if (actions.Count == 0)
            {
                actions.Add(new JObject
                {
                    ["action"] = "ASK_USER",
                    ["nodeType"] = null,
                    ["label"] = null,
                    ["params"] = new JObject()
                });

                missing.Add(new JObject
                {
                    ["key"] = "intencion",
                    ["question"] = "No pude determinar qué nodo querés agregar. Probá indicando documento, validación, tarea, correo o fin."
                });
            }

            EnsureWorkflowBoundaries(actions);

            JObject branchPlan = BuildBranchPlan(wantsCaeValidation, amount, branches);
            JArray proposedConnections = BuildProposedConnections(actions, branchPlan);

            warnings.Add("Proveedor ML.NET: interpretación local usando modelo entrenado externo. Todavía no se aplica al canvas automáticamente.");
            if (branches.HasBranchInfo)
                warnings.Add("Branch Connector v1: se generaron conexiones propuestas para revisión. Todavía no se aplican al canvas automáticamente.");
            if (emailRequest.WantsEmail)
                warnings.Add("email.send: se genera nodo de correo, no tarea humana. El envío real depende de la configuración SMTP/Web.config.");

            return new JObject
            {
                ["assistantVersion"] = "1.9-mlnet-email-mixed-flow-fix11",
                ["intent"] = "build_workflow",
                ["confidence"] = AggregateConfidence(predictions),
                ["messageToUser"] = BuildMessage(actions, docTipo, role, userKey, amount, branches),
                ["actions"] = actions,
                ["missingData"] = missing,
                ["warnings"] = warnings,
                ["branchPlan"] = branchPlan,
                ["proposedConnections"] = proposedConnections,
                ["mlnet"] = new JObject
                {
                    ["modelLoadedAtUtc"] = _modelLoadedAtUtc.ToString("s") + "Z",
                    ["predictions"] = JArray.FromObject(predictions),
                    ["resolved"] = new JObject
                    {
                        ["docTipoCodigo"] = docTipo,
                        ["contextPrefix"] = prefix,
                        ["rol"] = role,
                        ["usuarioAsignado"] = userKey,
                        ["importe"] = amount,
                        ["emailRequested"] = emailRequest.WantsEmail,
                        ["emailCount"] = emailRequests.Count,
                        ["emailTo"] = JArray.FromObject(emailRequest.To),
                        ["emailSubject"] = emailRequest.Subject,
                        ["caeFalseRole"] = branches.CaeFalseRole,
                        ["totalTrueRole"] = branches.TotalTrueRole,
                        ["totalFalseRole"] = branches.TotalFalseRole
                    }
                }
            };
        }

        private static JObject AddNode(string nodeType, string label, JObject parameters)
        {
            return new JObject
            {
                ["action"] = "ADD_NODE",
                ["nodeType"] = nodeType,
                ["label"] = label,
                ["params"] = parameters ?? new JObject()
            };
        }

        private static JObject EnsureHumanTaskAction(JArray actions, string role, string title, string description)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return null;

            JObject existing = FindHumanTaskByRole(actions, role);
            if (existing != null) return existing;

            JObject node = AddNode("human.task", title, HumanTaskParams(role, title, description));
            actions.Add(node);
            return node;
        }

        private static void AddEmailRequestAction(JArray actions, JArray missing, EmailRequest request, int ordinal)
        {
            if (actions == null || request == null || !request.WantsEmail) return;

            string label = ordinal > 1 ? "Enviar correo " + ordinal.ToString(CultureInfo.InvariantCulture) : "Enviar correo";
            actions.Add(AddNode("email.send", label, EmailParams(request)));

            if (request.To.Count == 0 && missing != null)
            {
                missing.Add(new JObject
                {
                    ["key"] = ordinal > 1 ? "email.to." + ordinal.ToString(CultureInfo.InvariantCulture) : "email.to",
                    ["question"] = EmailMissingRecipientQuestion(request)
                });
            }
        }

        private static void EnsureWorkflowBoundaries(JArray actions)
        {
            if (!HasConcreteWorkflowNode(actions)) return;

            if (FirstAction(actions, "util.start") == null)
                actions.Insert(0, AddNode("util.start", "Inicio", new JObject()));

            if (FirstAction(actions, "util.end") == null)
                actions.Add(AddNode("util.end", "Fin", new JObject()));
        }

        private static bool HasConcreteWorkflowNode(JArray actions)
        {
            if (actions == null) return false;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;

                string type = ActionNodeType(a);
                if (!string.Equals(type, "util.start", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "util.end", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static JArray BuildProposedConnections(JArray actions, JObject branchPlan)
        {
            var result = new JArray();
            if (actions == null || actions.Count == 0) return result;

            JObject caeIf = FindActionByLabel(actions, "control.if", "Validar CAE informado");
            JObject totalIf = FindActionLabelStarts(actions, "control.if", "Total mayor a ");
            JObject logger = FirstAction(actions, "util.logger");
            JObject end = FirstAction(actions, "util.end");
            JObject firstCondition = caeIf ?? totalIf;

            if (firstCondition == null)
            {
                AddSequentialConnections(result, actions);
                return result;
            }

            AddSequentialConnectionsUntil(result, actions, firstCondition);

            if (caeIf != null)
            {
                JObject trueTarget = totalIf ?? FirstActionAfter(actions, caeIf) ?? logger ?? end;
                JObject falseTarget = FindActionForPath(actions, GetBranchPath(branchPlan, "cae", "falsePath"));

                AddConnection(result, caeIf, trueTarget, "SI");
                AddConnection(result, caeIf, falseTarget, "NO");
            }

            if (totalIf != null)
            {
                JObject trueTarget = FindActionForPath(actions, GetBranchPath(branchPlan, "total", "truePath"));
                JObject falseTarget = FindActionForPath(actions, GetBranchPath(branchPlan, "total", "falsePath"));

                AddConnection(result, totalIf, trueTarget, "SI");
                AddConnection(result, totalIf, falseTarget, "NO");
            }

            List<JObject> branchTerminals = FindBranchTerminalActions(actions, branchPlan);
            JObject mergeTarget = FirstActionAfterLast(actions, branchTerminals) ?? logger ?? end;
            if (mergeTarget != null)
            {
                foreach (JObject branchTarget in branchTerminals)
                    AddConnection(result, branchTarget, mergeTarget, "");

                AddSequentialConnectionsFrom(result, actions, mergeTarget);
            }

            if (result.Count == 0)
                AddSequentialConnections(result, actions);

            return result;
        }

        private static void AddSequentialConnectionsUntil(JArray result, JArray actions, JObject stopAtInclusive)
        {
            JObject previous = null;
            foreach (JToken token in actions)
            {
                JObject current = token as JObject;
                if (current == null || !IsAddNode(current)) continue;

                if (previous != null)
                    AddConnection(result, previous, current, "");

                if (object.ReferenceEquals(current, stopAtInclusive))
                    return;

                previous = current;
            }
        }

        private static void AddSequentialConnectionsFrom(JArray result, JArray actions, JObject startAt)
        {
            if (actions == null || startAt == null) return;

            bool found = false;
            JObject previous = null;
            foreach (JToken token in actions)
            {
                JObject current = token as JObject;
                if (current == null || !IsAddNode(current)) continue;

                if (!found)
                {
                    if (object.ReferenceEquals(current, startAt))
                    {
                        found = true;
                        previous = current;
                    }
                    continue;
                }

                AddConnection(result, previous, current, "");
                previous = current;
            }
        }

        private static JObject FirstActionAfterLast(JArray actions, List<JObject> markers)
        {
            if (actions == null || markers == null || markers.Count == 0) return null;

            int lastIndex = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                JObject current = actions[i] as JObject;
                if (current == null || !IsAddNode(current)) continue;

                foreach (JObject marker in markers)
                {
                    if (object.ReferenceEquals(current, marker))
                    {
                        if (i > lastIndex) lastIndex = i;
                        break;
                    }
                }
            }

            if (lastIndex < 0) return null;
            for (int i = lastIndex + 1; i < actions.Count; i++)
            {
                JObject current = actions[i] as JObject;
                if (current == null || !IsAddNode(current)) continue;
                return current;
            }

            return null;
        }

        private static void AddSequentialConnections(JArray result, JArray actions)
        {
            JObject previous = null;
            foreach (JToken token in actions)
            {
                JObject current = token as JObject;
                if (current == null) continue;
                if (!IsAddNode(current)) continue;

                if (previous != null)
                    AddConnection(result, previous, current, "");

                previous = current;
            }
        }

        private static List<JObject> FindBranchTerminalActions(JArray actions, JObject branchPlan)
        {
            var list = new List<JObject>();
            AddUniqueAction(list, FindActionForPath(actions, GetBranchPath(branchPlan, "cae", "falsePath")));
            AddUniqueAction(list, FindActionForPath(actions, GetBranchPath(branchPlan, "total", "truePath")));
            AddUniqueAction(list, FindActionForPath(actions, GetBranchPath(branchPlan, "total", "falsePath")));
            return list;
        }

        private static void AddUniqueAction(List<JObject> list, JObject action)
        {
            if (list == null || action == null) return;

            foreach (JObject item in list)
            {
                if (object.ReferenceEquals(item, action)) return;
                if (string.Equals(ActionLabel(item), ActionLabel(action), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(ActionNodeType(item), ActionNodeType(action), StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add(action);
        }

        private static string GetBranchPath(JObject branchPlan, string fieldKind, string pathName)
        {
            if (branchPlan == null) return "";

            var branches = branchPlan["branches"] as JArray;
            if (branches == null) return "";

            foreach (JToken token in branches)
            {
                JObject b = token as JObject;
                if (b == null) continue;

                string kind = Convert.ToString(b["fieldKind"] ?? "").Trim();
                if (!string.Equals(kind, fieldKind, StringComparison.OrdinalIgnoreCase)) continue;

                return Convert.ToString(b[pathName] ?? "").Trim();
            }

            return "";
        }

        private static JObject FindActionForPath(JArray actions, string path)
        {
            path = (path ?? "").Trim();
            if (path.Length == 0) return null;

            if (path.Equals("evaluar total", StringComparison.OrdinalIgnoreCase))
                return FindActionLabelStarts(actions, "control.if", "Total mayor a ");

            if (path.Equals("continuar", StringComparison.OrdinalIgnoreCase))
                return FirstAction(actions, "util.logger") ?? FirstAction(actions, "util.end");

            if (path.StartsWith("human.task:", StringComparison.OrdinalIgnoreCase))
            {
                string role = path.Substring("human.task:".Length).Trim();
                return FindHumanTaskByRole(actions, role);
            }

            return FindActionByLabel(actions, null, path);
        }

        private static JObject FindHumanTaskByRole(JArray actions, string role)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return null;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "human.task", StringComparison.OrdinalIgnoreCase)) continue;

                JObject p = a["params"] as JObject;
                string r = p == null ? "" : Convert.ToString(p["rol"] ?? "").Trim();
                if (string.Equals(r, role, StringComparison.OrdinalIgnoreCase))
                    return a;
            }

            return null;
        }

        private static JObject FirstAction(JArray actions, string nodeType)
        {
            if (actions == null) return null;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (string.Equals(ActionNodeType(a), nodeType, StringComparison.OrdinalIgnoreCase))
                    return a;
            }

            return null;
        }

        private static JObject FirstActionAfter(JArray actions, JObject previous)
        {
            if (actions == null || previous == null) return null;

            bool foundPrevious = false;
            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;

                if (foundPrevious) return a;
                if (object.ReferenceEquals(a, previous)) foundPrevious = true;
            }

            return null;
        }

        private static JObject FindActionByLabel(JArray actions, string nodeType, string label)
        {
            if (actions == null || string.IsNullOrWhiteSpace(label)) return null;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.IsNullOrWhiteSpace(nodeType) && !string.Equals(ActionNodeType(a), nodeType, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(ActionLabel(a), label, StringComparison.OrdinalIgnoreCase)) return a;
            }

            return null;
        }

        private static JObject FindActionLabelStarts(JArray actions, string nodeType, string labelPrefix)
        {
            if (actions == null || string.IsNullOrWhiteSpace(labelPrefix)) return null;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), nodeType, StringComparison.OrdinalIgnoreCase)) continue;
                if (ActionLabel(a).StartsWith(labelPrefix, StringComparison.OrdinalIgnoreCase)) return a;
            }

            return null;
        }

        private static void AddConnection(JArray result, JObject fromAction, JObject toAction, string condition)
        {
            if (result == null || fromAction == null || toAction == null) return;

            string from = ActionLabel(fromAction);
            string to = ActionLabel(toAction);
            string fromType = ActionNodeType(fromAction);
            string toType = ActionNodeType(toAction);
            condition = (condition ?? "").Trim();

            if (from.Length == 0 || to.Length == 0) return;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fromType, toType, StringComparison.OrdinalIgnoreCase)) return;

            foreach (JToken token in result)
            {
                JObject c = token as JObject;
                if (c == null) continue;

                if (string.Equals(Convert.ToString(c["from"] ?? ""), from, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(c["to"] ?? ""), to, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(c["condition"] ?? ""), condition, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            var item = new JObject
            {
                ["action"] = "CONNECT_NODES",
                ["from"] = from,
                ["to"] = to,
                ["fromNodeType"] = fromType,
                ["toNodeType"] = toType
            };

            if (condition.Length > 0)
                item["condition"] = condition;

            result.Add(item);
        }

        private static bool IsAddNode(JObject action)
        {
            return string.Equals(Convert.ToString(action["action"] ?? "").Trim(), "ADD_NODE", StringComparison.OrdinalIgnoreCase);
        }

        private static string ActionNodeType(JObject action)
        {
            return Convert.ToString(action == null ? "" : action["nodeType"] ?? "").Trim();
        }

        private static string ActionLabel(JObject action)
        {
            return Convert.ToString(action == null ? "" : action["label"] ?? "").Trim();
        }

        private static string BuildMessage(JArray actions, string docTipo, string role, string userKey, string amount, BranchAnalysis branches)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(docTipo)) parts.Add("documento " + docTipo);
            if (!string.IsNullOrWhiteSpace(amount)) parts.Add("condición por total mayor a " + amount);

            if (branches != null)
            {
                if (!string.IsNullOrWhiteSpace(branches.CaeFalseRole)) parts.Add("si falta CAE derivar a " + branches.CaeFalseRole);
                if (!string.IsNullOrWhiteSpace(branches.TotalTrueRole)) parts.Add("si supera el total derivar a " + branches.TotalTrueRole);
                if (!string.IsNullOrWhiteSpace(branches.TotalFalseRole)) parts.Add("si no supera el total derivar a " + branches.TotalFalseRole);
            }

            if (FirstAction(actions, "email.send") != null) parts.Add("envío de correo");
            if (!string.IsNullOrWhiteSpace(role) && (branches == null || !branches.HasBranchInfo) && FirstAction(actions, "human.task") != null) parts.Add("derivación al rol " + role);
            if (!string.IsNullOrWhiteSpace(userKey) && FirstAction(actions, "human.task") != null) parts.Add("derivación al usuario " + userKey);

            if (parts.Count == 0)
                return "Recibí la intención y preparé una propuesta inicial para revisar.";

            return "Preparé una propuesta con " + string.Join(", ", parts.ToArray()) + ".";
        }

        private static EmailRequest AnalyzeEmailRequest(string originalText, string normalizedText, WfAiCatalog catalog)
        {
            List<EmailRequest> requests = AnalyzeEmailRequests(originalText, normalizedText, catalog);
            return requests.Count > 0 ? requests[0] : new EmailRequest();
        }

        private static List<EmailRequest> AnalyzeEmailRequests(string originalText, string normalizedText, WfAiCatalog catalog)
        {
            var list = ExtractStructuredEmailRequests(originalText);
            if (list.Count > 0) return list;

            var r = new EmailRequest();
            r.WantsEmail = WantsEmail(originalText, normalizedText);
            if (!r.WantsEmail) return list;

            foreach (string addr in ExtractEmailAddresses(originalText))
            {
                if (!ContainsIgnoreCase(r.To, addr))
                    r.To.Add(addr);
            }

            r.RecipientHint = ExtractEmailRecipientHint(originalText, normalizedText, catalog);
            r.Subject = ExtractEmailSubject(originalText);
            r.Body = ExtractEmailBody(originalText);
            FillEmailDefaults(r);
            list.Add(r);
            return list;
        }

        private static List<EmailRequest> ExtractStructuredEmailRequests(string originalText)
        {
            var list = new List<EmailRequest>();
            if (string.IsNullOrWhiteSpace(originalText)) return list;

            string pattern = @"\b(?:enviar|mandar|notificar)\s+(?:otro\s+|otra\s+|un\s+|una\s+)?(?:correo|mail|email|e-mail)\s+(?:a|para)\s+(?<to>[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,})\s+(?:con\s+)?(?:asunto|subject)\s+(?<subject>.+?)\s+(?:y\s+)?(?:cuerpo|body|mensaje)\s+(?<body>.+?)(?=(?:,|\.)\s*\b(?:luego|despues|después|mandar|enviar|derivar|pasar|validar|si|registrar|finalizar|terminar)\b|[\r\n]|$)";
            var matches = Regex.Matches(originalText, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match m in matches)
            {
                var r = new EmailRequest();
                r.WantsEmail = true;
                string to = (m.Groups["to"].Value ?? "").Trim().TrimEnd('.', ',', ';', ':', ')', ']');
                if (to.Length > 0) r.To.Add(to);
                r.Subject = CleanExtractedSentence(m.Groups["subject"].Value);
                r.Body = CleanExtractedSentence(m.Groups["body"].Value);
                FillEmailDefaults(r);
                list.Add(r);
            }

            return list;
        }

        private static void FillEmailDefaults(EmailRequest r)
        {
            if (r == null) return;
            if (string.IsNullOrWhiteSpace(r.Subject))
                r.Subject = "Notificación Workflow Studio";

            if (string.IsNullOrWhiteSpace(r.Body))
                r.Body = "Correo generado por Workflow Studio.";
        }

        private static JObject EmailParams(EmailRequest request)
        {
            var p = new JObject
            {
                ["to"] = JArray.FromObject(request.To),
                ["subject"] = request.Subject ?? "",
                ["body"] = request.Body ?? "",
                ["modo"] = "real",
                ["useWebConfig"] = true,
                ["isHtml"] = false
            };
            return p;
        }

        private static string EmailMissingRecipientQuestion(EmailRequest request)
        {
            string hint = (request == null ? "" : request.RecipientHint ?? "").Trim();
            if (hint.Length > 0)
                return "Detecté que querés enviar un correo a " + hint + ", pero no tengo una dirección de email. Indicá el email destino.";
            return "Detecté que querés enviar un correo, pero falta la dirección de email destino.";
        }

        private static bool WantsEmail(string originalText, string normalizedText)
        {
            string t = Normalize(normalizedText);
            if (ExtractEmailAddresses(originalText).Count > 0) return true;

            return ContainsAny(t,
                "correo", "mail", "email", "e mail", "notificar por correo", "notificacion por correo",
                "enviar correo", "enviar un correo", "mandar correo", "mandar un correo",
                "enviar mail", "enviar un mail", "mandar mail", "mandar un mail",
                "enviar email", "mandar email");
        }

        private static bool HasExplicitHumanTaskSignal(string normalizedText)
        {
            string t = Normalize(normalizedText);

            if (ContainsAny(t,
                "tarea", "tarea humana", "asignar tarea", "crear tarea",
                "derivar a", "pasar a", "enviar a compras", "mandar a compras",
                "enviar a direccion", "mandar a direccion", "enviar a gerencia", "mandar a gerencia",
                "enviar a administracion", "mandar a administracion", "enviar a operaciones", "mandar a operaciones",
                "aprobar", "aprobacion", "autorizar", "autorizacion", "revision", "revisar"))
                return true;

            return false;
        }

        private static bool UserMentionAppearsOnlyInEmailContext(string normalizedText, string userKey)
        {
            if (string.IsNullOrWhiteSpace(userKey)) return false;
            string t = Normalize(normalizedText);

            if (!ContainsAny(t, "correo", "mail", "email", "e mail")) return false;
            if (ContainsAny(t, "tarea a", "usuario", "asignar a", "derivar a usuario", "mandar la tarea", "enviar la tarea")) return false;

            return true;
        }

        private static string ExtractPreBranchHumanTaskRole(string normalizedText, WfAiCatalog catalog)
        {
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return "";

            int cut = FirstIndexOfAny(t,
                " validar si ", " si el total ", " si total ", " si supera ", " si no supera ",
                " cuando ", " total mayor ", " supera ");
            string head = cut >= 0 ? t.Substring(0, cut) : t;

            string[] markers = new[]
            {
                "mandar la tarea a", "enviar la tarea a", "derivar la tarea a", "pasar la tarea a",
                "mandar tarea a", "enviar tarea a", "derivar tarea a", "pasar tarea a",
                "asignar tarea a", "crear tarea a"
            };

            foreach (string m in markers)
            {
                int idx = head.LastIndexOf(" " + m + " ", StringComparison.OrdinalIgnoreCase);
                if (idx < 0 && head.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase)) idx = -1;
                if (idx < 0) continue;

                string tail = idx == -1 ? head.Substring(m.Length).Trim() : head.Substring(idx + m.Length + 2).Trim();
                string role = ResolveRole(tail, catalog);
                if (!string.IsNullOrWhiteSpace(role)) return role;
            }

            return "";
        }

        private static int FirstIndexOfAny(string text, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text) || values == null) return -1;
            int best = -1;
            foreach (string v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                int idx = text.IndexOf(v, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0 && (best < 0 || idx < best)) best = idx;
            }
            return best;
        }

        private static List<string> ExtractEmailAddresses(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            var matches = Regex.Matches(text, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                string v = (m.Value ?? "").Trim().TrimEnd('.', ',', ';', ':', ')', ']');
                if (v.Length > 0 && !ContainsIgnoreCase(list, v))
                    list.Add(v);
            }
            return list;
        }

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            if (list == null || value == null) return false;
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string ExtractEmailSubject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var m = Regex.Match(text,
                @"\b(?:asunto|subject)\b\s*[:\-]?\s*(?<v>.+?)(?=(?:\s+y\s+)?\b(?:cuerpo|body|mensaje|luego|despues|después|registrar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!m.Success) return "";
            return CleanExtractedSentence(m.Groups["v"].Value);
        }

        private static string ExtractEmailBody(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var m = Regex.Match(text,
                @"\b(?:cuerpo|body|mensaje)\b\s*[:\-]?\s*(?<v>.+?)(?=(?:,|\.)?\s*\b(?:luego|despues|después|registrar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!m.Success) return "";
            return CleanExtractedSentence(m.Groups["v"].Value);
        }

        private static string ExtractEmailRecipientHint(string originalText, string normalizedText, WfAiCatalog catalog)
        {
            string t = Normalize(normalizedText);

            if (catalog != null && catalog.Users != null)
            {
                foreach (var u in catalog.Users)
                {
                    if (u == null) continue;
                    string displayName = (u.DisplayName ?? "").Trim();
                    string userKey = (u.UserKey ?? "").Trim();

                    if (displayName.Length > 0 && ContainsPhrase(t, displayName)) return displayName;
                    if (userKey.Length > 0 && ContainsPhrase(t, userKey)) return userKey;
                }
            }

            if (string.IsNullOrWhiteSpace(originalText)) return "";
            var m = Regex.Match(originalText,
                @"\b(?:correo|mail|email|e-mail)\b\s+(?:a|para)\s+(?<v>.+?)(?=\s+\b(?:con\s+asunto|asunto|subject|con\s+cuerpo|cuerpo|body|mensaje|luego|despues|después|registrar|finalizar|terminar)\b|,|\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!m.Success) return "";
            return CleanExtractedSentence(m.Groups["v"].Value);
        }

        private static string CleanExtractedSentence(string value)
        {
            value = (value ?? "").Trim();
            value = Regex.Replace(value, @"\s+", " ").Trim();
            value = value.Trim(' ', ',', ';', ':', '-', '.');
            if (value.StartsWith("con ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(4).Trim();
            return value;
        }

        private static string DocLoadLabel(string docTipo)
        {
            if (string.Equals(docTipo, "NOTA_CREDITO_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase))
                return "Cargar nota de crédito";
            if (string.Equals(docTipo, "FACTURA_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase))
                return "Cargar factura";
            return "Cargar documento";
        }

        private static string HumanTaskTitle(string role, string userKey)
        {
            if (!string.IsNullOrWhiteSpace(userKey)) return "Enviar a " + userKey;
            if (string.IsNullOrWhiteSpace(role)) return "Tarea humana";
            if (string.Equals(role, "COMPRAS", StringComparison.OrdinalIgnoreCase)) return "Enviar a Compras";
            if (string.Equals(role, "DIR_GENERAL", StringComparison.OrdinalIgnoreCase)) return "Aprobación Dirección";
            if (string.Equals(role, "ADM_FIN", StringComparison.OrdinalIgnoreCase)) return "Enviar a Administración";
            if (string.Equals(role, "OPERACIONES", StringComparison.OrdinalIgnoreCase)) return "Enviar a Operaciones";
            if (string.Equals(role, "IT", StringComparison.OrdinalIgnoreCase)) return "Enviar a IT";
            return "Enviar a " + role;
        }

        private static JObject HumanTaskParams(string role, string title, string description)
        {
            var p = new JObject
            {
                ["titulo"] = title,
                ["descripcion"] = description
            };
            if (!string.IsNullOrWhiteSpace(role)) p["rol"] = role;
            return p;
        }

        private static string RoleFriendlyName(string role)
        {
            if (string.Equals(role, "COMPRAS", StringComparison.OrdinalIgnoreCase)) return "Compras";
            if (string.Equals(role, "DIR_GENERAL", StringComparison.OrdinalIgnoreCase)) return "Dirección";
            if (string.Equals(role, "ADM_FIN", StringComparison.OrdinalIgnoreCase)) return "Administración";
            if (string.Equals(role, "OPERACIONES", StringComparison.OrdinalIgnoreCase)) return "Operaciones";
            if (string.Equals(role, "IT", StringComparison.OrdinalIgnoreCase)) return "IT";
            return role ?? "";
        }

        private static BranchAnalysis AnalyzeBranches(string normalizedText, WfAiCatalog catalog, string amount)
        {
            var b = new BranchAnalysis();
            string pendingBranch = "";
            string lastConditionKind = "";

            foreach (string clause in SplitClausesForBranches(normalizedText))
            {
                string c = Normalize(clause);
                if (string.IsNullOrWhiteSpace(c)) continue;

                string r = ResolvePositiveDestinationRole(c, catalog);

                bool mentionsCae = ContainsToken(c, "cae") || ContainsToken(c, "cai");
                bool caeNegative = mentionsCae && ContainsAny(c,
                    "no tiene cae", "no tenga cae", "no posee cae", "falta cae", "falta el cae", "sin cae", "si no tiene cae", "cuando no tenga cae", "si falta cae", "si falta el cae");

                bool totalNegative = ContainsAny(c,
                    "no supera", "no lo supera", "no la supera", "no supera ese importe", "no supera el importe", "no supera dicho importe",
                    "no es mayor", "menor a", "menor que", "menor o igual", "no llega a", "por debajo");

                bool totalPositive = !totalNegative && ContainsAny(c,
                    "supera", "mayor a", "mayor que", "mas de", ">");

                bool contrary = ContainsAny(c, "caso contrario", "de lo contrario", "contrario");

                if (contrary && !string.IsNullOrWhiteSpace(r))
                {
                    if (string.Equals(lastConditionKind, "total", StringComparison.OrdinalIgnoreCase))
                    {
                        AssignBranchRole(b, "totalFalse", r);
                        pendingBranch = "";
                        continue;
                    }

                    if (string.Equals(lastConditionKind, "cae", StringComparison.OrdinalIgnoreCase))
                    {
                        AssignBranchRole(b, "caeFalse", r);
                        pendingBranch = "";
                        continue;
                    }
                }

                string branchKind = "";
                if (caeNegative) branchKind = "caeFalse";
                else if (totalNegative) branchKind = "totalFalse";
                else if (totalPositive) branchKind = "totalTrue";

                if (!string.IsNullOrWhiteSpace(branchKind))
                {
                    if (!string.IsNullOrWhiteSpace(r))
                    {
                        AssignBranchRole(b, branchKind, r);
                        pendingBranch = "";
                    }
                    else
                    {
                        pendingBranch = branchKind;
                    }

                    if (branchKind.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "total";
                    else if (branchKind.StartsWith("cae", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "cae";

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(r) && !string.IsNullOrWhiteSpace(pendingBranch))
                {
                    AssignBranchRole(b, pendingBranch, r);
                    pendingBranch = "";
                }
            }

            return b;
        }

        private static void AssignBranchRole(BranchAnalysis b, string branchKind, string role)
        {
            if (b == null || string.IsNullOrWhiteSpace(role)) return;

            if (string.Equals(branchKind, "caeFalse", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(b.CaeFalseRole))
                b.CaeFalseRole = role;

            if (string.Equals(branchKind, "totalTrue", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(b.TotalTrueRole))
                b.TotalTrueRole = role;

            if (string.Equals(branchKind, "totalFalse", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(b.TotalFalseRole))
                b.TotalFalseRole = role;
        }

        private static string ResolvePositiveDestinationRole(string clause, WfAiCatalog catalog)
        {
            string c = Normalize(clause);
            string fromMarker = ResolveRoleAfterPositiveDestinationMarker(c, catalog);
            if (!string.IsNullOrWhiteSpace(fromMarker)) return fromMarker;

            string role = ResolveRole(c, catalog);
            if (string.IsNullOrWhiteSpace(role)) return "";

            if (RoleAppearsOnlyInNegatedDestination(c, role, catalog))
                return "";

            return role;
        }

        private static string ResolveRoleAfterPositiveDestinationMarker(string clause, WfAiCatalog catalog)
        {
            string c = Normalize(clause);
            string[] markers = new[]
            {
                "mandarla a", "mandarlo a", "mandar a", "enviarla a", "enviarlo a", "enviar a",
                "derivarla a", "derivarlo a", "derivar a", "pasarla a", "pasarlo a", "pasar a",
                "debe ir a", "ir a", "lo revisa", "la revisa", "revisa", "aprueba", "aprobarla", "aprobarlo",
                "debe verla", "debe verlo", "verla", "verlo", "corregirlo", "corregirla", "corrige"
            };

            for (int i = 0; i < markers.Length; i++)
            {
                string marker = " " + markers[i] + " ";
                int idx = c.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                if (IsNegatedDestinationMarker(c, idx)) continue;

                string tail = c.Substring(idx + marker.Length);
                string r = ResolveRole(tail, catalog);
                if (!string.IsNullOrWhiteSpace(r)) return r;
            }

            return "";
        }

        private static bool IsNegatedDestinationMarker(string text, int markerIndex)
        {
            if (markerIndex < 0) return false;

            int start = Math.Max(0, markerIndex - 18);
            int end = Math.Min(text == null ? 0 : text.Length, markerIndex + 28);
            if (end <= start) return false;

            string window = text.Substring(start, end - start);

            return ContainsAny(window,
                "no debe ir a", "no debe enviar a", "no debe mandar a", "no debe derivar a", "no debe pasar a",
                "no tiene que ir a", "no tiene que enviar a", "no tiene que mandar a", "no tiene que derivar a", "no tiene que pasar a",
                "no ir a", "no enviar a", "no mandar a", "no derivar a", "no pasar a");
        }

        private static bool RoleAppearsOnlyInNegatedDestination(string clause, string role, WfAiCatalog catalog)
        {
            string c = Normalize(clause);
            if (string.IsNullOrWhiteSpace(role)) return false;

            var roleWords = new List<string>();
            roleWords.Add(role);
            roleWords.Add(role.Replace("_", " "));
            roleWords.Add(RoleFriendlyName(role));

            foreach (string word in roleWords)
            {
                string w = Normalize(word).Trim();
                if (w.Length == 0) continue;

                if (ContainsPhrase(c, w)
                    && (ContainsPhrase(c, "no debe ir a " + w)
                        || ContainsPhrase(c, "no debe enviar a " + w)
                        || ContainsPhrase(c, "no debe mandar a " + w)
                        || ContainsPhrase(c, "no debe derivar a " + w)
                        || ContainsPhrase(c, "no debe pasar a " + w)))
                    return true;
            }

            return false;
        }

        private static string DetectUnknownRoleMention(string normalizedText, WfAiCatalog catalog)
        {
            string t = Normalize(normalizedText);

            Match m = Regex.Match(t, @"\b(?:sector|area)\s+(?<name>[a-z0-9_]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string candidate = NormalizeUnknownRoleName(m.Groups["name"].Value);
                if (candidate.Length > 0 && string.IsNullOrWhiteSpace(ResolveRole(candidate, catalog)))
                    return candidate;
            }

            // Caso frecuente de prueba: "Legales" no debe convertirse en tarea humana si no existe como rol real.
            if (ContainsPhrase(t, "legales") && string.IsNullOrWhiteSpace(ResolveRole("legales", catalog)))
                return "Legales";

            return "";
        }

        private static string NormalizeUnknownRoleName(string raw)
        {
            string v = (raw ?? "").Trim();
            if (v.Length == 0) return "";

            if (ContainsAny(v, "si", "no", "que", "cuando", "para", "corregir", "validar", "finalizar", "terminar", "registrar"))
                return "";

            if (v.Length <= 1) return "";
            return char.ToUpperInvariant(v[0]) + v.Substring(1).ToLowerInvariant();
        }

        private static List<string> SplitClausesForBranches(string normalizedText)
        {
            var result = new List<string>();
            string t = Normalize(normalizedText).Trim();

            t = Regex.Replace(t, @"(?<!\d)\.(?!\d)", "|");
            t = Regex.Replace(t, @"\s*,\s*", "|");
            t = Regex.Replace(t, @"\s+y\s+si\s+", "|si ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+pero\s+si\s+", "|si ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+pero\s+antes\s+", "|pero antes ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+de\s+lo\s+contrario\s+", "|de lo contrario ", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+caso\s+contrario\s+", "|caso contrario ", RegexOptions.IgnoreCase);

            foreach (string part in t.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.Length > 0) result.Add(p);
            }

            return result;
        }

        private static JObject BuildBranchPlan(bool wantsCaeValidation, string amount, BranchAnalysis branches)
        {
            var branchPlan = new JObject
            {
                ["planner"] = "runtime-branch-planner-v1",
                ["hasBranches"] = branches != null && branches.HasBranchInfo
            };

            var items = new JArray();

            if (wantsCaeValidation)
            {
                items.Add(new JObject
                {
                    ["condition"] = "CAE informado",
                    ["fieldKind"] = "cae",
                    ["truePath"] = string.IsNullOrWhiteSpace(amount) ? "continuar" : "evaluar total",
                    ["falsePath"] = string.IsNullOrWhiteSpace(branches.CaeFalseRole) ? "pendiente de definir" : "human.task:" + branches.CaeFalseRole
                });
            }

            if (!string.IsNullOrWhiteSpace(amount))
            {
                items.Add(new JObject
                {
                    ["condition"] = "Total mayor a " + amount,
                    ["fieldKind"] = "total",
                    ["truePath"] = string.IsNullOrWhiteSpace(branches.TotalTrueRole) ? "pendiente de definir" : "human.task:" + branches.TotalTrueRole,
                    ["falsePath"] = string.IsNullOrWhiteSpace(branches.TotalFalseRole) ? "pendiente de definir" : "human.task:" + branches.TotalFalseRole
                });
            }

            branchPlan["branches"] = items;
            return branchPlan;
        }

        private static bool HasIntent(List<WfAiPredictedIntent> predictions, string intent)
        {
            if (predictions == null) return false;
            double min = GetDoubleSetting("WF_AI_MLNET_MIN_CONFIDENCE", 0.25);
            foreach (var p in predictions)
            {
                if (string.Equals(p.Intent, intent, StringComparison.OrdinalIgnoreCase) && p.Score >= min)
                    return true;
            }
            return false;
        }

        private static double AggregateConfidence(List<WfAiPredictedIntent> predictions)
        {
            if (predictions == null || predictions.Count == 0) return 0.0;
            double max = 0.0;
            foreach (var p in predictions)
                if (p.Score > max) max = p.Score;
            return Math.Round(max, 4);
        }

        private static string ResolveDocTipo(string normalizedText, WfAiCatalog catalog)
        {
            // Resolver global de DocTipo.
            // Importante: Normalize conserva puntos y comas para importes/rutas, por eso no alcanza con buscar " nc ".
            // En frases largas el usuario suele escribir "NC," o "factura," y eso antes no matcheaba.
            string t = Normalize(normalizedText);

            if (ContainsDocToken(t, "nc")
                || ContainsDocToken(t, "n/c")
                || ContainsDocToken(t, "nota credito")
                || ContainsDocToken(t, "nota de credito")
                || ContainsDocToken(t, "nota credito electronica")
                || ContainsDocToken(t, "nota de credito electronica")
                || ContainsDocToken(t, "credito electronica")
                || ContainsDocToken(t, "credito electronico"))
                return ExistingDocTypeOrEmpty(catalog, "NOTA_CREDITO_ELECTRONICA_AR");

            if (ContainsDocToken(t, "factura")
                || ContainsDocToken(t, "factura electronica")
                || ContainsDocToken(t, "fc")
                || ContainsDocToken(t, "fce"))
                return ExistingDocTypeOrEmpty(catalog, "FACTURA_ELECTRONICA_AR");

            // Si el usuario escribe directamente el código técnico del DocTipo, usarlo si está activo.
            if (catalog != null && catalog.DocTypes != null)
            {
                foreach (var d in catalog.DocTypes)
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Codigo)) continue;
                    if (ContainsDocToken(t, d.Codigo))
                        return d.Codigo;
                }
            }

            return "";
        }

        private static bool ContainsDocToken(string normalizedText, string token)
        {
            string t = Normalize(normalizedText).Trim();
            string n = Normalize(token).Trim();
            if (n.Length == 0) return false;

            // Límite lógico: espacio, coma, punto, slash, barra invertida o inicio/fin.
            string pattern = @"(^|[\s,\./\\])" + Regex.Escape(n).Replace(@"\ ", @"\s+") + @"($|[\s,\./\\])";
            return Regex.IsMatch(t, pattern, RegexOptions.IgnoreCase);
        }

        private static string ExistingDocTypeOrEmpty(WfAiCatalog catalog, string code)
        {
            if (catalog == null || catalog.DocTypes == null) return code;
            foreach (var d in catalog.DocTypes)
            {
                if (string.Equals(d.Codigo, code, StringComparison.OrdinalIgnoreCase))
                    return d.Codigo;
            }
            return "";
        }

        private static string ResolvePrefix(string docTipo, WfAiCatalog catalog)
        {
            if (!string.IsNullOrWhiteSpace(docTipo) && catalog != null && catalog.DocTypes != null)
            {
                foreach (var d in catalog.DocTypes)
                {
                    if (string.Equals(d.Codigo, docTipo, StringComparison.OrdinalIgnoreCase))
                        return d.ContextPrefix ?? "";
                }
            }

            if (string.Equals(docTipo, "FACTURA_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase)) return "factura";
            if (string.Equals(docTipo, "NOTA_CREDITO_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase)) return "notaCredito";
            return "";
        }

        private static string ResolveRole(string normalizedText, WfAiCatalog catalog)
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "compras", "COMPRAS" },
                { "compra", "COMPRAS" },
                { "sector compras", "COMPRAS" },
                { "area compras", "COMPRAS" },
                { "dirección", "DIR_GENERAL" },
                { "direccion", "DIR_GENERAL" },
                { "dirección general", "DIR_GENERAL" },
                { "direccion general", "DIR_GENERAL" },
                { "dir general", "DIR_GENERAL" },
                { "gerencia", "DIR_GENERAL" },
                { "gerente", "DIR_GENERAL" },
                { "administración", "ADM_FIN" },
                { "administracion", "ADM_FIN" },
                { "adm fin", "ADM_FIN" },
                { "administración finanzas", "ADM_FIN" },
                { "administracion finanzas", "ADM_FIN" },
                { "finanzas", "ADM_FIN" },
                { "operaciones", "OPERACIONES" },
                { "operación", "OPERACIONES" },
                { "operacion", "OPERACIONES" },
                { "it", "IT" },
                { "sistemas", "IT" },
                { "informática", "IT" },
                { "informatica", "IT" }
            };

            foreach (var kv in aliases)
            {
                if (ContainsPhrase(normalizedText, kv.Key))
                    return ExistingRoleOrEmpty(catalog, kv.Value);
            }

            if (catalog != null && catalog.Roles != null)
            {
                foreach (string r in catalog.Roles)
                {
                    if (ContainsPhrase(normalizedText, r) || ContainsPhrase(normalizedText, r.Replace("_", " ")))
                        return r;
                }
            }

            return "";
        }

        private static string ExistingRoleOrEmpty(WfAiCatalog catalog, string role)
        {
            if (catalog == null || catalog.Roles == null) return role;
            foreach (string r in catalog.Roles)
            {
                if (string.Equals(r, role, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return "";
        }

        private static string ResolveUser(string normalizedText, WfAiCatalog catalog)
        {
            if (catalog == null || catalog.Users == null) return "";

            foreach (var u in catalog.Users)
            {
                if (u == null) continue;

                string userKey = (u.UserKey ?? "").Trim();
                string displayName = (u.DisplayName ?? "").Trim();

                if (userKey.Length > 0 && ContainsPhrase(normalizedText, userKey))
                    return userKey;

                if (displayName.Length > 0 && ContainsPhrase(normalizedText, displayName))
                    return userKey;
            }

            return "";
        }

        private static string ExtractAmount(string normalizedText)
        {
            var m = Regex.Match(normalizedText,
                @"(?:supera|mayor(?:\s+a)?|mas\s+de|importe\s+mayor\s+a|total\s+mayor\s+a|>)\s*(?:los|las|a|de)?\s*\$?\s*(?<n>(?:\d{1,3}(?:[\.\s]\d{3})+(?:,\d+)?|\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:[\.,]\d+)?))",
                RegexOptions.IgnoreCase);

            if (!m.Success) return "";

            return NormalizeAmountValue(m.Groups["n"].Value);
        }

        private static string NormalizeAmountValue(string raw)
        {
            raw = (raw ?? "").Trim().Replace(" ", "");
            if (raw.Length == 0) return "";

            bool hasDot = raw.IndexOf('.') >= 0;
            bool hasComma = raw.IndexOf(',') >= 0;

            if (hasDot && hasComma)
            {
                int lastDot = raw.LastIndexOf('.');
                int lastComma = raw.LastIndexOf(',');

                if (lastComma > lastDot)
                    raw = raw.Replace(".", "").Replace(',', '.'); // 200.000,50 => 200000.50
                else
                    raw = raw.Replace(",", ""); // 200,000.50 => 200000.50
            }
            else if (hasDot)
            {
                if (Regex.IsMatch(raw, @"^\d{1,3}(?:\.\d{3})+$"))
                    raw = raw.Replace(".", ""); // 200.000 => 200000
            }
            else if (hasComma)
            {
                if (Regex.IsMatch(raw, @"^\d{1,3}(?:,\d{3})+$"))
                    raw = raw.Replace(",", ""); // 200,000 => 200000
                else
                    raw = raw.Replace(',', '.'); // 200000,50 => 200000.50
            }

            decimal value;
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return value.ToString("0.################", CultureInfo.InvariantCulture);

            return "";
        }

        private static bool ContainsToken(string normalizedText, string token)
        {
            string haystack = Normalize(normalizedText).Trim();
            string needle = Normalize(token).Trim();
            if (needle.Length == 0) return false;

            string pattern = @"(^|[^a-z0-9_])" + Regex.Escape(needle) + @"($|[^a-z0-9_])";
            return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase);
        }

        private static bool ContainsPhrase(string normalizedText, string phrase)
        {
            string haystack = Normalize(normalizedText).Trim();
            string needle = Normalize(phrase).Trim();
            if (needle.Length == 0) return false;

            string pattern = @"(^|[^a-z0-9_])" + Regex.Escape(needle).Replace(@"\ ", @"\s+") + @"($|[^a-z0-9_])";
            return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (text == null) text = "";

            foreach (string v in values)
            {
                string n = Normalize(v).Trim();
                if (n.Length == 0) continue;

                if (Regex.IsMatch(n, @"^[a-z0-9_]+(?:\s+[a-z0-9_]+)*$", RegexOptions.IgnoreCase))
                {
                    if (ContainsPhrase(text, n)) return true;
                }
                else
                {
                    string haystack = Normalize(text);
                    if (haystack.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }

        private static List<string> SplitSegments(string text)
        {
            var result = new List<string>();
            string t = Normalize(text);
            t = Regex.Replace(t,
                @"\s+(y\s+si|si|luego|despues|después|entonces|y\s+quiero|y\s+mandar(?:lo|la)?|y\s+mandarlo|y\s+mandarla|y\s+enviar(?:lo|la)?|y\s+enviarlo|y\s+enviarla|y\s+derivar(?:lo|la)?|y\s+pasar(?:lo|la)?|quiero)\s+",
                "|$1 ",
                RegexOptions.IgnoreCase);

            foreach (string part in t.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.StartsWith("y ", StringComparison.OrdinalIgnoreCase)) p = p.Substring(2).Trim();
                if (p.Length > 0) result.Add(p);
            }
            return result;
        }

        private static string Normalize(string text)
        {
            string s = (text ?? "").ToLowerInvariant().Trim();
            s = RemoveDiacritics(s);
            s = Regex.Replace(s, @"[^a-z0-9_/\\\.\,>\$]+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return " " + s + " ";
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string formD = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in formD)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static double MaxScore(float[] scores)
        {
            if (scores == null || scores.Length == 0) return 0.0;
            float max = scores[0];
            for (int i = 1; i < scores.Length; i++)
                if (scores[i] > max) max = scores[i];
            return Math.Round(max, 4);
        }

        private static string MapConfiguredPath(string key, string fallbackRelativePath)
        {
            string configured = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(configured)) configured = fallbackRelativePath;
            configured = configured.Trim();

            if (Path.IsPathRooted(configured)) return configured;

            if (HttpContext.Current != null && HttpContext.Current.Server != null)
                return HttpContext.Current.Server.MapPath("~/" + configured.Replace("\\", "/"));

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
        }

        private static double GetDoubleSetting(string key, double fallback)
        {
            string v = ConfigurationManager.AppSettings[key];
            double n;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out n)) return n;
            if (double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out n)) return n;
            return fallback;
        }

        private class EmailRequest
        {
            public bool WantsEmail { get; set; }
            public List<string> To { get; private set; }
            public string RecipientHint { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }

            public EmailRequest()
            {
                WantsEmail = false;
                To = new List<string>();
                RecipientHint = "";
                Subject = "";
                Body = "";
            }
        }

        private class BranchAnalysis
        {
            public string CaeFalseRole { get; set; }
            public string TotalTrueRole { get; set; }
            public string TotalFalseRole { get; set; }

            public BranchAnalysis()
            {
                CaeFalseRole = "";
                TotalTrueRole = "";
                TotalFalseRole = "";
            }

            public bool HasBranchInfo
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(CaeFalseRole)
                        || !string.IsNullOrWhiteSpace(TotalTrueRole)
                        || !string.IsNullOrWhiteSpace(TotalFalseRole);
                }
            }
        }

        public class WfAiIntentInput
        {
            [LoadColumn(0)]
            public string Intent { get; set; }

            [LoadColumn(1)]
            public string Texto { get; set; }
        }

        public class WfAiIntentOutput
        {
            [ColumnName("PredictedLabel")]
            public string PredictedLabel { get; set; }

            public float[] Score { get; set; }
        }

        public class WfAiPredictedIntent
        {
            public string Text { get; set; }
            public string Intent { get; set; }
            public double Score { get; set; }
        }
    }
}
