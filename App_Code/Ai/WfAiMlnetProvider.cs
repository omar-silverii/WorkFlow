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
            // fix52: Phrase Engine v2 en modo diagnóstico/controlado.
            // No reemplaza todavía la lógica legacy; deja disponible análisis de cláusulas, conceptos
            // y señales léxicas para empezar a sacar lógica de este proveedor gigante sin romper lo validado.
            PhraseEngineDiagnostic phraseEngine = BuildPhraseEngineDiagnostic(userText);

            string norm = Normalize(userText);
            string docTipo = ResolveDocTipo(norm, catalog);
            string prefix = ResolvePrefix(docTipo, catalog);
            string role = ResolveRole(norm, catalog);
            string userKey = ResolveUser(norm, catalog);
            string amount = ExtractAmount(norm);
            List<GuidedConditionRequest> guidedConditions = AnalyzeGuidedConditions(userText);
            bool hasGuidedConditions = guidedConditions.Count > 0;
            NaturalCompositeConditionRequest naturalCompositeCondition = AnalyzeNaturalCompositeCondition(userText, norm, catalog, prefix, amount);
            bool hasNaturalCompositeCondition = naturalCompositeCondition != null && naturalCompositeCondition.IsDetected;
            List<HumanTaskOutcomeRequest> humanTaskOutcomes = AnalyzeHumanTaskOutcomeRequests(userText, norm, catalog);
            bool hasHumanTaskOutcome = humanTaskOutcomes != null && humanTaskOutcomes.Count > 0;
            List<EmailRequest> emailRequests = AnalyzeEmailRequests(userText, norm, catalog);
            EmailRequest emailRequest = emailRequests.Count > 0 ? emailRequests[0] : new EmailRequest();
            NotifyRequest notifyRequest = AnalyzeNotifyRequest(userText, norm);
            StateVarsRequest stateVarsRequest = AnalyzeStateVarsRequest(userText);
            DelayRequest delayRequest = AnalyzeDelayRequest(norm);
            HttpRequestRequest httpRequest = AnalyzeHttpRequestRequest(userText, norm);
            SqlRequest sqlRequest = AnalyzeSqlRequest(userText, norm);
            FileWriteRequest fileWriteRequest = AnalyzeFileWriteRequest(userText, norm);
            FileReadRequest fileReadRequest = AnalyzeFileReadRequest(userText, norm);
            QueuePublishRequest queuePublishRequest = AnalyzeQueuePublishRequest(userText, norm);
            QueueConsumeRequest queueConsumeRequest = AnalyzeQueueConsumeRequest(userText, norm);
            StandaloneLoggerRequest standaloneLoggerRequest = AnalyzeStandaloneLoggerRequest(userText, norm);
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
            else if (notifyRequest.WantsNotify && RoleMentionAppearsOnlyInNotifyContext(norm, role))
            {
                role = "";
                userKey = "";
            }

            bool wantsCaeValidation = !hasGuidedConditions && !hasNaturalCompositeCondition && (ContainsToken(norm, "cae") || ContainsToken(norm, "cai"));
            if (hasGuidedConditions || hasNaturalCompositeCondition)
            {
                // Si el constructor guiado o la frase libre armó un IF explícito por campo, evitamos duplicar
                // condiciones legacy por CAE/total detectadas por palabras sueltas o importes.
                amount = "";
            }

            // No alcanza con que ML.NET prediga CARGAR_DOCUMENTO:
            // frases de variables como "quitar variable" pueden clasificarse mal.
            // Para agregar doc.load exigimos una señal explícita de documento en la frase.
            bool hasExplicitDocumentSignal = docTipo.Length > 0
                || ContainsAny(norm, "cargar", "subir", "leer", "documento", "factura", "nota credito", "nota de credito", " nc ", "comprobante");
            bool wantsDocument = hasExplicitDocumentSignal;
            if ((fileWriteRequest.WantsFileWrite || fileReadRequest.WantsFileRead)
                && string.IsNullOrWhiteSpace(docTipo)
                && !ContainsAny(norm, "nota credito", "nota de credito", "factura", "documento"))
            {
                // fix64: frases como "leer archivo" o "escribir archivo" son file.read/file.write,
                // no doc.load. Evitamos que la palabra "leer" dispare carga documental.
                wantsDocument = false;
            }

            bool wantsHumanTask = role.Length > 0 || userKey.Length > 0 || HasIntent(predictions, "CREAR_TAREA_ROL") || HasIntent(predictions, "CONDICION_Y_TAREA") || ContainsAny(norm, "enviar a", "mandar a", "derivar a", "pasar a", "aprobar", "revision");
            if (emailRequest.WantsEmail && !HasExplicitHumanTaskSignal(norm))
                wantsHumanTask = false;
            if ((httpRequest.WantsHttp || sqlRequest.WantsSql
                    || fileWriteRequest.WantsFileWrite || fileReadRequest.WantsFileRead
                    || queuePublishRequest.WantsQueuePublish || queueConsumeRequest.WantsQueueConsume)
                && !HasExplicitHumanTaskSignal(norm))
                wantsHumanTask = false;
            bool wantsLogger = HasIntent(predictions, "REGISTRAR_LOG") || ContainsAny(norm, "log", "registrar", "dejar constancia");
            if (hasHumanTaskOutcome)
            {
                // fix45: si la frase ya pidió qué registrar al aprobar/rechazar una tarea humana,
                // esos logger se agregan como ramas del resultado humano. Evitamos crear un logger común
                // que mezcle las ramas y oculte la decisión APTO/NO APTO.
                wantsLogger = false;
            }
            bool wantsEnd = HasIntent(predictions, "FINALIZAR_FLUJO") || ContainsAny(norm, "finalizar", "terminar", "fin del flujo");

            BranchAnalysis branches = AnalyzeBranches(norm, catalog, amount);
            BranchLoggerRequest branchLoggerRequest = AnalyzeBranchLoggerRequest(userText, norm, amount);
            ApplyPhraseSemanticHintsToLegacyGeneration(phraseEngine, branches, branchLoggerRequest, humanTaskOutcomes);
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
                string docPath = ExtractDocumentPath(userText);

                var p = new JObject();
                if (docTipo.Length > 0) p["docTipoCodigo"] = docTipo;
                p["path"] = string.IsNullOrWhiteSpace(docPath) ? "${input.filePath}" : docPath;
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

            if (httpRequest.WantsHttp)
                AddHttpRequestAction(actions, httpRequest);

            if (sqlRequest.WantsSql)
                AddSqlRequestAction(actions, sqlRequest);

            if (fileWriteRequest.WantsFileWrite)
                AddFileWriteAction(actions, fileWriteRequest);

            if (fileReadRequest.WantsFileRead)
                AddFileReadAction(actions, fileReadRequest);

            if (queuePublishRequest.WantsQueuePublish)
                AddQueuePublishAction(actions, queuePublishRequest);

            if (queueConsumeRequest.WantsQueueConsume)
                AddQueueConsumeAction(actions, queueConsumeRequest);

            if (emailRequests.Count > 0)
                AddEmailRequestAction(actions, missing, emailRequests[0], emailRequests.Count > 1 ? 1 : 0);

            if (stateVarsRequest.HasChanges)
                AddStateVarsAction(actions, stateVarsRequest);

            if (notifyRequest.WantsNotify)
                AddNotifyAction(actions, notifyRequest, catalog);

            bool preBranchTaskCreated = false;
            if (!string.IsNullOrWhiteSpace(preBranchRole))
            {
                actions.Add(AddNode("human.task", HumanTaskTitle(preBranchRole, ""), HumanTaskParams(preBranchRole, HumanTaskTitle(preBranchRole, ""), "Revisión previa generada por el Asistente IA.")));
                preBranchTaskCreated = true;
            }

            if (guidedConditions.Count > 0)
            {
                foreach (GuidedConditionRequest condition in guidedConditions)
                    AddGuidedConditionAction(actions, missing, condition);
            }

            if (hasNaturalCompositeCondition)
            {
                AddNaturalCompositeConditionAction(actions, missing, naturalCompositeCondition);

                if (!string.IsNullOrWhiteSpace(naturalCompositeCondition.TrueRole))
                {
                    EnsureHumanTaskAction(actions, naturalCompositeCondition.TrueRole, HumanTaskTitle(naturalCompositeCondition.TrueRole, ""), "Rama SI CUMPLE de condición compuesta generada por el Asistente IA.");
                    branchTasksCreated = true;
                }
                else
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "compoundTruePath",
                        ["question"] = "¿A qué rol o usuario querés enviar la tarea si se cumple la condición compuesta?"
                    });
                }

                if (!string.IsNullOrWhiteSpace(naturalCompositeCondition.FalseRole))
                {
                    EnsureHumanTaskAction(actions, naturalCompositeCondition.FalseRole, HumanTaskTitle(naturalCompositeCondition.FalseRole, ""), "Rama NO CUMPLE de condición compuesta generada por el Asistente IA.");
                    branchTasksCreated = true;
                }
                else
                {
                    missing.Add(new JObject
                    {
                        ["key"] = "compoundFalsePath",
                        ["question"] = "¿A qué rol o usuario querés enviar la tarea si NO se cumple la condición compuesta?"
                    });
                }
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
                else if (branchLoggerRequest != null
                    && branchLoggerRequest.IsDetected
                    && string.Equals(branchLoggerRequest.BranchKind, "totalFalse", StringComparison.OrdinalIgnoreCase))
                {
                    string loggerLabel = string.IsNullOrWhiteSpace(branchLoggerRequest.Label) ? "Registrar evento" : branchLoggerRequest.Label;
                    actions.Add(AddNode("util.logger", loggerLabel, new JObject
                    {
                        ["level"] = string.IsNullOrWhiteSpace(branchLoggerRequest.Level) ? "Info" : branchLoggerRequest.Level,
                        ["message"] = string.IsNullOrWhiteSpace(branchLoggerRequest.Message) ? "Evento generado por Asistente IA." : branchLoggerRequest.Message
                    }));
                    branches.TotalFalseActionLabel = loggerLabel;
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

            if (hasHumanTaskOutcome)
            {
                // fix48: las condiciones por importe legacy crean sus tareas después del análisis de
                // resultados humanos. Agregamos los IF wf.tarea.resultado recién después de que ya
                // existan las human.task de las ramas, para no dejar resultados de tarea antes de la tarea real.
                foreach (HumanTaskOutcomeRequest humanTaskOutcome in humanTaskOutcomes)
                    AddHumanTaskOutcomeActions(actions, humanTaskOutcome);
            }

            if (delayRequest.WantsDelay)
                AddDelayAction(actions, delayRequest);

            if (wantsLogger)
            {
                string level = string.IsNullOrWhiteSpace(standaloneLoggerRequest.Level) ? "Info" : standaloneLoggerRequest.Level;
                string label = string.Equals(level, "Warn", StringComparison.OrdinalIgnoreCase) ? "Registrar advertencia" : "Registrar evento";
                actions.Add(AddNode("util.logger", label, new JObject
                {
                    ["level"] = level,
                    ["message"] = string.IsNullOrWhiteSpace(standaloneLoggerRequest.Message) ? "Paso agregado por Asistente IA" : standaloneLoggerRequest.Message
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

            JObject branchPlan = BuildBranchPlan(wantsCaeValidation, amount, branches, naturalCompositeCondition);
            JArray proposedConnections = BuildProposedConnections(actions, branchPlan);

            JObject phraseSemanticConsistency = BuildPhraseSemanticConsistency(phraseEngine, actions, branchPlan, proposedConnections);

            warnings.Add("Proveedor ML.NET: interpretación local usando modelo entrenado externo. Todavía no se aplica al canvas automáticamente.");
            warnings.Add("Phrase Engine v2: análisis de cláusulas y léxico activo en modo diagnóstico; no reemplaza todavía el provider legacy.");
            if (branches.HasBranchInfo)
                warnings.Add("Branch Connector v1: se generaron conexiones propuestas para revisión. Todavía no se aplican al canvas automáticamente.");
            if (emailRequest.WantsEmail)
                warnings.Add("email.send: se genera nodo de correo, no tarea humana. El envío real depende de la configuración SMTP/Web.config.");
            if (notifyRequest.WantsNotify)
                warnings.Add("util.notify: se genera una notificación operativa interna; no envía email real.");
            if (!SemanticConsistencyOk(phraseSemanticConsistency))
                warnings.Add("Phrase Engine v2: hay diferencias entre la interpretación semántica y el grafo legacy generado. Revisar phraseSemanticConsistency antes de aplicar.");

            return new JObject
            {
                ["assistantVersion"] = "1.13-mlnet-operational-notify-fix49",
                ["intent"] = "build_workflow",
                ["confidence"] = AggregateConfidence(predictions),
                ["messageToUser"] = BuildMessage(actions, docTipo, role, userKey, amount, branches, naturalCompositeCondition),
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
                        ["notifyRequested"] = notifyRequest.WantsNotify,
                        ["notifyTitle"] = notifyRequest.Title,
                        ["notifyMessage"] = notifyRequest.Message,
                        ["stateVarsRequested"] = stateVarsRequest.HasChanges,
                        ["delayRequested"] = delayRequest.WantsDelay,
                        ["delayMilliseconds"] = delayRequest.Milliseconds,
                        ["delaySeconds"] = delayRequest.Seconds,
                        ["naturalCompositeRequested"] = hasNaturalCompositeCondition,
                        ["naturalCompositeMode"] = hasNaturalCompositeCondition ? naturalCompositeCondition.RulesMode : "",
                        ["naturalCompositeRules"] = hasNaturalCompositeCondition ? JArray.FromObject(naturalCompositeCondition.Rules) : new JArray(),
                        ["humanTaskOutcomeRequested"] = hasHumanTaskOutcome,
                        ["humanTaskOutcomeRole"] = hasHumanTaskOutcome ? string.Join(",", humanTaskOutcomes.ConvertAll(x => x.TaskRole).ToArray()) : "",
                        ["caeFalseRole"] = branches.CaeFalseRole,
                        ["totalTrueRole"] = branches.TotalTrueRole,
                        ["totalFalseRole"] = branches.TotalFalseRole,
                        ["phraseEngineActive"] = true,
                        ["phraseSemanticByClause"] = true,
                        ["phraseConcepts"] = JArray.FromObject(phraseEngine.Concepts),
                        ["phraseNodeTypes"] = JArray.FromObject(phraseEngine.NodeTypes),
                        ["phraseClauseCount"] = phraseEngine.Clauses.Count,
                        ["phraseClauses"] = JArray.FromObject(phraseEngine.DebugClauses()),
                        ["phraseSemanticConsistencyActive"] = true,
                        ["phraseSemanticConsistencyOk"] = SemanticConsistencyOk(phraseSemanticConsistency),
                        ["phraseSemanticConsistency"] = phraseSemanticConsistency,
                        ["phrasePrimaryRole"] = phraseEngine.PrimaryRole,
                        ["phrasePrimaryHumanOutcome"] = phraseEngine.PrimaryHumanOutcome,
                        ["phraseFirstNumber"] = phraseEngine.FirstNumber
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

        private static void ApplyPhraseSemanticHintsToLegacyGeneration(PhraseEngineDiagnostic phraseEngine, BranchAnalysis branches, BranchLoggerRequest branchLoggerRequest, List<HumanTaskOutcomeRequest> humanTaskOutcomes)
        {
            // fix56: empezamos a usar el Phrase Engine como corrector controlado del legacy,
            // pero solo para los casos ya diagnosticados/validados por fix54/fix55.
            // No reemplaza el provider completo: corrige ramas de importe, caso contrario con logger
            // y acciones APTO/NO_APTO con notificación/logger por rama.
            if (phraseEngine == null) return;

            ApplyPhraseSemanticBranchHints(phraseEngine, branches, branchLoggerRequest);
            ApplyPhraseSemanticOutcomeHints(phraseEngine, humanTaskOutcomes);
        }

        private static void ApplyPhraseSemanticBranchHints(PhraseEngineDiagnostic phraseEngine, BranchAnalysis branches, BranchLoggerRequest branchLoggerRequest)
        {
            if (phraseEngine == null || branches == null) return;

            foreach (PhraseClauseDiagnostic clause in phraseEngine.Clauses)
            {
                if (clause == null) continue;

                string clauseType = clause.ClauseType ?? "";
                string kind = clause.ConditionKind ?? "";
                string side = clause.BranchSide ?? "";
                string taskRole = clause.TaskRole ?? "";

                if (string.Equals(clauseType, "condition_branch", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(kind, "total", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(taskRole))
                {
                    if (IsPhraseTotalTrueSide(side) && string.IsNullOrWhiteSpace(branches.TotalTrueRole))
                        branches.TotalTrueRole = taskRole;

                    if (IsPhraseTotalFalseSide(side) && string.IsNullOrWhiteSpace(branches.TotalFalseRole))
                        branches.TotalFalseRole = taskRole;
                }

                if (IsPhraseElseLoggerClause(clause))
                {
                    // Caso real fix55:
                    // "Caso contrario, registrar un evento informativo indicando que no requiere aprobación de Dirección..."
                    // ResolveRole legacy podía leer "Dirección" y convertir la rama NO en human.task:DIR_GENERAL.
                    // Si el Phrase Engine dice que es logger sin TaskRole, se limpia esa falsa tarea humana
                    // y se deja la rama NO como logger operativo.
                    if (!string.IsNullOrWhiteSpace(clause.Role)
                        && string.Equals(branches.TotalFalseRole, clause.Role, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(clause.TaskRole))
                    {
                        branches.TotalFalseRole = "";
                    }

                    if (branchLoggerRequest != null)
                    {
                        string message = ExtractBranchLoggerMessage(clause.Text ?? "");
                        branchLoggerRequest.IsDetected = true;
                        branchLoggerRequest.BranchKind = "totalFalse";
                        branchLoggerRequest.Level = string.IsNullOrWhiteSpace(clause.LoggerLevel) ? "Info" : clause.LoggerLevel;
                        branchLoggerRequest.Message = string.IsNullOrWhiteSpace(message) ? "No requiere aprobación de Dirección." : message;
                        branchLoggerRequest.Label = string.Equals(branchLoggerRequest.Level, "Warn", StringComparison.OrdinalIgnoreCase)
                            ? "Registrar advertencia de rama alternativa"
                            : "Registrar evento sin aprobación de Dirección";
                    }
                }

                if (string.Equals(clauseType, "else_human_task", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(taskRole))
                {
                    if (string.IsNullOrWhiteSpace(branches.TotalTrueRole) && string.Equals(side, "opposite", StringComparison.OrdinalIgnoreCase))
                        branches.TotalTrueRole = taskRole;
                }
            }
        }

        private static bool IsPhraseTotalTrueSide(string side)
        {
            string s = Normalize(side).Trim();
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "true_of_greater_than", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPhraseTotalFalseSide(string side)
        {
            string s = Normalize(side).Trim();
            return string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "false_of_greater_than", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPhraseElseLoggerClause(PhraseClauseDiagnostic clause)
        {
            if (clause == null) return false;
            return string.Equals(clause.ClauseType ?? "", "else_branch", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(clause.LoggerLevel)
                && string.IsNullOrWhiteSpace(clause.TaskRole)
                && string.IsNullOrWhiteSpace(clause.NotifyRole);
        }

        private static void ApplyPhraseSemanticOutcomeHints(PhraseEngineDiagnostic phraseEngine, List<HumanTaskOutcomeRequest> humanTaskOutcomes)
        {
            if (phraseEngine == null || humanTaskOutcomes == null) return;

            foreach (PhraseClauseDiagnostic clause in phraseEngine.Clauses)
            {
                if (clause == null) continue;
                if (string.IsNullOrWhiteSpace(clause.HumanOutcome)) continue;
                if (string.IsNullOrWhiteSpace(clause.Role)) continue;

                // Solo usamos esto para cláusulas de resultado humano o notificación asociada a resultado humano.
                bool outcomeLike = string.Equals(clause.ClauseType ?? "", "human_result", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(clause.ClauseType ?? "", "notification", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(clause.NotifyRole));
                if (!outcomeLike) continue;

                HumanTaskOutcomeRequest request = FindOrCreateHumanTaskOutcomeRequest(humanTaskOutcomes, clause.Role);
                if (request == null) continue;

                bool approved = string.Equals(clause.HumanOutcome, "apto", StringComparison.OrdinalIgnoreCase);
                bool rejected = string.Equals(clause.HumanOutcome, "no_apto", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clause.HumanOutcome, "no apto", StringComparison.OrdinalIgnoreCase);

                if (approved)
                {
                    request.HasApprovedBranch = true;
                    if (!string.IsNullOrWhiteSpace(clause.NotifyRole))
                        request.ApprovedNotifyRole = clause.NotifyRole;

                    // Si la frase pidió notificar pero no pidió registrar, no inventamos logger.
                    request.ApprovedWantsLogger = !string.IsNullOrWhiteSpace(clause.LoggerLevel);

                    string msg = ExtractHumanTaskOutcomeMessage(phraseEngine.OriginalText, request.TaskRole, true);
                    if (!string.IsNullOrWhiteSpace(msg)) request.ApprovedMessage = msg;
                    if (string.IsNullOrWhiteSpace(request.ApprovedMessage))
                        request.ApprovedMessage = "Tarea de " + RoleFriendlyName(request.TaskRole) + " aprobada.";
                }

                if (rejected)
                {
                    request.HasRejectedBranch = true;
                    if (!string.IsNullOrWhiteSpace(clause.NotifyRole))
                        request.RejectedNotifyRole = clause.NotifyRole;

                    request.RejectedWantsLogger = !string.IsNullOrWhiteSpace(clause.LoggerLevel);

                    string msg = ExtractHumanTaskOutcomeMessage(phraseEngine.OriginalText, request.TaskRole, false);
                    if (!string.IsNullOrWhiteSpace(msg)) request.RejectedMessage = msg;
                    if (string.IsNullOrWhiteSpace(request.RejectedMessage))
                        request.RejectedMessage = "Tarea de " + RoleFriendlyName(request.TaskRole) + " rechazada.";
                }

                request.IsDetected = request.HasApprovedBranch || request.HasRejectedBranch;
            }
        }

        private static HumanTaskOutcomeRequest FindOrCreateHumanTaskOutcomeRequest(List<HumanTaskOutcomeRequest> list, string role)
        {
            if (list == null || string.IsNullOrWhiteSpace(role)) return null;

            foreach (HumanTaskOutcomeRequest item in list)
            {
                if (item == null) continue;
                if (string.Equals(item.TaskRole, role, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            var request = new HumanTaskOutcomeRequest
            {
                IsDetected = true,
                TaskRole = role
            };
            list.Add(request);
            return request;
        }

        private static List<HumanTaskOutcomeRequest> AnalyzeHumanTaskOutcomeRequests(string userText, string normalizedText, WfAiCatalog catalog)
        {
            var list = new List<HumanTaskOutcomeRequest>();
            string t = Normalize(string.IsNullOrWhiteSpace(userText) ? normalizedText : userText);
            if (string.IsNullOrWhiteSpace(t)) return list;

            var byRole = new Dictionary<string, HumanTaskOutcomeRequest>(StringComparer.OrdinalIgnoreCase);

            HumanTaskOutcomeRequest pendingOutcome = null;
            bool pendingApproved = false;
            bool pendingRejected = false;

            foreach (string clauseRaw in SplitClausesForBranches(t))
            {
                string c = Normalize(clauseRaw);
                if (string.IsNullOrWhiteSpace(c)) continue;

                bool clauseApproval = MentionsHumanTaskApproval(c);
                bool clauseReject = MentionsHumanTaskReject(c);

                if (!clauseApproval && !clauseReject)
                {
                    // fix49: frases humanas reales suelen separar por coma:
                    // "Si Dirección la aprueba, notificar a Compras..." o
                    // "Si Dirección la rechaza, registrar advertencia..., notificar...".
                    // La cláusula posterior pertenece al último resultado humano detectado.
                    if (pendingOutcome != null)
                    {
                        string pendingNotifyRole = ExtractOutcomeNotifyRole(c, catalog);
                        bool pendingLogger = ContainsAny(c, "registrar", "evento", "advertencia", "log");

                        if (pendingApproved)
                        {
                            if (pendingLogger) pendingOutcome.ApprovedWantsLogger = true;
                            if (!string.IsNullOrWhiteSpace(pendingNotifyRole)) pendingOutcome.ApprovedNotifyRole = pendingNotifyRole;
                        }

                        if (pendingRejected)
                        {
                            if (pendingLogger) pendingOutcome.RejectedWantsLogger = true;
                            if (!string.IsNullOrWhiteSpace(pendingNotifyRole)) pendingOutcome.RejectedNotifyRole = pendingNotifyRole;
                        }
                    }

                    continue;
                }

                // No alcanza con detectar "aprobar" en una tarea humana normal
                // (ej.: "enviarla a ADM_FIN para aprobar"). Para crear ramas APTO/NO APTO
                // tiene que ser una frase de resultado: "si Compras aprueba/rechaza", etc.
                if (!IsHumanTaskOutcomeClause(c)) continue;

                string role = ResolveRole(c, catalog);
                if (string.IsNullOrWhiteSpace(role)) continue;

                HumanTaskOutcomeRequest request;
                if (!byRole.TryGetValue(role, out request))
                {
                    request = new HumanTaskOutcomeRequest { TaskRole = role };
                    byRole[role] = request;
                    list.Add(request);
                }

                string notifyRole = ExtractOutcomeNotifyRole(c, catalog);
                bool clauseWantsLogger = ContainsAny(c, "registrar", "evento", "advertencia", "log");

                if (clauseApproval)
                {
                    request.HasApprovedBranch = true;
                    if (clauseWantsLogger) request.ApprovedWantsLogger = true;
                    if (!string.IsNullOrWhiteSpace(notifyRole)) request.ApprovedNotifyRole = notifyRole;
                }
                if (clauseReject)
                {
                    request.HasRejectedBranch = true;
                    if (clauseWantsLogger) request.RejectedWantsLogger = true;
                    if (!string.IsNullOrWhiteSpace(notifyRole)) request.RejectedNotifyRole = notifyRole;
                }

                pendingOutcome = request;
                pendingApproved = clauseApproval;
                pendingRejected = clauseReject;
            }

            foreach (HumanTaskOutcomeRequest request in list)
            {
                request.ApprovedMessage = ExtractHumanTaskOutcomeMessage(userText, request.TaskRole, true);
                request.RejectedMessage = ExtractHumanTaskOutcomeMessage(userText, request.TaskRole, false);

                if (request.HasApprovedBranch && !request.ApprovedWantsLogger && string.IsNullOrWhiteSpace(request.ApprovedNotifyRole))
                    request.ApprovedWantsLogger = true;
                if (request.HasRejectedBranch && !request.RejectedWantsLogger && string.IsNullOrWhiteSpace(request.RejectedNotifyRole))
                    request.RejectedWantsLogger = true;

                if (request.HasApprovedBranch && string.IsNullOrWhiteSpace(request.ApprovedMessage))
                    request.ApprovedMessage = "Tarea de " + RoleFriendlyName(request.TaskRole) + " aprobada.";
                if (request.HasRejectedBranch && string.IsNullOrWhiteSpace(request.RejectedMessage))
                    request.RejectedMessage = "Tarea de " + RoleFriendlyName(request.TaskRole) + " rechazada.";

                request.IsDetected = request.HasApprovedBranch || request.HasRejectedBranch;
            }

            list.RemoveAll(x => x == null || !x.IsDetected || string.IsNullOrWhiteSpace(x.TaskRole));
            return list;
        }

        private static HumanTaskOutcomeRequest AnalyzeHumanTaskOutcomeRequest(string userText, string normalizedText, WfAiCatalog catalog)
        {
            List<HumanTaskOutcomeRequest> requests = AnalyzeHumanTaskOutcomeRequests(userText, normalizedText, catalog);
            return requests.Count > 0 ? requests[0] : new HumanTaskOutcomeRequest();
        }

        private static bool IsHumanTaskOutcomeClause(string normalizedClause)
        {
            string c = Normalize(normalizedClause);
            if (string.IsNullOrWhiteSpace(c)) return false;

            return ContainsAny(c,
                "si ",
                "resultado", "resultado de tarea", "resultado humano", "wf.tarea.resultado",
                "cuando aprueba", "cuando rechaza", "cuando la aprueba", "cuando la rechaza",
                "si aprueba", "si rechaza", "si la aprueba", "si la rechaza", "si lo aprueba", "si lo rechaza");
        }

        private static bool MentionsHumanTaskApproval(string normalizedText)
        {
            string t = Normalize(normalizedText);
            return ContainsAny(t, "aprueba", "aprueban", "aprobado", "aprobada", "aprobar")
                || (ContainsAny(t, "apto", "apta") && !ContainsAny(t, "no apto", "no apta"));
        }

        private static bool MentionsHumanTaskReject(string normalizedText)
        {
            string t = Normalize(normalizedText);
            return ContainsAny(t, "rechaza", "rechazan", "rechazado", "rechazada", "rechazar", "no apto", "no apta", "no aprobado", "no aprobada");
        }

        private static string ExtractHumanTaskOutcomeMessage(string userText, bool approved)
        {
            return ExtractHumanTaskOutcomeMessage(userText, "", approved);
        }

        private static string ExtractHumanTaskOutcomeMessage(string userText, string role, bool approved)
        {
            if (string.IsNullOrWhiteSpace(userText)) return "";

            string roleNorm = Normalize(role).Trim();
            string friendlyNorm = Normalize(RoleFriendlyName(role)).Trim();

            string[] parts = Regex.Split(userText, @"(?<!\d)[\.;](?!\d)|\r?\n");
            foreach (string raw in parts)
            {
                string original = (raw ?? "").Trim();
                if (original.Length == 0) continue;

                string c = Normalize(original);
                bool matches = approved ? MentionsHumanTaskApproval(c) : MentionsHumanTaskReject(c);
                if (!matches) continue;
                if (!ContainsAny(c, "registrar", "evento", "advertencia", "log", "notificar", "avisar", "notificacion", "notificación")) continue;

                if (!string.IsNullOrWhiteSpace(roleNorm)
                    && !ContainsPhrase(c, roleNorm)
                    && !ContainsPhrase(c, friendlyNorm))
                    continue;

                Match m = Regex.Match(original, @"indicando\s+que\s+(?<msg>.*?)(?:\s+y\s+finalizar|\s+finalizar|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    string msg = CleanOutcomeMessage(m.Groups["msg"].Value);
                    if (msg.Length > 0) return msg;
                }

                m = Regex.Match(original, @"registrar(?:\s+un|\s+una)?(?:\s+evento|\s+informativo|\s+advertencia)?\s+(?<msg>.*?)(?:\s+y\s+finalizar|\s+finalizar|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    string msg = CleanOutcomeMessage(m.Groups["msg"].Value);
                    if (msg.Length > 0) return msg;
                }
            }

            return "";
        }

        private static string ExtractOutcomeNotifyRole(string normalizedClause, WfAiCatalog catalog)
        {
            string c = Normalize(normalizedClause);
            if (string.IsNullOrWhiteSpace(c)) return "";
            if (!ContainsAny(c, "notificar", "avisar", "notificacion", "notificación")) return "";

            Match m = Regex.Match(c,
                @"\b(?:notificar|avisar)\s+(?:internamente\s+|por\s+sistema\s+|en\s+el\s+sistema\s+)?(?:(?:al|a\s+la|a|para\s+el|para\s+la)\s+)(?:(?:rol|sector|area|área|usuario)\s+)?(?<dest>.+?)(?=\s+indicando\b|\s+que\b|\s+y\s+finalizar\b|\s+finalizar\b|,|\.|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!m.Success) return "";

            string dest = NormalizeNotifyDestinationText(CleanExtractedSentence(m.Groups["dest"].Value));
            string resolved = ResolveRole(Normalize(dest), catalog);
            return string.IsNullOrWhiteSpace(resolved) ? dest : resolved;
        }

        private static string CleanOutcomeMessage(string value)
        {
            string msg = (value ?? "").Trim();
            msg = Regex.Replace(msg, @"\s+", " ").Trim();
            msg = Regex.Replace(msg, @"^(que\s+)+", "", RegexOptions.IgnoreCase).Trim();
            msg = Regex.Replace(msg, @"(?:,?\s*)\b(?:notificar|avisar)\s+(?:al|a\s+la|a|para\s+el|para\s+la)\s+.+$", "", RegexOptions.IgnoreCase).Trim();
            msg = msg.Trim(' ', '.', ',', ';', ':');
            return msg;
        }

        private static void AddHumanTaskOutcomeActions(JArray actions, HumanTaskOutcomeRequest request)
        {
            if (actions == null || request == null || !request.IsDetected || string.IsNullOrWhiteSpace(request.TaskRole)) return;

            string role = request.TaskRole.Trim();
            string friendly = RoleFriendlyName(role);

            bool hasResultIf = false;
            string expectedLabel = Normalize("Resultado de " + role + " aprobado").Trim();
            string expectedFriendlyLabel = Normalize("Resultado de " + friendly + " aprobado").Trim();
            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["field"] ?? "").Trim(), "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;

                string label = Normalize(ActionLabel(a)).Trim();
                if (string.Equals(label, expectedLabel, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, expectedFriendlyLabel, StringComparison.OrdinalIgnoreCase)
                    || ContainsPhrase(label, Normalize(role).Trim())
                    || ContainsPhrase(label, Normalize(friendly).Trim()))
                {
                    hasResultIf = true;
                    break;
                }
            }

            if (!hasResultIf)
            {
                actions.Add(AddNode("control.if", "Resultado de " + role + " aprobado", new JObject
                {
                    ["field"] = "wf.tarea.resultado",
                    ["op"] = "==",
                    ["value"] = "apto"
                }));
            }

            if (request.HasApprovedBranch)
            {
                if (request.ApprovedWantsLogger)
                {
                    actions.Add(AddNode("util.logger", "Registrar aprobación de " + role, new JObject
                    {
                        ["level"] = "Info",
                        ["message"] = request.ApprovedMessage ?? ("Tarea de " + friendly + " aprobada.")
                    }));
                }

                if (!string.IsNullOrWhiteSpace(request.ApprovedNotifyRole))
                    AddOutcomeNotifyAction(actions, "Notificar aprobación de " + role + " a " + request.ApprovedNotifyRole, request.ApprovedNotifyRole, request.ApprovedMessage);
            }

            if (request.HasRejectedBranch)
            {
                if (request.RejectedWantsLogger)
                {
                    actions.Add(AddNode("util.logger", "Registrar rechazo de " + role, new JObject
                    {
                        ["level"] = "Warn",
                        ["message"] = request.RejectedMessage ?? ("Tarea de " + friendly + " rechazada.")
                    }));
                }

                if (!string.IsNullOrWhiteSpace(request.RejectedNotifyRole))
                    AddOutcomeNotifyAction(actions, "Notificar rechazo de " + role + " a " + request.RejectedNotifyRole, request.RejectedNotifyRole, request.RejectedMessage);
            }
        }

        private static NaturalCompositeConditionRequest AnalyzeNaturalCompositeCondition(string userText, string normalizedText, WfAiCatalog catalog, string prefix, string amount)
        {
            var request = new NaturalCompositeConditionRequest();
            string t = Normalize(string.IsNullOrWhiteSpace(userText) ? normalizedText : userText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool explicitComposite = ContainsAny(t,
                "condicion compuesta", "condición compuesta",
                "cualquiera de las reglas", "cualquier regla", "al menos una regla", "una de las reglas",
                "todas las reglas", "cada regla",
                "modo cualquiera", "modo any", "modo or", "modo o", "modo todas", "modo all", "modo and", "modo y");

            bool hasOrBetweenKnownRules = Regex.IsMatch(t, @"\b(cae|cai)\b.*\bo\b.*\b(comprobante\s+asociado|asociado|total|items|item|itemscount)\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(t, @"\b(comprobante\s+asociado|asociado)\b.*\bo\b.*\b(cae|cai|total|items|item|itemscount)\b", RegexOptions.IgnoreCase);

            bool hasAndBetweenKnownRules = Regex.IsMatch(t, @"\b(cae|cai|comprobante\s+asociado|asociado|total|items|item|itemscount)\b.*\by\b.*\b(cae|cai|comprobante\s+asociado|asociado|total|items|item|itemscount)\b", RegexOptions.IgnoreCase);

            // fix47: frases humanas tipo
            // "Si tiene CAE, el total es mayor a 200000 y tiene al menos un item..."
            // no dicen literalmente "condición compuesta". Detectamos composición por cantidad
            // de campos de negocio mencionados en la misma condición.
            bool hasMultipleKnownRules = CountKnownNaturalConditionRuleKinds(t) >= 2;

            if (!explicitComposite && !hasOrBetweenKnownRules && !hasAndBetweenKnownRules && !hasMultipleKnownRules)
                return request;

            string contextPrefix = ResolveNaturalConditionPrefix(prefix, t);
            request.RulesMode = ResolveNaturalRulesMode(t);
            request.Rules.AddRange(ExtractNaturalCompositeRules(t, contextPrefix, amount));
            ForceNaturalMissingOperators(t, request.Rules);
            request.TrueRole = ExtractNaturalCompositeBranchRole(t, catalog, true);
            request.FalseRole = ExtractNaturalCompositeBranchRole(t, catalog, false);

            request.IsDetected = request.Rules.Count > 0;
            return request;
        }

        private static void ForceNaturalMissingOperators(string normalizedText, List<NaturalConditionRule> rules)
        {
            if (rules == null || rules.Count == 0) return;

            string t = Normalize(normalizedText);

            bool caeMissing = ContainsAny(t,
                "no tiene cae", "no tenga cae", "no posee cae", "sin cae",
                "falta cae", "falta el cae", "falta la cae",
                "cae faltante", "cae vacio", "cae vacío", "cae en blanco",
                "cae no informado", "cae sin informar");

            bool comprobanteMissing = ContainsAny(t,
                "no tiene comprobante asociado", "no tenga comprobante asociado", "sin comprobante asociado",
                "falta comprobante asociado", "falta el comprobante asociado",
                "comprobante asociado faltante", "comprobante asociado vacio", "comprobante asociado vacío",
                "comprobante asociado en blanco", "comprobante asociado no informado", "comprobante asociado sin informar",
                "no tiene numero asociado", "no tiene número asociado", "sin numero asociado", "sin número asociado",
                "falta numero asociado", "falta número asociado", "falta el numero asociado", "falta el número asociado",
                "numero asociado faltante", "número asociado faltante");

            foreach (NaturalConditionRule rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Field)) continue;

                string f = rule.Field.Trim();

                if (caeMissing && Regex.IsMatch(f, @"(^|\.)cae$", RegexOptions.IgnoreCase))
                {
                    rule.Op = "empty";
                    rule.Value = "";
                    continue;
                }

                if (comprobanteMissing && Regex.IsMatch(f, @"comprobanteAsociado\.numero$", RegexOptions.IgnoreCase))
                {
                    rule.Op = "empty";
                    rule.Value = "";
                    continue;
                }
            }
        }

        private static string ResolveNaturalRulesMode(string normalizedText)
        {
            string t = Normalize(normalizedText);

            if (ContainsAny(t, "cualquiera", "cualquier regla", "al menos una", "una de las reglas", "modo any", "modo or", "modo o")
                || Regex.IsMatch(t, @"\bo\b", RegexOptions.IgnoreCase))
                return "any";

            return "all";
        }

        private static string ResolveNaturalConditionPrefix(string prefix, string normalizedText)
        {
            if (!string.IsNullOrWhiteSpace(prefix)) return prefix.Trim();

            string t = Normalize(normalizedText);
            if (ContainsAny(t, "nota de credito", "nota credito", "nc", "cae", "comprobante asociado"))
                return "notaCredito";

            return "";
        }

        private static List<NaturalConditionRule> ExtractNaturalCompositeRules(string normalizedText, string prefix, string amount)
        {
            var rules = new List<NaturalConditionRule>();
            string t = Normalize(normalizedText);
            string basePath = string.IsNullOrWhiteSpace(prefix) ? "biz" : "biz." + prefix.Trim();

            if (ContainsAny(t, "cae_prueba_faltante", "cae prueba faltante"))
                AddNaturalRule(rules, basePath + ".cae_prueba_faltante", ResolveNaturalPresenceOperator(t, "cae"), "", "CAE prueba faltante");
            else if (ContainsToken(t, "cae") || ContainsToken(t, "cai"))
                AddNaturalRule(rules, basePath + ".cae", ResolveNaturalPresenceOperator(t, "cae"), "", "CAE");

            if (ContainsAny(t, "comprobante asociado", "comprobante asociada", "asociado", "comprobanteAsociado", "comprobante asociado numero", "comprobante asociado número"))
                AddNaturalRule(rules, basePath + ".comprobanteAsociado.numero", ResolveNaturalPresenceOperator(t, "comprobanteAsociado"), "", "Comprobante asociado");

            if (ContainsAny(t,
                "itemscount", "items count",
                "cantidad de items", "cantidad de ítems", "cantidad de item", "cantidad de ítem",
                "al menos un item", "al menos un ítem", "un item", "un ítem",
                "items", "ítems", "item", "ítem"))
                AddNaturalRule(rules, basePath + ".itemsCount", ">", "0", "Items");

            if (ContainsToken(t, "total"))
            {
                string op = ">";
                if (ContainsAny(t, "menor o igual", "menor igual", "no supera", "no mayor")) op = "<=";
                else if (ContainsAny(t, "menor que", "menor a")) op = "<";
                else if (ContainsAny(t, "mayor o igual", "mayor igual")) op = ">=";
                else if (ContainsAny(t, "igual a", "es igual")) op = "==";

                string value = string.IsNullOrWhiteSpace(amount) ? ExtractAmount(t) : amount;
                if (!string.IsNullOrWhiteSpace(value))
                    AddNaturalRule(rules, basePath + ".total", op, value, "Total");
            }

            return rules;
        }

        private static string ResolveNaturalPresenceOperator(string normalizedText, string fieldKind)
        {
            string t = Normalize(normalizedText);
            string k = Normalize(fieldKind);

            if (k == "cae" || k == "cai")
            {
                if (ContainsAny(t,
                    "no tiene cae", "no tenga cae", "no posee cae", "sin cae",
                    "falta cae", "falta el cae", "falta la cae", "si falta cae", "si falta el cae",
                    "cae faltante", "cae vacio", "cae vacío", "cae en blanco", "cae no informado", "cae sin informar"))
                    return "empty";

                return "not_empty";
            }

            if (k == "comprobanteasociado" || k == "asociado" || k == "numeroasociado")
            {
                if (ContainsAny(t,
                    "no tiene comprobante asociado", "no tenga comprobante asociado", "sin comprobante asociado",
                    "falta comprobante asociado", "falta el comprobante asociado", "comprobante asociado faltante",
                    "comprobante asociado vacio", "comprobante asociado vacío", "comprobante asociado en blanco",
                    "comprobante asociado no informado", "comprobante asociado sin informar",
                    "no tiene numero asociado", "no tiene número asociado", "sin numero asociado", "sin número asociado",
                    "falta numero asociado", "falta número asociado", "falta el numero asociado", "falta el número asociado",
                    "numero asociado faltante", "número asociado faltante"))
                    return "empty";

                return "not_empty";
            }

            return "not_empty";
        }

        private static void AddNaturalRule(List<NaturalConditionRule> rules, string field, string op, string value, string label)
        {
            if (rules == null || string.IsNullOrWhiteSpace(field)) return;

            foreach (NaturalConditionRule r in rules)
            {
                if (string.Equals(r.Field, field, StringComparison.OrdinalIgnoreCase)) return;
            }

            rules.Add(new NaturalConditionRule
            {
                Field = field,
                Op = string.IsNullOrWhiteSpace(op) ? "not_empty" : op,
                Value = value ?? "",
                Label = label ?? ""
            });
        }

        private static string ExtractNaturalCompositeBranchRole(string normalizedText, WfAiCatalog catalog, bool trueBranch)
        {
            string pending = "";
            foreach (string clauseRaw in SplitClausesForBranches(normalizedText))
            {
                string c = Normalize(clauseRaw);
                if (string.IsNullOrWhiteSpace(c)) continue;

                bool trueMarker = ContainsAny(c, "si cumple", "si se cumple", "si la condicion cumple", "si cumple la condicion", "si es verdadero", "si da true", "rama si", "si cumple va", "si cumple enviar");
                bool falseMarker = ContainsAny(c, "si no cumple", "si no se cumple", "si la condicion no cumple", "si no cumple la condicion", "si es falso", "si da false", "rama no", "caso contrario", "de lo contrario");
                bool naturalMissingConditionMarker = IsNaturalMissingConditionClause(c);
                bool naturalPositiveConditionMarker = IsNaturalPositiveConditionClause(c);

                string role = ResolvePositiveDestinationRole(c, catalog);
                if (string.IsNullOrWhiteSpace(role)) role = ResolveRole(c, catalog);

                if (trueBranch && (trueMarker || naturalMissingConditionMarker || naturalPositiveConditionMarker))
                {
                    if (!string.IsNullOrWhiteSpace(role)) return role;
                    pending = "true";
                    continue;
                }

                if (!trueBranch && falseMarker)
                {
                    if (!string.IsNullOrWhiteSpace(role)) return role;
                    pending = "false";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(role))
                {
                    if (trueBranch && string.Equals(pending, "true", StringComparison.OrdinalIgnoreCase)) return role;
                    if (!trueBranch && string.Equals(pending, "false", StringComparison.OrdinalIgnoreCase)) return role;
                }
            }

            return "";
        }

        private static bool IsNaturalMissingConditionClause(string normalizedClause)
        {
            string c = Normalize(normalizedClause);
            if (string.IsNullOrWhiteSpace(c)) return false;

            return ContainsAny(c,
                "si no tiene cae", "si no tenga cae", "si no posee cae", "si falta cae", "si falta el cae", "si esta sin cae", "si está sin cae",
                "si no tiene comprobante asociado", "si no tenga comprobante asociado", "si falta comprobante asociado", "si falta el comprobante asociado",
                "si esta sin comprobante asociado", "si está sin comprobante asociado",
                "si no tiene numero asociado", "si no tiene número asociado", "si falta numero asociado", "si falta número asociado",
                "si esta sin numero asociado", "si está sin número asociado",
                "no tiene cae", "falta el cae", "sin cae",
                "no tiene comprobante asociado", "falta el comprobante asociado", "sin comprobante asociado",
                "no tiene numero asociado", "no tiene número asociado", "falta el numero asociado", "falta el número asociado", "sin numero asociado", "sin número asociado");
        }

        private static bool IsNaturalPositiveConditionClause(string normalizedClause)
        {
            string c = Normalize(normalizedClause);
            if (string.IsNullOrWhiteSpace(c)) return false;

            if (!ContainsAny(c, "si ", "cuando ")) return false;

            bool mentionsKnownField = ContainsAny(c,
                "cae", "cai", "comprobante asociado", "numero asociado", "número asociado",
                "total", "importe", "items", "ítems", "item", "ítem", "itemscount");
            if (!mentionsKnownField) return false;

            if (IsNaturalMissingConditionClause(c)) return false;

            return ContainsAny(c,
                "tiene cae", "cae informado", "cae no esta vacio", "cae no está vacío",
                "tiene comprobante asociado", "comprobante asociado informado",
                "total mayor", "importe mayor", "mayor a", "mayor que",
                "tiene items", "tiene ítems", "tiene item", "tiene ítem",
                "al menos un item", "al menos un ítem");
        }

        private static int CountKnownNaturalConditionRuleKinds(string normalizedText)
        {
            string t = Normalize(normalizedText);
            int count = 0;

            if (ContainsToken(t, "cae") || ContainsToken(t, "cai")) count++;
            if (ContainsAny(t, "comprobante asociado", "numero asociado", "número asociado")) count++;
            if (ContainsToken(t, "total") || ContainsToken(t, "importe")) count++;
            if (ContainsAny(t, "itemscount", "items count", "cantidad de items", "cantidad de ítems", "cantidad de item", "cantidad de ítem", "al menos un item", "al menos un ítem", "items", "ítems", "item", "ítem")) count++;

            return count;
        }

        private static void AddNaturalCompositeConditionAction(JArray actions, JArray missing, NaturalCompositeConditionRequest request)
        {
            if (actions == null || request == null || request.Rules == null || request.Rules.Count == 0) return;

            var rules = new JArray();
            foreach (NaturalConditionRule rule in request.Rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Field)) continue;

                var item = new JObject
                {
                    ["field"] = rule.Field,
                    ["op"] = string.IsNullOrWhiteSpace(rule.Op) ? "not_empty" : rule.Op
                };

                if (GuidedOperatorNeedsValue(rule.Op))
                    item["value"] = rule.Value ?? "";

                rules.Add(item);
            }

            if (rules.Count == 0) return;

            var p = new JObject
            {
                ["rulesMode"] = string.Equals(request.RulesMode, "any", StringComparison.OrdinalIgnoreCase) ? "any" : "all",
                ["rules"] = rules
            };

            actions.Add(AddNode("control.if", NaturalCompositeConditionLabel(request), p));
        }

        private static string NaturalCompositeConditionLabel(NaturalCompositeConditionRequest request)
        {
            string mode = request != null && string.Equals(request.RulesMode, "any", StringComparison.OrdinalIgnoreCase)
                ? "cualquiera"
                : "todas";

            return "Condición compuesta: " + mode + " de las reglas";
        }

        private static List<GuidedConditionRequest> AnalyzeGuidedConditions(string userText)
        {
            var result = new List<GuidedConditionRequest>();
            if (string.IsNullOrWhiteSpace(userText)) return result;

            // Frases generadas por el constructor fix24:
            // "validar el campo biz.notaCredito.total con operador > valor 100000"
            var re = new Regex(
                @"validar\s+el\s+campo\s+(?<field>[A-Za-z0-9_\.\[\]<>]+)\s+con\s+operador\s+(?<op>>=|<=|!=|==|>|<|=|not_empty|empty|contains|not_contains|true|false)(?:\s+valor\s+(?<value>.*?))?(?=\s*,|\s+luego\s+|\s+si\s+|\s*\.\s*$|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in re.Matches(userText))
            {
                string field = (m.Groups["field"].Value ?? "").Trim();
                string opRaw = (m.Groups["op"].Value ?? "").Trim();
                string value = m.Groups["value"].Success ? (m.Groups["value"].Value ?? "").Trim() : "";
                if (field.Length == 0) continue;

                string op = NormalizeGuidedOperator(opRaw, ref value);
                result.Add(new GuidedConditionRequest
                {
                    Field = field,
                    Op = op,
                    Value = value,
                    Label = FriendlyFieldName(field)
                });
            }

            return result;
        }

        private static string NormalizeGuidedOperator(string opRaw, ref string value)
        {
            string op = (opRaw ?? "").Trim().ToLowerInvariant();
            if (op == "=") return "==";
            if (op == "true")
            {
                value = "true";
                return "==";
            }
            if (op == "false")
            {
                value = "false";
                return "==";
            }
            if (op == "not_empty" || op == "empty" || op == "contains" || op == "not_contains") return op;
            if (op == "==" || op == "!=" || op == ">" || op == "<" || op == ">=" || op == "<=") return op;
            return "not_empty";
        }

        private static void AddGuidedConditionAction(JArray actions, JArray missing, GuidedConditionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Field)) return;

            var p = new JObject
            {
                ["field"] = request.Field,
                ["op"] = request.Op
            };

            if (GuidedOperatorNeedsValue(request.Op))
                p["value"] = request.Value ?? "";

            actions.Add(AddNode("control.if", GuidedConditionLabel(request), p));

            if (GuidedOperatorNeedsValue(request.Op) && string.IsNullOrWhiteSpace(request.Value))
            {
                missing.Add(new JObject
                {
                    ["key"] = "valorCondicion",
                    ["question"] = "Falta indicar el valor para validar el campo " + request.Field + "."
                });
            }
        }

        private static bool GuidedOperatorNeedsValue(string op)
        {
            string o = (op ?? "").Trim().ToLowerInvariant();
            return !(o == "not_empty" || o == "empty" || o == "exists" || o == "not_exists");
        }

        private static string GuidedConditionLabel(GuidedConditionRequest request)
        {
            if (request == null) return "Validar condición";
            string label = string.IsNullOrWhiteSpace(request.Label) ? request.Field : request.Label;
            string op = request.Op ?? "";
            string value = request.Value ?? "";

            if (op == "not_empty") return "Validar " + label + " informado";
            if (op == "empty") return "Validar " + label + " vacío";
            if (GuidedOperatorNeedsValue(op) && !string.IsNullOrWhiteSpace(value))
                return "Validar " + label + " " + op + " " + value;
            return "Validar " + label;
        }

        private static string FriendlyFieldName(string field)
        {
            string f = (field ?? "").Trim();
            if (f.Length == 0) return "campo";
            int idx = f.LastIndexOf('.');
            string name = idx >= 0 && idx < f.Length - 1 ? f.Substring(idx + 1) : f;
            name = name.Replace("_", " ").Replace("[]", "");
            return name;
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

        private static void AddStateVarsAction(JArray actions, StateVarsRequest request)
        {
            if (actions == null || request == null || !request.HasChanges) return;

            var p = new JObject();
            if (request.Set.Count > 0)
            {
                var set = new JObject();
                foreach (var kv in request.Set)
                    set[kv.Key] = kv.Value == null ? JValue.CreateNull() : JToken.FromObject(kv.Value);
                p["set"] = set;
            }

            if (request.Remove.Count > 0)
                p["remove"] = JArray.FromObject(request.Remove);

            actions.Add(AddNode("state.vars", "Definir variables", p));
        }

        private static void AddDelayAction(JArray actions, DelayRequest request)
        {
            if (actions == null || request == null || !request.WantsDelay) return;

            var p = new JObject
            {
                ["message"] = "Demora agregada por Asistente IA"
            };

            if (request.Milliseconds > 0)
                p["ms"] = request.Milliseconds;
            else if (!string.IsNullOrWhiteSpace(request.Seconds))
                p["seconds"] = request.Seconds;

            actions.Add(AddNode("control.delay", "Demora", p));
        }

        private static JObject AddOutcomeNotifyAction(JArray actions, string label, string roleDestino, string message)
        {
            if (actions == null) return null;

            string role = (roleDestino ?? "").Trim();
            if (role.Length == 0) return null;

            var p = new JObject
            {
                ["tipo"] = "sistema",
                ["canal"] = "sistema",
                ["nivel"] = "info",
                ["destinoTipo"] = "rol",
                ["usuarioDestino"] = "",
                ["rolDestino"] = role,
                ["destino"] = role,
                ["prioridad"] = "normal",
                ["asunto"] = "Notificación Workflow Studio",
                ["mensaje"] = string.IsNullOrWhiteSpace(message) ? "Notificación generada por Asistente IA." : message
            };

            JObject node = AddNode("util.notify", string.IsNullOrWhiteSpace(label) ? "Notificar" : label, p);
            actions.Add(node);
            return node;
        }

        private static void AddNotifyAction(JArray actions, NotifyRequest request, WfAiCatalog catalog)
        {
            if (actions == null || request == null || !request.WantsNotify) return;

            string destino = request.Destination ?? "";
            string destinoTipo = "usuarioActual";
            string usuarioDestino = "";
            string rolDestino = "";

            if (!string.IsNullOrWhiteSpace(destino))
            {
                string destinoTrim = NormalizeNotifyDestinationText(destino);
                if (destinoTrim.Contains("\\") || destinoTrim.Contains("@"))
                {
                    destinoTipo = "usuario";
                    usuarioDestino = destinoTrim;
                }
                else
                {
                    destinoTipo = "rol";
                    string resolvedRole = ResolveRole(Normalize(destinoTrim), catalog);
                    rolDestino = string.IsNullOrWhiteSpace(resolvedRole) ? destinoTrim : resolvedRole;
                }
            }

            var p = new JObject
            {
                ["tipo"] = "sistema",
                ["canal"] = "sistema",
                ["nivel"] = "info",
                ["destinoTipo"] = destinoTipo,
                ["usuarioDestino"] = usuarioDestino,
                ["rolDestino"] = rolDestino,
                ["destino"] = usuarioDestino.Length > 0 ? usuarioDestino : rolDestino,
                ["prioridad"] = "normal",
                ["asunto"] = request.Title ?? "Notificación Workflow Studio",
                ["mensaje"] = request.Message ?? "Notificación generada por Asistente IA."
            };

            actions.Add(AddNode("util.notify", "Notificar", p));
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

            JObject compoundIf = FindCompoundConditionAction(actions);
            JObject caeIf = FindActionByLabel(actions, "control.if", "Validar CAE informado");
            JObject totalIf = FindActionLabelStarts(actions, "control.if", "Total mayor a ");
            JObject logger = FirstAction(actions, "util.logger");
            JObject end = FirstAction(actions, "util.end");
            JObject firstCondition = compoundIf ?? caeIf ?? totalIf;

            if (firstCondition == null)
            {
                AddSequentialConnections(result, actions);
                return result;
            }

            AddSequentialConnectionsUntil(result, actions, firstCondition);

            if (compoundIf != null)
            {
                JObject trueTarget = FindActionForPath(actions, GetBranchPath(branchPlan, "compound", "truePath"));
                JObject falseTarget = FindActionForPath(actions, GetBranchPath(branchPlan, "compound", "falsePath"));

                AddConnection(result, compoundIf, trueTarget, "SI");
                AddConnection(result, compoundIf, falseTarget, "NO");
            }

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

            List<HumanTaskOutcomeConnection> outcomeConnections = FindHumanTaskOutcomeConnections(actions);
            if (outcomeConnections != null && outcomeConnections.Count > 0)
            {
                // fix48: los resultados de tareas humanas pueden estar en ramas de IF compuesto,
                // CAE o importe. Conectamos la tarea con su IF de resultado y solo mandamos a Fin
                // las ramas terminales que no tienen evaluación humana propia.
                var outcomeTasks = new List<JObject>();

                foreach (HumanTaskOutcomeConnection outcomeConnection in outcomeConnections)
                {
                    if (outcomeConnection == null || !outcomeConnection.IsDetected) continue;

                    JObject outcomeTask = FindHumanTaskByRole(actions, outcomeConnection.TaskRole);
                    if (outcomeTask == null) continue;

                    AddUniqueAction(outcomeTasks, outcomeTask);
                    AddConnection(result, outcomeTask, outcomeConnection.ResultIf, "");

                    AddOutcomeBranchConnections(result, outcomeConnection.ResultIf, outcomeConnection.ApprovedAction, outcomeConnection.ApprovedFollowUpAction, end, "SI");
                    AddOutcomeBranchConnections(result, outcomeConnection.ResultIf, outcomeConnection.RejectedAction, outcomeConnection.RejectedFollowUpAction, end, "NO");
                }

                List<JObject> terminals = FindBranchTerminalActions(actions, branchPlan);
                foreach (JObject terminal in terminals)
                {
                    if (terminal == null) continue;

                    bool terminalHasOutcome = false;
                    foreach (JObject taskWithOutcome in outcomeTasks)
                    {
                        if (object.ReferenceEquals(terminal, taskWithOutcome))
                        {
                            terminalHasOutcome = true;
                            break;
                        }
                    }

                    if (!terminalHasOutcome)
                        AddConnection(result, terminal, end, "");
                }

                return result;
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

        private static void AddOutcomeBranchConnections(JArray result, JObject resultIf, JObject firstAction, JObject followUpAction, JObject end, string condition)
        {
            JObject target = firstAction ?? followUpAction ?? end;
            AddConnection(result, resultIf, target, condition);

            if (firstAction != null && followUpAction != null)
            {
                AddConnection(result, firstAction, followUpAction, "");
                AddConnection(result, followUpAction, end, "");
            }
            else if (target != null && !object.ReferenceEquals(target, end))
            {
                AddConnection(result, target, end, "");
            }
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

        private static List<HumanTaskOutcomeConnection> FindHumanTaskOutcomeConnections(JArray actions)
        {
            var list = new List<HumanTaskOutcomeConnection>();
            if (actions == null) return list;

            foreach (JToken token in actions)
            {
                JObject resultIf = token as JObject;
                if (resultIf == null || !IsAddNode(resultIf)) continue;
                if (!string.Equals(ActionNodeType(resultIf), "control.if", StringComparison.OrdinalIgnoreCase)) continue;

                JObject p = resultIf["params"] as JObject;
                if (p == null) continue;

                string field = Convert.ToString(p["field"] ?? "").Trim();
                string op = Convert.ToString(p["op"] ?? "").Trim();
                string value = Convert.ToString(p["value"] ?? "").Trim();

                if (!string.Equals(field, "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)
                    || !(string.Equals(op, "==", StringComparison.OrdinalIgnoreCase) || string.Equals(op, "=", StringComparison.OrdinalIgnoreCase))
                    || !string.Equals(value, "apto", StringComparison.OrdinalIgnoreCase))
                    continue;

                string role = ResolveRoleFromTaskResultAction(actions, resultIf);
                if (string.IsNullOrWhiteSpace(role)) continue;

                list.Add(new HumanTaskOutcomeConnection
                {
                    IsDetected = true,
                    TaskRole = role,
                    ResultIf = resultIf,
                    ApprovedAction = FindLoggerForOutcome(actions, role, true),
                    RejectedAction = FindLoggerForOutcome(actions, role, false),
                    ApprovedFollowUpAction = FindNotifyForOutcome(actions, role, true),
                    RejectedFollowUpAction = FindNotifyForOutcome(actions, role, false)
                });
            }

            return list;
        }

        private static HumanTaskOutcomeConnection FindHumanTaskOutcomeConnection(JArray actions)
        {
            if (actions == null) return null;

            JObject resultIf = null;
            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "control.if", StringComparison.OrdinalIgnoreCase)) continue;

                JObject p = a["params"] as JObject;
                if (p == null) continue;

                string field = Convert.ToString(p["field"] ?? "").Trim();
                string op = Convert.ToString(p["op"] ?? "").Trim();
                string value = Convert.ToString(p["value"] ?? "").Trim();

                if (string.Equals(field, "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(op, "==", StringComparison.OrdinalIgnoreCase) || string.Equals(op, "=", StringComparison.OrdinalIgnoreCase))
                    && string.Equals(value, "apto", StringComparison.OrdinalIgnoreCase))
                {
                    resultIf = a;
                    break;
                }
            }

            if (resultIf == null) return null;

            string role = ResolveRoleFromTaskResultAction(actions, resultIf);
            if (string.IsNullOrWhiteSpace(role)) return null;

            return new HumanTaskOutcomeConnection
            {
                IsDetected = true,
                TaskRole = role,
                ResultIf = resultIf,
                ApprovedAction = FindLoggerForOutcome(actions, role, true),
                RejectedAction = FindLoggerForOutcome(actions, role, false),
                ApprovedFollowUpAction = FindNotifyForOutcome(actions, role, true),
                RejectedFollowUpAction = FindNotifyForOutcome(actions, role, false)
            };
        }

        private static string ResolveRoleFromTaskResultAction(JArray actions, JObject resultIf)
        {
            string label = Normalize(ActionLabel(resultIf));
            if (actions == null || string.IsNullOrWhiteSpace(label)) return "";

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "human.task", StringComparison.OrdinalIgnoreCase)) continue;

                JObject p = a["params"] as JObject;
                string role = p == null ? "" : Convert.ToString(p["rol"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(role)) continue;

                string roleNorm = Normalize(role).Trim();
                string friendlyNorm = Normalize(RoleFriendlyName(role)).Trim();

                if (roleNorm.Length > 0 && ContainsPhrase(label, roleNorm)) return role;
                if (friendlyNorm.Length > 0 && ContainsPhrase(label, friendlyNorm)) return role;
            }

            return "";
        }

        private static JObject FindLoggerForOutcome(JArray actions, string role, bool approved)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return null;

            string prefix = approved ? "registrar aprobacion de " : "registrar rechazo de ";
            string prefixNormRole = Normalize(prefix + role).Trim();
            string prefixNormFriendly = Normalize(prefix + RoleFriendlyName(role)).Trim();

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "util.logger", StringComparison.OrdinalIgnoreCase)) continue;

                string label = Normalize(ActionLabel(a)).Trim();
                if (ContainsPhrase(label, prefixNormRole) || ContainsPhrase(label, prefixNormFriendly))
                    return a;
            }

            return null;
        }

        private static JObject FindNotifyForOutcome(JArray actions, string role, bool approved)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return null;

            string prefix = approved ? "notificar aprobacion de " : "notificar rechazo de ";
            string prefixNormRole = Normalize(prefix + role).Trim();
            string prefixNormFriendly = Normalize(prefix + RoleFriendlyName(role)).Trim();

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "util.notify", StringComparison.OrdinalIgnoreCase)) continue;

                string label = Normalize(ActionLabel(a)).Trim();
                if (ContainsPhrase(label, prefixNormRole) || ContainsPhrase(label, prefixNormFriendly))
                    return a;
            }

            return null;
        }

        private static List<JObject> FindBranchTerminalActions(JArray actions, JObject branchPlan)
        {
            var list = new List<JObject>();
            AddUniqueAction(list, FindActionForPath(actions, GetBranchPath(branchPlan, "compound", "truePath")));
            AddUniqueAction(list, FindActionForPath(actions, GetBranchPath(branchPlan, "compound", "falsePath")));
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

        private static JObject FindCompoundConditionAction(JArray actions)
        {
            if (actions == null) return null;

            foreach (JToken token in actions)
            {
                JObject a = token as JObject;
                if (a == null || !IsAddNode(a)) continue;
                if (!string.Equals(ActionNodeType(a), "control.if", StringComparison.OrdinalIgnoreCase)) continue;

                JObject p = a["params"] as JObject;
                if (p == null) continue;
                JArray rules = p["rules"] as JArray;
                if (rules != null && rules.Count > 0) return a;
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

        private static string BuildMessage(JArray actions, string docTipo, string role, string userKey, string amount, BranchAnalysis branches, NaturalCompositeConditionRequest naturalCompositeCondition)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(docTipo)) parts.Add("documento " + docTipo);
            if (!string.IsNullOrWhiteSpace(amount)) parts.Add("condición por total mayor a " + amount);
            if (naturalCompositeCondition != null && naturalCompositeCondition.IsDetected)
                parts.Add("condición compuesta " + (string.Equals(naturalCompositeCondition.RulesMode, "any", StringComparison.OrdinalIgnoreCase) ? "ANY / cualquiera" : "ALL / todas"));

            if (branches != null)
            {
                if (!string.IsNullOrWhiteSpace(branches.CaeFalseRole)) parts.Add("si falta CAE derivar a " + branches.CaeFalseRole);
                if (!string.IsNullOrWhiteSpace(branches.TotalTrueRole)) parts.Add("si supera el total derivar a " + branches.TotalTrueRole);
                if (!string.IsNullOrWhiteSpace(branches.TotalFalseRole)) parts.Add("si no supera el total derivar a " + branches.TotalFalseRole);
            }

            if (FirstAction(actions, "http.request") != null) parts.Add("solicitud HTTP");
            if (FirstAction(actions, "data.sql") != null) parts.Add("consulta SQL");
            if (FirstAction(actions, "file.write") != null) parts.Add("escritura de archivo");
            if (FirstAction(actions, "file.read") != null) parts.Add("lectura de archivo");
            if (FirstAction(actions, "queue.publish") != null) parts.Add("publicación en cola");
            if (FirstAction(actions, "queue.consume") != null) parts.Add("consumo de cola");
            if (FirstAction(actions, "state.vars") != null) parts.Add("definición de variables");
            if (FirstAction(actions, "control.delay") != null) parts.Add("demora controlada");
            if (FirstAction(actions, "util.notify") != null) parts.Add("notificación interna");
            if (FirstAction(actions, "email.send") != null) parts.Add("envío de correo");
            if (!string.IsNullOrWhiteSpace(role) && (branches == null || !branches.HasBranchInfo) && FirstAction(actions, "human.task") != null) parts.Add("derivación al rol " + role);
            if (!string.IsNullOrWhiteSpace(userKey) && FirstAction(actions, "human.task") != null) parts.Add("derivación al usuario " + userKey);

            if (parts.Count == 0)
                return "Recibí la intención y preparé una propuesta inicial para revisar.";

            return "Preparé una propuesta con " + string.Join(", ", parts.ToArray()) + ".";
        }

        private static HttpRequestRequest AnalyzeHttpRequestRequest(string originalText, string normalizedText)
        {
            var request = new HttpRequestRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "solicitud http", "request http", "http", "api", "endpoint", "servicio rest", "llamada rest");
            if (!wants) return request;

            string method = ExtractHttpMethod(originalText);
            string url = ExtractHttpUrl(originalText);

            request.WantsHttp = true;
            request.Method = string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant();
            request.Url = string.IsNullOrWhiteSpace(url) ? "/Api/Ping.ashx" : url;
            request.Label = "Solicitud HTTP " + request.Method + " " + request.Url;
            request.TimeoutMs = 10000;
            request.FailOnStatus = false;
            request.FailStatusMin = 400;
            return request;
        }

        private static string ExtractHttpMethod(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";
            var m = Regex.Match(originalText, @"\b(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\b", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.ToUpperInvariant() : "";
        }

        private static string ExtractHttpUrl(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:a|url|endpoint|api)\s+(?<url>https?://[^\s,;]+|/[^\s,;]+)",
                RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                m = Regex.Match(originalText, @"(?<url>https?://[^\s,;]+|/[^\s,;]+)", RegexOptions.IgnoreCase);
            }

            if (!m.Success) return "";
            string url = (m.Groups["url"].Value ?? "").Trim();
            while (url.EndsWith(".", StringComparison.Ordinal) || url.EndsWith(",", StringComparison.Ordinal) || url.EndsWith(";", StringComparison.Ordinal))
                url = url.Substring(0, url.Length - 1).Trim();
            return url;
        }

        private static void AddHttpRequestAction(JArray actions, HttpRequestRequest request)
        {
            if (actions == null || request == null || !request.WantsHttp) return;

            actions.Add(AddNode("http.request", string.IsNullOrWhiteSpace(request.Label) ? "Solicitud HTTP" : request.Label, new JObject
            {
                ["method"] = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method,
                ["url"] = string.IsNullOrWhiteSpace(request.Url) ? "/Api/Ping.ashx" : request.Url,
                ["headers"] = new JObject(),
                ["query"] = new JObject(),
                ["body"] = null,
                ["contentType"] = "",
                ["timeoutMs"] = request.TimeoutMs <= 0 ? 10000 : request.TimeoutMs,
                ["failOnStatus"] = request.FailOnStatus,
                ["failStatusMin"] = request.FailStatusMin <= 0 ? 400 : request.FailStatusMin
            }));
        }

        private static SqlRequest AnalyzeSqlRequest(string originalText, string normalizedText)
        {
            var request = new SqlRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "consulta sql", "ejecutar sql", "sql", "select", "base de datos");
            if (!wants) return request;

            request.WantsSql = true;
            request.ConnectionStringName = ExtractConnectionStringName(originalText);
            if (string.IsNullOrWhiteSpace(request.ConnectionStringName)) request.ConnectionStringName = "DefaultConnection";
            request.Query = ExtractSqlQuery(originalText);
            if (string.IsNullOrWhiteSpace(request.Query)) request.Query = "SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;";
            request.Label = "Consulta SQL";
            return request;
        }

        private static string ExtractConnectionStringName(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:connectionstringname|connection string name|conexion|conexión|usar|con)\s+(?<name>[A-Za-z_][A-Za-z0-9_\-]*)",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string value = (m.Groups["name"].Value ?? "").Trim();
                if (!string.Equals(value, "sql", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "una", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "la", StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            if (Regex.IsMatch(originalText, @"\bDefaultConnection\b", RegexOptions.IgnoreCase))
                return "DefaultConnection";

            return "";
        }

        private static string ExtractSqlQuery(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"(?<query>select\b.+?)(?=(?:\.\s|\s+registrar\b|\s+despues\b|\s+después\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            string query = Regex.Replace(m.Groups["query"].Value ?? "", @"\s+", " ").Trim();
            if (query.Length > 0 && !query.EndsWith(";", StringComparison.Ordinal)) query += ";";
            return query;
        }

        private static void AddSqlRequestAction(JArray actions, SqlRequest request)
        {
            if (actions == null || request == null || !request.WantsSql) return;

            actions.Add(AddNode("data.sql", string.IsNullOrWhiteSpace(request.Label) ? "Consulta SQL" : request.Label, new JObject
            {
                ["connectionStringName"] = string.IsNullOrWhiteSpace(request.ConnectionStringName) ? "DefaultConnection" : request.ConnectionStringName,
                ["query"] = string.IsNullOrWhiteSpace(request.Query) ? "SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;" : request.Query,
                ["parameters"] = new JObject()
            }));
        }

        private static FileWriteRequest AnalyzeFileWriteRequest(string originalText, string normalizedText)
        {
            var request = new FileWriteRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "escribir archivo", "guardar archivo", "crear archivo", "generar archivo", "file write", "archivo escribir");
            if (!wants) return request;

            request.WantsFileWrite = true;
            request.Path = ExtractFilePath(originalText);
            if (string.IsNullOrWhiteSpace(request.Path)) request.Path = @"C:\temp\wf_ai_regression.txt";
            request.Content = ExtractFileContent(originalText);
            if (string.IsNullOrWhiteSpace(request.Content)) request.Content = "Contenido generado por Asistente IA";
            request.Overwrite = true;
            request.Label = "Escribir archivo";
            return request;
        }

        private static FileReadRequest AnalyzeFileReadRequest(string originalText, string normalizedText)
        {
            var request = new FileReadRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "leer archivo", "abrir archivo", "file read", "archivo leer");
            if (!wants) return request;

            request.WantsFileRead = true;
            request.Path = ExtractFilePath(originalText);
            if (string.IsNullOrWhiteSpace(request.Path)) request.Path = @"C:\temp\wf_ai_regression.txt";
            request.Salida = "archivo";
            request.AsJson = false;
            request.Label = "Leer archivo";
            return request;
        }

        private static string ExtractFilePath(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"(?<path>[A-Za-z]:\\[^\r\n,;]+?|/[^\r\n,;]+?)(?=(?:\s+con\s+contenido\b|\s+contenido\b|\.\s|\s+registrar\b|\s+despues\b|\s+después\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            return CleanExtractedSentence(m.Groups["path"].Value).Trim();
        }

        private static string ExtractFileContent(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:con\s+contenido|contenido)\s+(?<content>.+?)(?=(?:\.\s|\s+registrar\b|\s+despues\b|\s+después\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            return CleanExtractedSentence(m.Groups["content"].Value);
        }

        private static void AddFileWriteAction(JArray actions, FileWriteRequest request)
        {
            if (actions == null || request == null || !request.WantsFileWrite) return;

            actions.Add(AddNode("file.write", string.IsNullOrWhiteSpace(request.Label) ? "Escribir archivo" : request.Label, new JObject
            {
                ["path"] = string.IsNullOrWhiteSpace(request.Path) ? @"C:\temp\wf_ai_regression.txt" : request.Path,
                ["content"] = string.IsNullOrWhiteSpace(request.Content) ? "Contenido generado por Asistente IA" : request.Content,
                ["encoding"] = "utf-8",
                ["overwrite"] = request.Overwrite
            }));
        }

        private static void AddFileReadAction(JArray actions, FileReadRequest request)
        {
            if (actions == null || request == null || !request.WantsFileRead) return;

            actions.Add(AddNode("file.read", string.IsNullOrWhiteSpace(request.Label) ? "Leer archivo" : request.Label, new JObject
            {
                ["path"] = string.IsNullOrWhiteSpace(request.Path) ? @"C:\temp\wf_ai_regression.txt" : request.Path,
                ["salida"] = string.IsNullOrWhiteSpace(request.Salida) ? "archivo" : request.Salida,
                ["encoding"] = "utf-8",
                ["asJson"] = request.AsJson
            }));
        }

        private static QueuePublishRequest AnalyzeQueuePublishRequest(string originalText, string normalizedText)
        {
            var request = new QueuePublishRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "publicar en cola", "encolar", "queue publish", "mandar a cola", "enviar a cola");
            if (!wants) return request;

            request.WantsQueuePublish = true;
            request.Queue = ExtractQueueName(originalText);
            if (string.IsNullOrWhiteSpace(request.Queue)) request.Queue = "banco-regresion";
            request.Payload = ExtractQueuePayload(originalText);
            if (string.IsNullOrWhiteSpace(request.Payload)) request.Payload = "Mensaje generado por Asistente IA";
            request.Broker = "sql";
            request.ConnectionStringName = "DefaultConnection";
            request.Label = "Publicar en cola " + request.Queue;
            return request;
        }

        private static QueueConsumeRequest AnalyzeQueueConsumeRequest(string originalText, string normalizedText)
        {
            var request = new QueueConsumeRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool wants = ContainsAny(t, "consumir de cola", "leer de cola", "tomar mensaje", "queue consume");
            if (!wants) return request;

            request.WantsQueueConsume = true;
            request.Queue = ExtractQueueName(originalText);
            if (string.IsNullOrWhiteSpace(request.Queue)) request.Queue = "banco-regresion";
            request.Take = ExtractQueueTake(originalText);
            if (request.Take <= 0) request.Take = 1;
            request.Broker = "sql";
            request.ConnectionStringName = "DefaultConnection";
            request.OutputPrefix = "queue.consume";
            request.Label = "Consumir cola " + request.Queue;
            return request;
        }

        private static string ExtractQueueName(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:cola|queue)\s+(?<queue>[A-Za-z0-9_\-\.]+)(?=(?:\s+con\s+payload\b|\s+payload\b|\s+tomando\b|\s+tomar\b|\.\s|\s+registrar\b|\s+despues\b|\s+después\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            return CleanExtractedSentence(m.Groups["queue"].Value);
        }

        private static string ExtractQueuePayload(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\bpayload\s+(?<payload>.+?)(?=(?:\.\s|\s+registrar\b|\s+despues\b|\s+después\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            return CleanExtractedSentence(m.Groups["payload"].Value);
        }

        private static int ExtractQueueTake(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return 0;

            var m = Regex.Match(originalText, @"\b(?:tomando|tomar|take)\s+(?<n>\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0;

            int n;
            return int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        private static void AddQueuePublishAction(JArray actions, QueuePublishRequest request)
        {
            if (actions == null || request == null || !request.WantsQueuePublish) return;

            actions.Add(AddNode("queue.publish", string.IsNullOrWhiteSpace(request.Label) ? "Publicar en cola" : request.Label, new JObject
            {
                ["broker"] = string.IsNullOrWhiteSpace(request.Broker) ? "sql" : request.Broker,
                ["queue"] = string.IsNullOrWhiteSpace(request.Queue) ? "banco-regresion" : request.Queue,
                ["payload"] = string.IsNullOrWhiteSpace(request.Payload) ? "Mensaje generado por Asistente IA" : request.Payload,
                ["connectionStringName"] = string.IsNullOrWhiteSpace(request.ConnectionStringName) ? "DefaultConnection" : request.ConnectionStringName
            }));
        }

        private static void AddQueueConsumeAction(JArray actions, QueueConsumeRequest request)
        {
            if (actions == null || request == null || !request.WantsQueueConsume) return;

            actions.Add(AddNode("queue.consume", string.IsNullOrWhiteSpace(request.Label) ? "Consumir cola" : request.Label, new JObject
            {
                ["broker"] = string.IsNullOrWhiteSpace(request.Broker) ? "sql" : request.Broker,
                ["queue"] = string.IsNullOrWhiteSpace(request.Queue) ? "banco-regresion" : request.Queue,
                ["take"] = request.Take <= 0 ? 1 : request.Take,
                ["prefetch"] = request.Take <= 0 ? 1 : request.Take,
                ["connectionStringName"] = string.IsNullOrWhiteSpace(request.ConnectionStringName) ? "DefaultConnection" : request.ConnectionStringName,
                ["outputPrefix"] = string.IsNullOrWhiteSpace(request.OutputPrefix) ? "queue.consume" : request.OutputPrefix,
                ["debug"] = false
            }));
        }

        private static StandaloneLoggerRequest AnalyzeStandaloneLoggerRequest(string originalText, string normalizedText)
        {
            var request = new StandaloneLoggerRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            if (!ContainsAny(t, "registrar", "log", "logger", "dejar constancia")) return request;

            if (ContainsAny(t, "advertencia", "warning", "warn")) request.Level = "Warn";
            else if (ContainsAny(t, "error", "erroneo", "erróneo")) request.Level = "Error";
            else request.Level = "Info";

            request.Message = ExtractStandaloneLoggerMessage(originalText);
            return request;
        }

        private static string ExtractStandaloneLoggerMessage(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\bindicando(?:\s+que)?\s+(?<msg>.+?)(?=(?:\.\s|\s+y\s+finalizar\b|\s+finalizar\b|$))",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return "";

            string msg = Regex.Replace(m.Groups["msg"].Value ?? "", @"\s+", " ").Trim();
            while (msg.EndsWith(".", StringComparison.Ordinal) || msg.EndsWith(",", StringComparison.Ordinal) || msg.EndsWith(";", StringComparison.Ordinal))
                msg = msg.Substring(0, msg.Length - 1).Trim();
            return msg;
        }

        private static NotifyRequest AnalyzeNotifyRequest(string originalText, string normalizedText)
        {
            var request = new NotifyRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            bool explicitInternalNotify = ContainsAny(t,
                "notificar internamente", "notificacion interna", "notificación interna",
                "notificar por sistema", "notificacion por sistema", "notificación por sistema",
                "notificar en el sistema", "aviso interno", "aviso por sistema", "aviso en el sistema",
                "avisar por sistema", "mostrar notificacion", "mostrar notificación",
                "agregar notificacion", "agregar notificación", "crear notificacion", "crear notificación");

            if (!explicitInternalNotify)
                return request;

            // Si el usuario pide correo/mail/email, eso lo maneja email.send.
            // Permitimos util.notify solamente cuando la frase aclara que es interno/sistema.
            if (ContainsAny(t, "notificar por correo", "notificar por mail", "notificar por email", "correo", "mail", "email")
                && !ContainsAny(t, "internamente", "interno", "sistema"))
                return request;

            request.WantsNotify = true;
            request.Title = ExtractNotifyTitle(originalText);
            request.Message = ExtractNotifyMessage(originalText);
            request.Destination = ExtractNotifyDestination(originalText);

            if (string.IsNullOrWhiteSpace(request.Title))
                request.Title = "Notificación Workflow Studio";

            if (string.IsNullOrWhiteSpace(request.Message))
                request.Message = "Notificación interna generada por Asistente IA.";

            return request;
        }

        private static string ExtractNotifyTitle(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:titulo|título|asunto)\s+(?<title>.+?)(?=(?:\s+y\s+)?(?:mensaje|cuerpo|texto)\s+|(?:,|\.)\s*\b(?:luego|despues|después|mandar|enviar|derivar|pasar|validar|si|registrar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return m.Success ? CleanExtractedSentence(m.Groups["title"].Value) : "";
        }

        private static string ExtractNotifyMessage(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:mensaje|cuerpo|texto)\s+(?<msg>.+?)(?=(?:,|\.)\s*\b(?:luego|despues|después|mandar|enviar|derivar|pasar|validar|si|registrar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return m.Success ? CleanExtractedSentence(m.Groups["msg"].Value) : "";
        }

        private static string ExtractNotifyDestination(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText)) return "";

            var m = Regex.Match(originalText,
                @"\b(?:notificar|avisar)\s+(?:internamente\s+|por\s+sistema\s+|en\s+el\s+sistema\s+)?(?:(?:al|a\s+la|a|para\s+el|para\s+la|para)\s+)(?:(?:rol|sector|area|área|usuario)\s+)?(?<dest>.+?)(?=(?:\s+con\s+)?(?:titulo|título|asunto|mensaje|cuerpo|texto)\b|(?:,|\.)\s*\b(?:luego|despues|después|mandar|enviar|derivar|pasar|validar|si|registrar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return m.Success ? NormalizeNotifyDestinationText(CleanExtractedSentence(m.Groups["dest"].Value)) : "";
        }

        private static string NormalizeNotifyDestinationText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            string r = CleanExtractedSentence(value).Trim();
            r = Regex.Replace(r,
                @"^(?:al\s+|a\s+la\s+|a\s+|para\s+el\s+|para\s+la\s+|para\s+)?(?:rol|sector|area|área)\s+",
                "",
                RegexOptions.IgnoreCase).Trim();
            r = Regex.Replace(r,
                @"^(?:al\s+|a\s+la\s+|a\s+|para\s+el\s+|para\s+la\s+|para\s+)?usuario\s+",
                "",
                RegexOptions.IgnoreCase).Trim();
            return r;
        }

        private static StateVarsRequest AnalyzeStateVarsRequest(string originalText)
        {
            var request = new StateVarsRequest();
            if (string.IsNullOrWhiteSpace(originalText)) return request;

            string setPattern = @"\b(?:guardar|setear|definir|crear|asignar|poner)\s+(?:la\s+)?variable\s+(?<key>[A-Z0-9_\.]+)\s+(?:con\s+valor|como|en|=)\s+(?<value>.+?)(?=(?:,|\.)?\s*\b(?:luego|despues|después|registrar|finalizar|terminar|esperar|demorar|pausar|mandar|enviar|derivar|pasar|validar|si)\b|[\r\n]|$)";
            foreach (Match m in Regex.Matches(originalText, setPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string key = CleanVariableKey(m.Groups["key"].Value);
                string value = CleanExtractedSentence(m.Groups["value"].Value);
                if (key.Length == 0) continue;

                request.Set[key] = CoerceStateVarValue(value);
            }

            string removePattern = @"\b(?:quitar|eliminar|borrar|remover)\s+(?:la\s+)?variable\s+(?<key>[A-Z0-9_\.]+)";
            foreach (Match m in Regex.Matches(originalText, removePattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string key = CleanVariableKey(m.Groups["key"].Value);
                if (key.Length > 0 && !ContainsIgnoreCase(request.Remove, key))
                    request.Remove.Add(key);
            }

            return request;
        }

        private static DelayRequest AnalyzeDelayRequest(string normalizedText)
        {
            var request = new DelayRequest();
            string t = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(t)) return request;

            var m = Regex.Match(t,
                @"\b(?:esperar|demorar|pausar|delay|demora)\s+(?:de\s+)?(?<n>\d+(?:[\.,]\d+)?)\s*(?<unit>milisegundos|milisegundo|ms|segundos|segundo|minutos|minuto|minutes|minute|min)\b",
                RegexOptions.IgnoreCase);

            if (!m.Success) return request;

            string rawNumber = (m.Groups["n"].Value ?? "").Replace(',', '.');
            string unit = Normalize(m.Groups["unit"].Value).Trim();

            double number;
            if (!double.TryParse(rawNumber, NumberStyles.Any, CultureInfo.InvariantCulture, out number))
                return request;

            if (number <= 0) return request;

            request.WantsDelay = true;

            if (unit == "ms" || unit.StartsWith("milisegundo", StringComparison.OrdinalIgnoreCase))
            {
                request.Milliseconds = (int)Math.Round(number);
            }
            else if (unit.StartsWith("minuto", StringComparison.OrdinalIgnoreCase)
                || unit.StartsWith("minute", StringComparison.OrdinalIgnoreCase)
                || unit == "min")
            {
                request.Seconds = Math.Round(number * 60.0, 3).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                request.Seconds = Math.Round(number, 3).ToString(CultureInfo.InvariantCulture);
            }

            return request;
        }

        private static string CleanVariableKey(string value)
        {
            value = (value ?? "").Trim().Trim('.', ',', ';', ':');
            if (value.Length == 0) return "";

            if (!Regex.IsMatch(value, @"^[A-Z0-9_]+(?:\.[A-Z0-9_]+)*$", RegexOptions.IgnoreCase))
                return "";

            return value;
        }

        private static object CoerceStateVarValue(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0) return "";

            bool b;
            if (bool.TryParse(value, out b)) return b;

            int i;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;

            double d;
            if (double.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;

            return value;
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

        private static bool RoleMentionAppearsOnlyInNotifyContext(string normalizedText, string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            string t = Normalize(normalizedText);

            if (!ContainsAny(t, "notificar", "notificacion", "aviso", "avisar")) return false;
            if (ContainsAny(t, "tarea", "tarea humana", "asignar tarea", "crear tarea", "derivar a", "pasar a", "mandar a", "enviar a", "aprobar", "aprobacion", "autorizar", "revision", "revisar"))
                return false;

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

        private static string ExtractDocumentPath(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // El constructor guiado puede generar frases como:
            // "cargar una NC desde C:\Temp\NC_00002.pdf, luego ..."
            // Antes el proveedor siempre usaba ${input.filePath}; con esto respeta
            // una ruta explícita cuando fue cargada por el usuario.
            var m = Regex.Match(text,
                @"\b(?:cargar|leer|dar\s+de\s+alta|alta)\b.+?\b(?:desde|archivo|ruta)\b\s*[:\-]?\s*(?<path>.+?)(?=(?:,|\.)\s*\b(?:luego|despues|después|verificar|validar|si|mandar|enviar|notificar|avisar|registrar|esperar|finalizar|terminar)\b|[\r\n]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!m.Success) return "";

            string path = (m.Groups["path"].Value ?? "").Trim();
            path = Regex.Replace(path, @"\s+", " ").Trim();
            path = path.Trim(' ', ',', ';', ':', '-', '.', '\'', '"');

            if (path.Length == 0) return "";

            // Convención del motor: input.filePath se expande como template.
            if (string.Equals(path, "input.filePath", StringComparison.OrdinalIgnoreCase))
                return "${input.filePath}";
            if (string.Equals(path, "${input.filePath}", StringComparison.OrdinalIgnoreCase))
                return "${input.filePath}";

            return path;
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
            string lastBranchKind = "";

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

                if (contrary)
                {
                    string contraryBranch = "";

                    if (string.Equals(lastConditionKind, "total", StringComparison.OrdinalIgnoreCase))
                    {
                        // fix48b: "caso contrario" puede venir separado por coma de su destino:
                        // "Caso contrario, enviarla a DIR_GENERAL" se parte en dos cláusulas.
                        // Si todavía no tenemos rol, dejamos pendiente la rama opuesta para que
                        // la cláusula siguiente con el rol complete el camino.
                        if (string.Equals(lastBranchKind, "totalFalse", StringComparison.OrdinalIgnoreCase))
                            contraryBranch = "totalTrue";
                        else if (string.Equals(lastBranchKind, "totalTrue", StringComparison.OrdinalIgnoreCase))
                            contraryBranch = "totalFalse";
                        else
                            contraryBranch = "totalFalse";
                    }
                    else if (string.Equals(lastConditionKind, "cae", StringComparison.OrdinalIgnoreCase))
                    {
                        contraryBranch = "caeFalse";
                    }

                    if (!string.IsNullOrWhiteSpace(contraryBranch))
                    {
                        if (!string.IsNullOrWhiteSpace(r))
                        {
                            AssignBranchRole(b, contraryBranch, r);
                            pendingBranch = "";
                            lastBranchKind = contraryBranch;
                        }
                        else
                        {
                            pendingBranch = contraryBranch;
                        }

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

                    lastBranchKind = branchKind;
                    if (branchKind.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "total";
                    else if (branchKind.StartsWith("cae", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "cae";

                    continue;
                }

                if (!string.IsNullOrWhiteSpace(r) && !string.IsNullOrWhiteSpace(pendingBranch))
                {
                    AssignBranchRole(b, pendingBranch, r);
                    lastBranchKind = pendingBranch;
                    if (pendingBranch.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "total";
                    else if (pendingBranch.StartsWith("cae", StringComparison.OrdinalIgnoreCase))
                        lastConditionKind = "cae";
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

        private static BranchLoggerRequest AnalyzeBranchLoggerRequest(string userText, string normalizedText, string amount)
        {
            var request = new BranchLoggerRequest();
            string t = Normalize(string.IsNullOrWhiteSpace(userText) ? normalizedText : userText);
            if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(amount)) return request;

            bool pendingContrary = false;
            foreach (string raw in SplitClausesForBranches(string.IsNullOrWhiteSpace(userText) ? t : userText))
            {
                string c = Normalize(raw);
                if (string.IsNullOrWhiteSpace(c)) continue;

                bool contrary = ContainsAny(c, "caso contrario", "de lo contrario", "contrario");
                bool logger = ContainsAny(c, "registrar", "evento", "informativo", "advertencia", "log");

                if (contrary && !logger)
                {
                    pendingContrary = true;
                    continue;
                }

                if (!(contrary || pendingContrary) || !logger) continue;

                string msg = ExtractBranchLoggerMessage(raw);
                request.IsDetected = true;
                request.BranchKind = "totalFalse";
                request.Level = ContainsAny(c, "advertencia", "warn", "rechazada", "rechazado", "error") ? "Warn" : "Info";
                request.Message = string.IsNullOrWhiteSpace(msg) ? "No requiere aprobación de Dirección." : msg;
                request.Label = request.Level.Equals("Warn", StringComparison.OrdinalIgnoreCase)
                    ? "Registrar advertencia de rama alternativa"
                    : "Registrar evento sin aprobación de Dirección";
                return request;
            }

            return request;
        }

        private static string ExtractBranchLoggerMessage(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause)) return "";

            Match m = Regex.Match(clause, @"indicando\s+que\s+(?<msg>.*?)(?:\s+y\s+finalizar|\s+finalizar|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success) return CleanOutcomeMessage(m.Groups["msg"].Value);

            m = Regex.Match(clause, @"registrar(?:\s+un|\s+una)?(?:\s+evento|\s+informativo|\s+advertencia)?\s+(?<msg>.*?)(?:\s+y\s+finalizar|\s+finalizar|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success) return CleanOutcomeMessage(m.Groups["msg"].Value);

            return "";
        }

        private static JObject BuildBranchPlan(bool wantsCaeValidation, string amount, BranchAnalysis branches, NaturalCompositeConditionRequest naturalCompositeCondition)
        {
            bool hasNaturalCompositeBranches = naturalCompositeCondition != null
                && naturalCompositeCondition.IsDetected
                && (!string.IsNullOrWhiteSpace(naturalCompositeCondition.TrueRole) || !string.IsNullOrWhiteSpace(naturalCompositeCondition.FalseRole));

            var branchPlan = new JObject
            {
                ["planner"] = "runtime-branch-planner-v1",
                ["hasBranches"] = (branches != null && branches.HasBranchInfo) || hasNaturalCompositeBranches
            };

            var items = new JArray();

            if (naturalCompositeCondition != null && naturalCompositeCondition.IsDetected)
            {
                items.Add(new JObject
                {
                    ["condition"] = NaturalCompositeConditionLabel(naturalCompositeCondition),
                    ["fieldKind"] = "compound",
                    ["truePath"] = string.IsNullOrWhiteSpace(naturalCompositeCondition.TrueRole) ? "pendiente de definir" : "human.task:" + naturalCompositeCondition.TrueRole,
                    ["falsePath"] = string.IsNullOrWhiteSpace(naturalCompositeCondition.FalseRole) ? "pendiente de definir" : "human.task:" + naturalCompositeCondition.FalseRole
                });
            }

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
                    ["falsePath"] = !string.IsNullOrWhiteSpace(branches.TotalFalseRole)
                        ? "human.task:" + branches.TotalFalseRole
                        : (!string.IsNullOrWhiteSpace(branches.TotalFalseActionLabel) ? branches.TotalFalseActionLabel : "pendiente de definir")
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
                { "dir_general", "DIR_GENERAL" },
                { "dir-general", "DIR_GENERAL" },
                { "gerencia", "DIR_GENERAL" },
                { "gerente", "DIR_GENERAL" },
                { "administración", "ADM_FIN" },
                { "administracion", "ADM_FIN" },
                { "adm fin", "ADM_FIN" },
                { "adm_fin", "ADM_FIN" },
                { "adm-fin", "ADM_FIN" },
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

        private class HumanTaskOutcomeRequest
        {
            public bool IsDetected { get; set; }
            public string TaskRole { get; set; }
            public bool HasApprovedBranch { get; set; }
            public bool HasRejectedBranch { get; set; }
            public string ApprovedMessage { get; set; }
            public string RejectedMessage { get; set; }
            public string ApprovedNotifyRole { get; set; }
            public string RejectedNotifyRole { get; set; }
            public bool ApprovedWantsLogger { get; set; }
            public bool RejectedWantsLogger { get; set; }

            public HumanTaskOutcomeRequest()
            {
                IsDetected = false;
                TaskRole = "";
                HasApprovedBranch = false;
                HasRejectedBranch = false;
                ApprovedMessage = "";
                RejectedMessage = "";
                ApprovedNotifyRole = "";
                RejectedNotifyRole = "";
                ApprovedWantsLogger = false;
                RejectedWantsLogger = false;
            }
        }

        private class HumanTaskOutcomeConnection
        {
            public bool IsDetected { get; set; }
            public string TaskRole { get; set; }
            public JObject ResultIf { get; set; }
            public JObject ApprovedAction { get; set; }
            public JObject RejectedAction { get; set; }
            public JObject ApprovedFollowUpAction { get; set; }
            public JObject RejectedFollowUpAction { get; set; }
        }

        private class NaturalCompositeConditionRequest
        {
            public bool IsDetected { get; set; }
            public string RulesMode { get; set; }
            public List<NaturalConditionRule> Rules { get; private set; }
            public string TrueRole { get; set; }
            public string FalseRole { get; set; }

            public NaturalCompositeConditionRequest()
            {
                IsDetected = false;
                RulesMode = "all";
                Rules = new List<NaturalConditionRule>();
                TrueRole = "";
                FalseRole = "";
            }
        }

        private class NaturalConditionRule
        {
            public string Field { get; set; }
            public string Op { get; set; }
            public string Value { get; set; }
            public string Label { get; set; }

            public NaturalConditionRule()
            {
                Field = "";
                Op = "not_empty";
                Value = "";
                Label = "";
            }
        }

        private class GuidedConditionRequest
        {
            public string Field { get; set; }
            public string Op { get; set; }
            public string Value { get; set; }
            public string Label { get; set; }

            public GuidedConditionRequest()
            {
                Field = "";
                Op = "not_empty";
                Value = "";
                Label = "";
            }
        }

        private class HttpRequestRequest
        {
            public bool WantsHttp { get; set; }
            public string Method { get; set; }
            public string Url { get; set; }
            public string Label { get; set; }
            public int TimeoutMs { get; set; }
            public bool FailOnStatus { get; set; }
            public int FailStatusMin { get; set; }
        }

        private class SqlRequest
        {
            public bool WantsSql { get; set; }
            public string ConnectionStringName { get; set; }
            public string Query { get; set; }
            public string Label { get; set; }
        }

        private class FileWriteRequest
        {
            public bool WantsFileWrite { get; set; }
            public string Path { get; set; }
            public string Content { get; set; }
            public string Label { get; set; }
            public bool Overwrite { get; set; }
        }

        private class FileReadRequest
        {
            public bool WantsFileRead { get; set; }
            public string Path { get; set; }
            public string Salida { get; set; }
            public string Label { get; set; }
            public bool AsJson { get; set; }
        }

        private class QueuePublishRequest
        {
            public bool WantsQueuePublish { get; set; }
            public string Broker { get; set; }
            public string Queue { get; set; }
            public string Payload { get; set; }
            public string ConnectionStringName { get; set; }
            public string Label { get; set; }
        }

        private class QueueConsumeRequest
        {
            public bool WantsQueueConsume { get; set; }
            public string Broker { get; set; }
            public string Queue { get; set; }
            public int Take { get; set; }
            public string ConnectionStringName { get; set; }
            public string OutputPrefix { get; set; }
            public string Label { get; set; }
        }

        private class StandaloneLoggerRequest
        {
            public string Level { get; set; }
            public string Message { get; set; }
        }

        private class NotifyRequest
        {
            public bool WantsNotify { get; set; }
            public string Destination { get; set; }
            public string Title { get; set; }
            public string Message { get; set; }

            public NotifyRequest()
            {
                WantsNotify = false;
                Destination = "";
                Title = "";
                Message = "";
            }
        }

        private class StateVarsRequest
        {
            public Dictionary<string, object> Set { get; private set; }
            public List<string> Remove { get; private set; }

            public StateVarsRequest()
            {
                Set = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Remove = new List<string>();
            }

            public bool HasChanges
            {
                get { return Set.Count > 0 || Remove.Count > 0; }
            }
        }

        private class DelayRequest
        {
            public bool WantsDelay { get; set; }
            public int Milliseconds { get; set; }
            public string Seconds { get; set; }

            public DelayRequest()
            {
                WantsDelay = false;
                Milliseconds = 0;
                Seconds = "";
            }
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

        private class BranchLoggerRequest
        {
            public bool IsDetected { get; set; }
            public string BranchKind { get; set; }
            public string Label { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }

            public BranchLoggerRequest()
            {
                IsDetected = false;
                BranchKind = "";
                Label = "";
                Level = "Info";
                Message = "";
            }
        }


        // fix52b: diagnóstico local del Phrase Engine sin dependencia directa a tipos nuevos.
        // En algunos proyectos WebForms/App_Code, Visual Studio puede no resolver tipos nuevos al compilar
        // si el provider los referencia fuerte desde el mismo ciclo. Para no romper la base validada,
        // este diagnóstico queda autocontenido dentro del provider. En próximos fixes se puede volver
        // a delegar al engine externo cuando la separación ya esté estabilizada.
        // fix53: agrega diagnóstico semántico por cláusula. No reemplaza todavía al provider legacy.
        private static PhraseEngineDiagnostic BuildPhraseEngineDiagnostic(string text)
        {
            var d = new PhraseEngineDiagnostic();
            d.OriginalText = text ?? string.Empty;
            d.NormalizedText = Normalize(text);

            List<string> clauses = SplitPhraseDiagnosticClauses(text ?? string.Empty);
            int idx = 1;
            foreach (string raw in clauses)
            {
                string clause = (raw ?? string.Empty).Trim();
                if (clause.Length == 0) continue;
                var c = new PhraseClauseDiagnostic
                {
                    Index = idx++,
                    Text = clause,
                    NormalizedText = Normalize(clause)
                };
                AnalyzePhraseClauseDiagnostic(c);
                d.Clauses.Add(c);
            }

            string n = d.NormalizedText;
            AddPhraseConcept(d, n, "notificar avisar informar mandar aviso", "notify", "util.notify");
            AddPhraseConcept(d, n, "tarea asignar revisar aprobar validar", "human_task", "human.task");
            AddPhraseConcept(d, n, "registrar evento log advertencia informativo", "logger", "util.logger");
            AddPhraseConcept(d, n, "si condicion validar cumple supera mayor menor falta no tiene", "condition", "control.if");
            AddPhraseConcept(d, n, "consulta sql base datos select", "sql", "data.sql");
            AddPhraseConcept(d, n, "http api endpoint servicio", "http", "http.request");
            AddPhraseConcept(d, n, "archivo leer escribir", "file", "file.read,file.write");
            AddPhraseConcept(d, n, "cola queue encolar publicar consumir mensaje", "queue", "queue.publish,queue.consume");
            AddPhraseConcept(d, n, "subflujo workflow proceso", "subflow", "util.subflow");

            d.PrimaryRole = ResolveRole(n, null) ?? string.Empty;
            d.PrimaryHumanOutcome = ResolveHumanOutcome(n);
            d.FirstNumber = ExtractAmount(n) ?? string.Empty;
            return d;
        }

        private static List<string> SplitPhraseDiagnosticClauses(string text)
        {
            var result = new List<string>();
            string t = (text ?? string.Empty).Replace("\r", " ").Replace("\n", ". ");

            // Conservamos explícitamente el marcador "caso contrario". En fix52 el split lo consumía
            // y la cláusula siguiente quedaba sin contexto de rama contraria.
            // IMPORTANTE: no separar "si no", porque frases como "si no supera" son una condición
            // negativa única; separarlas genera "si no, supera" y se pierde el operador semántico.
            t = Regex.Replace(t, @"(?i)\b(caso\s+contrario|de\s+lo\s+contrario)\b\s*,?", @". $1, ");
            t = Regex.Replace(t, @"(?i)\b(después|despues|luego)\b", ". ");

            foreach (string raw in Regex.Split(t, @"[\.;]+"))
            {
                string c = (raw ?? string.Empty).Trim();
                c = Regex.Replace(c, @"\s+", " ").Trim();
                if (c.Length == 0) continue;

                // Si quedó una coma inicial por el split anterior, la limpiamos sin perder contenido.
                c = c.TrimStart(',', ' ', '\t');
                if (c.Length > 0) result.Add(c);
            }

            return result;
        }

        private static void AnalyzePhraseClauseDiagnostic(PhraseClauseDiagnostic c)
        {
            if (c == null) return;
            string n = c.NormalizedText ?? string.Empty;

            // fix55: en cláusulas compuestas como
            // "Si Dirección la aprueba, notificar a COMPRAS..." hay dos roles:
            // - Role: quién resuelve la tarea humana (DIR_GENERAL)
            // - NotifyRole: a quién se notifica (COMPRAS)
            // ResolveRole(n) devuelve el primer alias encontrado y podía confundir ambos.
            string humanResultRole = ResolveHumanResultRoleForClause(n);
            string notifyRole = ResolveNotifyRoleForClause(n);
            c.Role = !string.IsNullOrWhiteSpace(humanResultRole) ? humanResultRole : (ResolveRole(n, null) ?? string.Empty);
            c.Number = ExtractAmount(n) ?? string.Empty;
            c.HumanOutcome = ResolveHumanOutcome(n);

            if (ContainsAny(n, "caso contrario", "de lo contrario"))
            {
                c.BranchMarker = "else";
                c.BranchSide = "opposite";
                c.ClauseType = "else_branch";
            }
            else if (ContainsPhrase(n, "si ") || n.TrimStart().StartsWith("si ", StringComparison.OrdinalIgnoreCase))
            {
                c.BranchMarker = "if";
            }

            bool hasNotify = ContainsAny(n, "notificar", "avisar", "informar", "mandar aviso", "dar aviso");
            bool hasTask = ContainsAny(n, "enviar", "enviarla", "enviarlo", "mandar", "asignar", "derivar", "pasar")
                && ContainsAny(n, "aprobar", "revisar", "corregir", "validar", "tarea");
            bool hasLogger = ContainsAny(n, "registrar", "evento", "log", "advertencia", "informativo");
            bool hasEnd = ContainsAny(n, "finalizar", "terminar", "fin");
            bool hasHttp = ContainsAny(n, "solicitud http", "request http", "http", "api", "endpoint", "servicio rest", "llamada rest");
            bool hasSql = ContainsAny(n, "consulta sql", "ejecutar sql", "sql", "select", "base de datos");
            bool hasFileWrite = ContainsAny(n, "escribir archivo", "guardar archivo", "crear archivo", "generar archivo", "file write", "archivo escribir");
            bool hasFileRead = ContainsAny(n, "leer archivo", "abrir archivo", "file read", "archivo leer");
            bool hasStateVars = ContainsAny(n, "guardar variable", "setear variable", "definir variable", "crear variable", "asignar variable", "poner variable", "quitar variable", "eliminar variable", "borrar variable", "remover variable");
            bool hasQueuePublish = ContainsAny(n, "publicar en cola", "encolar", "queue publish", "mandar a cola", "enviar a cola");
            bool hasQueueConsume = ContainsAny(n, "consumir de cola", "leer de cola", "tomar mensaje", "queue consume");

            // fix65: diagnóstico semántico explícito para nodos operativos simples.
            // Estas cláusulas no son ramas humanas; se auditan contra actions para asegurar
            // que el provider generó el nodo correcto con los parámetros esperados.
            if (hasHttp)
            {
                c.AddConcept("http");
                c.AddNodeType("http.request");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "http_request";
            }

            if (hasSql)
            {
                c.AddConcept("sql");
                c.AddNodeType("data.sql");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "sql_query";
            }

            // fix66: diagnóstico semántico para archivo, variables y cola.
            // Se mantiene separado de la generación legacy: solo audita que el nodo
            // operativo simple esperado exista con parámetros consistentes.
            if (hasFileWrite)
            {
                c.AddConcept("file");
                c.AddNodeType("file.write");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "file_write";
            }

            if (hasFileRead)
            {
                c.AddConcept("file");
                c.AddNodeType("file.read");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "file_read";
            }

            if (hasStateVars)
            {
                c.AddConcept("state_vars");
                c.AddNodeType("state.vars");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "state_vars";
            }

            if (hasQueuePublish)
            {
                c.AddConcept("queue");
                c.AddNodeType("queue.publish");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "queue_publish";
            }

            if (hasQueueConsume)
            {
                c.AddConcept("queue");
                c.AddNodeType("queue.consume");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "queue_consume";
            }

            if (hasNotify)
            {
                c.AddConcept("notify");
                c.AddNodeType("util.notify");
                c.NotifyRole = !string.IsNullOrWhiteSpace(notifyRole) ? notifyRole : c.Role;
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "notification";
            }

            if (hasLogger)
            {
                c.AddConcept("logger");
                c.AddNodeType("util.logger");
                c.LoggerLevel = ContainsAny(n, "advertencia", "warn", "rechaza", "rechazada", "rechazado", "no apto") ? "Warn" : "Info";
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "logger";
            }

            if (hasTask && !hasNotify)
            {
                c.AddConcept("human_task");
                c.AddNodeType("human.task");
                c.TaskRole = c.Role;
                if (string.IsNullOrWhiteSpace(c.ClauseType) || c.ClauseType == "else_branch")
                    c.ClauseType = c.ClauseType == "else_branch" ? "else_human_task" : "human_task";
            }

            if (ContainsAny(n, "nota de credito", "nota credito", "factura", "documento", "cargar"))
            {
                c.AddConcept("document");
                c.AddNodeType("doc.load");
                if (string.IsNullOrWhiteSpace(c.ClauseType)) c.ClauseType = "document_load";
            }

            AnalyzePhraseClauseCondition(c, n);

            // fix59: las frases de condición compuesta explícita (por ejemplo
            // "condición compuesta donde cualquiera de las reglas... CAE no vacío o comprobante no vacío")
            // describen el IF completo, pero el destino aparece en cláusulas posteriores
            // ("Si cumple..." / "Si no cumple..."). No deben auditarse como condition_branch
            // sin rol, porque eso genera warnings falsos. Se auditan como composite_condition.
            if (IsExplicitCompositeConditionClause(n))
            {
                c.ClauseType = "composite_condition";
                c.ConditionKind = "compound";
                c.BranchSide = string.Empty;
                c.TaskRole = string.Empty;
            }

            // fix60: frases naturales negativas compuestas, por ejemplo
            // "Si no tiene CAE o falta el comprobante asociado, enviarla a COMPRAS"
            // no dicen literalmente "condición compuesta", pero el legacy genera un
            // control.if con rulesMode=any y reglas empty. La auditoría semántica
            // debe validar el IF compuesto y también la rama SI hacia el rol indicado.
            if (IsNaturalMissingCompositeConditionBranchClause(n))
            {
                c.ClauseType = "composite_condition_branch";
                c.ConditionKind = "compound";
                c.ConditionField = string.Empty;
                c.ConditionOperator = string.Empty;
                c.ConditionValue = string.Empty;
                c.BranchSide = "true";
                c.TaskRole = string.IsNullOrWhiteSpace(c.TaskRole) ? c.Role : c.TaskRole;
                c.AddConcept("condition");
                c.AddNodeType("control.if");
            }

            // fix61: frases naturales compuestas ALL sin decir literalmente
            // "condición compuesta", por ejemplo:
            // "Si tiene CAE, el total es mayor a 200000 y tiene al menos un ítem,
            // enviarla a DIR_GENERAL". El legacy genera rulesMode=all. La auditoría
            // semántica debe validar las reglas CAE/total/items y la rama SI al rol.
            if (IsNaturalAllCompositeConditionBranchClause(n))
            {
                c.ClauseType = "composite_condition_branch";
                c.ConditionKind = "compound";
                c.ConditionField = string.Empty;
                c.ConditionOperator = string.Empty;
                c.ConditionValue = string.Empty;
                c.BranchSide = "true";
                c.TaskRole = string.IsNullOrWhiteSpace(c.TaskRole) ? c.Role : c.TaskRole;
                c.AddConcept("condition");
                c.AddNodeType("control.if");
            }

            if (!string.IsNullOrWhiteSpace(c.ConditionField))
            {
                c.AddConcept("condition");
                c.AddNodeType("control.if");

                // fix53b: si una cláusula contiene condición y destino humano (ej.:
                // "Si no supera 200000, enviarla a COMPRAS"), su tipo principal debe ser
                // condition_branch. El destino queda en TaskRole, pero la cláusula no debe
                // diagnosticarse como simple human_task porque se pierde el sentido del IF.
                if (string.Equals(c.BranchMarker, "if", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.ClauseType, "human_task", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(c.ClauseType)
                    || string.Equals(c.ClauseType, "else_branch", StringComparison.OrdinalIgnoreCase))
                {
                    c.ClauseType = string.Equals(c.ClauseType, "else_branch", StringComparison.OrdinalIgnoreCase)
                        ? "else_condition"
                        : "condition_branch";
                }
            }

            if (!string.IsNullOrWhiteSpace(c.HumanOutcome))
            {
                c.AddConcept("human_result");
                c.AddNodeType("control.if");
                if (string.IsNullOrWhiteSpace(c.ClauseType) || c.ClauseType == "logger")
                    c.ClauseType = "human_result";
            }

            if (hasEnd)
            {
                c.AddConcept("end");
                c.AddNodeType("util.end");
            }

            c.ActionHint = BuildPhraseClauseActionHint(c);
        }

        private static bool IsExplicitCompositeConditionClause(string normalizedText)
        {
            string n = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(n)) return false;

            bool explicitComposite = ContainsAny(n,
                "condicion compuesta", "condición compuesta",
                "cualquiera de las reglas", "cualquier regla", "al menos una regla", "una de las reglas",
                "todas las reglas", "cada regla",
                "modo any", "modo or", "modo all", "modo and");

            if (!explicitComposite) return false;

            bool mentionsKnownRule = ContainsToken(n, "cae")
                || ContainsToken(n, "cai")
                || ContainsAny(n, "comprobante asociado", "numero asociado", "número asociado", "total", "importe", "items", "ítems", "item", "ítem");

            return mentionsKnownRule;
        }

        private static bool IsNaturalMissingCompositeConditionBranchClause(string normalizedText)
        {
            string n = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(n)) return false;

            bool hasIf = ContainsPhrase(n, "si ") || n.TrimStart().StartsWith("si ", StringComparison.OrdinalIgnoreCase);
            if (!hasIf) return false;

            bool hasCaeMissing = ContainsAny(n,
                "no tiene cae", "no tenga cae", "sin cae", "falta cae", "falta el cae",
                "cae faltante", "cae vacio", "cae vacío", "cae en blanco", "cae no informado");

            bool hasAssociatedMissing = ContainsAny(n,
                "no tiene comprobante asociado", "sin comprobante asociado",
                "falta comprobante asociado", "falta el comprobante asociado",
                "comprobante asociado faltante", "comprobante asociado vacio", "comprobante asociado vacío",
                "comprobante asociado en blanco", "comprobante asociado no informado",
                "no tiene numero asociado", "no tiene número asociado",
                "sin numero asociado", "sin número asociado",
                "falta numero asociado", "falta número asociado",
                "falta el numero asociado", "falta el número asociado",
                "número asociado faltante", "numero asociado faltante");

            bool hasAnyJoin = Regex.IsMatch(n, @"\bo\b", RegexOptions.IgnoreCase)
                || ContainsAny(n, "cualquiera", "alguna de", "una de");

            return hasCaeMissing && hasAssociatedMissing && hasAnyJoin;
        }

        private static bool IsNaturalAllCompositeConditionBranchClause(string normalizedText)
        {
            string n = Normalize(normalizedText);
            if (string.IsNullOrWhiteSpace(n)) return false;

            bool hasIf = ContainsPhrase(n, "si ") || n.TrimStart().StartsWith("si ", StringComparison.OrdinalIgnoreCase);
            if (!hasIf) return false;

            bool hasCaePresent = ContainsToken(n, "cae")
                && ContainsAny(n,
                    "tiene cae", "tenga cae", "con cae",
                    "cae no esta vacio", "cae no está vacío", "cae no este vacio", "cae no esté vacío",
                    "cae informado", "cae no vacio", "cae no vacío");

            bool hasTotalCondition = ContainsAny(n, "total", "importe", "monto")
                && ContainsAny(n, "mayor", "supera", "mas de", "más de", ">");

            bool hasItemsCondition = ContainsAny(n,
                "al menos un item", "al menos un ítem", "al menos un items", "al menos un ítems",
                "tiene al menos un item", "tiene al menos un ítem",
                "items", "ítems", "item", "ítem");

            bool hasAllJoin = Regex.IsMatch(n, @"\by\b", RegexOptions.IgnoreCase)
                || ContainsAny(n, "todas", "cada regla", "cumplirse todas");

            // Evita capturar condiciones simples de importe o presencia: este caso solo
            // se considera compuesto ALL cuando aparecen las tres reglas conocidas.
            return hasCaePresent && hasTotalCondition && hasItemsCondition && hasAllJoin;
        }

        private static void AnalyzePhraseClauseCondition(PhraseClauseDiagnostic c, string n)
        {
            if (c == null) return;
            if (n == null) n = string.Empty;

            bool looksLikeAmountCondition = ContainsAny(n, "total", "importe", "monto")
                || (!string.IsNullOrWhiteSpace(c.Number)
                    && ContainsAny(n, "supera", "no supera", "no excede", "no pasa", "mayor", "menor", "menos de", "pesos", "$"));

            if (looksLikeAmountCondition)
            {
                c.ConditionKind = "total";
                c.ConditionField = "biz.notaCredito.total";
                c.ConditionValue = c.Number;

                if (ContainsAny(n, "no supera", "no excede", "no pasa", "menor o igual", "hasta"))
                {
                    c.IsNegative = true;
                    c.ConditionOperator = "<=";
                    c.BranchSide = "false_of_greater_than";
                }
                else if (ContainsAny(n, "supera", "mayor", "mas de", "más de", ">"))
                {
                    c.ConditionOperator = ">";
                    c.BranchSide = "true";
                }
                else if (ContainsAny(n, "menor", "menos de", "<"))
                {
                    c.ConditionOperator = "<";
                    c.BranchSide = "true";
                }
                return;
            }

            if (ContainsToken(n, "cae") || ContainsToken(n, "cai"))
            {
                c.ConditionKind = "cae";
                c.ConditionField = "biz.notaCredito.cae";
                if (ContainsAny(n, "falta", "no tiene", "vacio", "vacío", "sin cae", "sin dato", "no informado"))
                {
                    c.IsNegative = true;
                    c.ConditionOperator = "empty";
                }
                else
                {
                    c.ConditionOperator = "not_empty";
                }
                return;
            }

            if (ContainsAny(n, "comprobante asociado", "asociado", "numero asociado", "número asociado"))
            {
                c.ConditionKind = "associated_document";
                c.ConditionField = "biz.notaCredito.comprobanteAsociado.numero";
                if (ContainsAny(n, "falta", "no tiene", "vacio", "vacío", "sin", "no informado"))
                {
                    c.IsNegative = true;
                    c.ConditionOperator = "empty";
                }
                else
                {
                    c.ConditionOperator = "not_empty";
                }
                return;
            }

            if (ContainsAny(n, "item", "ítem", "items", "ítems"))
            {
                c.ConditionKind = "items";
                c.ConditionField = "biz.notaCredito.itemsCount";
                c.ConditionOperator = ContainsAny(n, "no tiene", "sin", "falta") ? "==" : ">";
                c.ConditionValue = ContainsAny(n, "no tiene", "sin", "falta") ? "0" : "0";
                c.IsNegative = ContainsAny(n, "no tiene", "sin", "falta");
                return;
            }
        }

        private static string BuildPhraseClauseActionHint(PhraseClauseDiagnostic c)
        {
            if (c == null) return string.Empty;

            if (!string.IsNullOrWhiteSpace(c.ConditionField))
            {
                string op = string.IsNullOrWhiteSpace(c.ConditionOperator) ? "?" : c.ConditionOperator;
                string val = string.IsNullOrWhiteSpace(c.ConditionValue) ? "" : " " + c.ConditionValue;
                return "condition: " + c.ConditionField + " " + op + val;
            }

            if (!string.IsNullOrWhiteSpace(c.HumanOutcome))
            {
                return "human_result: " + (string.IsNullOrWhiteSpace(c.Role) ? "" : c.Role + " ") + c.HumanOutcome;
            }

            if (!string.IsNullOrWhiteSpace(c.NotifyRole))
                return "notify_role: " + c.NotifyRole;

            if (!string.IsNullOrWhiteSpace(c.TaskRole))
                return "human_task_role: " + c.TaskRole;

            if (!string.IsNullOrWhiteSpace(c.LoggerLevel))
                return "logger: " + c.LoggerLevel;

            if (c.NodeTypes != null && c.NodeTypes.Count > 0)
                return "node: " + c.NodeTypes[0];

            return string.Empty;
        }

        private static void AddPhraseConcept(PhraseEngineDiagnostic d, string normalizedText, string keywords, string concept, string nodeTypesCsv)
        {
            if (d == null || string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(keywords)) return;
            string[] ks = keywords.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool found = false;
            foreach (string k in ks)
            {
                if (normalizedText.Contains(k)) { found = true; break; }
            }
            if (!found) return;
            d.AddConcept(concept);
            string[] nodeTypes = (nodeTypesCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string nt in nodeTypes) d.AddNodeType(nt.Trim());
        }

        // fix54: validador diagnóstico de consistencia semántica.
        // Compara lo que entendió el Phrase Engine por cláusula contra lo que el provider legacy generó
        // en actions / branchPlan / proposedConnections. Por ahora NO bloquea el canvas: deja errores y
        // advertencias en el JSON técnico para migrar lógica con seguridad en próximos fixes.
        private static JObject BuildPhraseSemanticConsistency(PhraseEngineDiagnostic phraseEngine, JArray actions, JObject branchPlan, JArray proposedConnections)
        {
            var checks = new JArray();
            var errors = new JArray();
            var warnings = new JArray();

            if (phraseEngine == null)
            {
                errors.Add("Phrase Engine no disponible para validar consistencia semántica.");
                return BuildSemanticConsistencyObject(false, checks, warnings, errors);
            }

            foreach (PhraseClauseDiagnostic clause in phraseEngine.Clauses)
            {
                if (clause == null) continue;

                if (string.Equals(clause.ClauseType, "http_request", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticHttpRequest(clause, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "sql_query", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticSqlQuery(clause, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "logger", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(clause.HumanOutcome))
                    CheckSemanticStandaloneLogger(clause, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "file_write", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticFileWrite(clause, phraseEngine.OriginalText, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "file_read", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticFileRead(clause, phraseEngine.OriginalText, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "state_vars", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticStateVars(clause, phraseEngine.OriginalText, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "queue_publish", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticQueuePublish(clause, phraseEngine.OriginalText, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "queue_consume", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticQueueConsume(clause, phraseEngine.OriginalText, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "composite_condition", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clause.ClauseType, "composite_condition_branch", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticCompositeCondition(clause, actions, branchPlan, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "condition_branch", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clause.ClauseType, "composite_condition_branch", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticConditionBranch(clause, branchPlan, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "else_human_task", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clause.ClauseType, "human_task", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticHumanTask(clause, actions, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "notification", StringComparison.OrdinalIgnoreCase))
                    CheckSemanticNotification(clause, actions, proposedConnections, checks, warnings, errors);

                // fix55: cláusulas como "Si Dirección la rechaza, registrar..., notificar..."
                // siguen siendo notification, pero además expresan resultado humano y logger por rama.
                if (!string.IsNullOrWhiteSpace(clause.HumanOutcome))
                    CheckSemanticHumanResult(clause, actions, proposedConnections, checks, warnings, errors);

                if (string.Equals(clause.ClauseType, "else_branch", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(clause.LoggerLevel))
                    CheckSemanticElseLoggerBranch(clause, actions, proposedConnections, checks, warnings, errors);
            }

            CheckGeneratedIfBranches(actions, proposedConnections, checks, warnings, errors);

            bool ok = errors.Count == 0;
            return BuildSemanticConsistencyObject(ok, checks, warnings, errors);
        }

        private static JObject BuildSemanticConsistencyObject(bool ok, JArray checks, JArray warnings, JArray errors)
        {
            return new JObject
            {
                ["ok"] = ok,
                ["mode"] = "diagnostic_only",
                ["checks"] = checks ?? new JArray(),
                ["warnings"] = warnings ?? new JArray(),
                ["errors"] = errors ?? new JArray()
            };
        }

        private static bool SemanticConsistencyOk(JObject consistency)
        {
            if (consistency == null) return true;
            JToken tok;
            if (!consistency.TryGetValue("ok", out tok)) return true;
            bool val;
            if (bool.TryParse(Convert.ToString(tok), out val)) return val;
            return true;
        }

        private static void CheckSemanticHttpRequest(PhraseClauseDiagnostic clause, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            HttpRequestRequest expected = AnalyzeHttpRequestRequest(clause.Text, clause.NormalizedText);
            JObject action = FindHttpRequestAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "http_request",
                ["expectedMethod"] = string.IsNullOrWhiteSpace(expected.Method) ? "GET" : expected.Method,
                ["expectedUrl"] = string.IsNullOrWhiteSpace(expected.Url) ? "/Api/Ping.ashx" : expected.Url,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualMethod"] = p == null ? "" : Convert.ToString(p["method"]);
                check["actualUrl"] = p == null ? "" : Convert.ToString(p["url"]);
                check["actualTimeoutMs"] = p == null ? "" : Convert.ToString(p["timeoutMs"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba http.request " + (string.IsNullOrWhiteSpace(expected.Method) ? "GET" : expected.Method) + " " + (string.IsNullOrWhiteSpace(expected.Url) ? "/Api/Ping.ashx" : expected.Url) + ", pero no existe en actions.");
        }

        private static void CheckSemanticSqlQuery(PhraseClauseDiagnostic clause, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            SqlRequest expected = AnalyzeSqlRequest(clause.Text, clause.NormalizedText);
            JObject action = FindSqlAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "sql_query",
                ["expectedConnectionStringName"] = string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName,
                ["expectedQuery"] = string.IsNullOrWhiteSpace(expected.Query) ? "SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;" : expected.Query,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualConnectionStringName"] = p == null ? "" : Convert.ToString(p["connectionStringName"]);
                check["actualQuery"] = p == null ? "" : Convert.ToString(p["query"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba data.sql con conexión " + (string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName) + ", pero no existe en actions o no coincide la consulta.");
        }

        private static void CheckSemanticStandaloneLogger(PhraseClauseDiagnostic clause, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            StandaloneLoggerRequest expected = AnalyzeStandaloneLoggerRequest(clause.Text, clause.NormalizedText);
            string expectedLevel = string.IsNullOrWhiteSpace(expected.Level) ? (string.IsNullOrWhiteSpace(clause.LoggerLevel) ? "Info" : clause.LoggerLevel) : expected.Level;
            string expectedMessage = expected.Message ?? string.Empty;

            JObject action = FindLoggerAction(actions, expectedLevel, expectedMessage);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "standalone_logger",
                ["expectedLevel"] = expectedLevel,
                ["expectedMessage"] = expectedMessage,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualLevel"] = p == null ? "" : Convert.ToString(p["level"]);
                check["actualMessage"] = p == null ? "" : Convert.ToString(p["message"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba util.logger " + expectedLevel + " con mensaje \"" + expectedMessage + "\", pero no existe en actions.");
        }

        private static void CheckSemanticFileWrite(PhraseClauseDiagnostic clause, string fullText, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            FileWriteRequest expected = AnalyzeFileWriteRequest(string.IsNullOrWhiteSpace(fullText) ? clause.Text : fullText, clause.NormalizedText);
            JObject action = FindFileWriteAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "file_write",
                ["expectedPath"] = string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path,
                ["expectedContent"] = string.IsNullOrWhiteSpace(expected.Content) ? "Contenido generado por Asistente IA" : expected.Content,
                ["expectedEncoding"] = "utf-8",
                ["expectedOverwrite"] = true,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualPath"] = p == null ? "" : Convert.ToString(p["path"]);
                check["actualContent"] = p == null ? "" : Convert.ToString(p["content"]);
                check["actualEncoding"] = p == null ? "" : Convert.ToString(p["encoding"]);
                check["actualOverwrite"] = p == null ? "" : Convert.ToString(p["overwrite"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba file.write path " + (string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path) + ", pero no existe en actions o no coinciden parámetros.");
        }

        private static void CheckSemanticFileRead(PhraseClauseDiagnostic clause, string fullText, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            FileReadRequest expected = AnalyzeFileReadRequest(string.IsNullOrWhiteSpace(fullText) ? clause.Text : fullText, clause.NormalizedText);
            JObject action = FindFileReadAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "file_read",
                ["expectedPath"] = string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path,
                ["expectedSalida"] = string.IsNullOrWhiteSpace(expected.Salida) ? "archivo" : expected.Salida,
                ["expectedEncoding"] = "utf-8",
                ["expectedAsJson"] = false,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualPath"] = p == null ? "" : Convert.ToString(p["path"]);
                check["actualSalida"] = p == null ? "" : Convert.ToString(p["salida"]);
                check["actualEncoding"] = p == null ? "" : Convert.ToString(p["encoding"]);
                check["actualAsJson"] = p == null ? "" : Convert.ToString(p["asJson"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba file.read path " + (string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path) + ", pero no existe en actions o no coinciden parámetros.");
        }

        private static void CheckSemanticStateVars(PhraseClauseDiagnostic clause, string fullText, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            StateVarsRequest expected = AnalyzeStateVarsRequest(string.IsNullOrWhiteSpace(fullText) ? clause.Text : fullText);
            JObject action = FindStateVarsAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "state_vars",
                ["expectedSetCount"] = expected == null ? 0 : expected.Set.Count,
                ["expectedRemoveCount"] = expected == null ? 0 : expected.Remove.Count,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualSetCount"] = p == null || !(p["set"] is JObject) ? 0 : CountJObjectProperties((JObject)p["set"]);
                check["actualRemoveCount"] = p == null || !(p["remove"] is JArray) ? 0 : ((JArray)p["remove"]).Count;
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba state.vars con set/remove indicado, pero no existe en actions o no coinciden parámetros.");
        }

        private static int CountJObjectProperties(JObject obj)
        {
            if (obj == null) return 0;
            int count = 0;
            foreach (var prop in obj.Properties()) count++;
            return count;
        }

        private static void CheckSemanticQueuePublish(PhraseClauseDiagnostic clause, string fullText, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            QueuePublishRequest expected = AnalyzeQueuePublishRequest(string.IsNullOrWhiteSpace(fullText) ? clause.Text : fullText, clause.NormalizedText);
            JObject action = FindQueuePublishAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "queue_publish",
                ["expectedBroker"] = string.IsNullOrWhiteSpace(expected.Broker) ? "sql" : expected.Broker,
                ["expectedQueue"] = string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue,
                ["expectedPayload"] = string.IsNullOrWhiteSpace(expected.Payload) ? "Mensaje generado por Asistente IA" : expected.Payload,
                ["expectedConnectionStringName"] = string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualBroker"] = p == null ? "" : Convert.ToString(p["broker"]);
                check["actualQueue"] = p == null ? "" : Convert.ToString(p["queue"]);
                check["actualPayload"] = p == null ? "" : Convert.ToString(p["payload"]);
                check["actualConnectionStringName"] = p == null ? "" : Convert.ToString(p["connectionStringName"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba queue.publish cola " + (string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue) + ", pero no existe en actions o no coinciden parámetros.");
        }

        private static void CheckSemanticQueueConsume(PhraseClauseDiagnostic clause, string fullText, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            QueueConsumeRequest expected = AnalyzeQueueConsumeRequest(string.IsNullOrWhiteSpace(fullText) ? clause.Text : fullText, clause.NormalizedText);
            JObject action = FindQueueConsumeAction(actions, expected);
            bool exists = action != null;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "queue_consume",
                ["expectedBroker"] = string.IsNullOrWhiteSpace(expected.Broker) ? "sql" : expected.Broker,
                ["expectedQueue"] = string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue,
                ["expectedTake"] = expected.Take <= 0 ? 1 : expected.Take,
                ["expectedConnectionStringName"] = string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName,
                ["expectedOutputPrefix"] = string.IsNullOrWhiteSpace(expected.OutputPrefix) ? "queue.consume" : expected.OutputPrefix,
                ["result"] = exists ? "ok" : "missing_action"
            };

            if (exists)
            {
                JObject p = action["params"] as JObject;
                check["label"] = Convert.ToString(action["label"]);
                check["actualBroker"] = p == null ? "" : Convert.ToString(p["broker"]);
                check["actualQueue"] = p == null ? "" : Convert.ToString(p["queue"]);
                check["actualTake"] = p == null ? "" : Convert.ToString(p["take"]);
                check["actualPrefetch"] = p == null ? "" : Convert.ToString(p["prefetch"]);
                check["actualConnectionStringName"] = p == null ? "" : Convert.ToString(p["connectionStringName"]);
                check["actualOutputPrefix"] = p == null ? "" : Convert.ToString(p["outputPrefix"]);
            }

            checks.Add(check);

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba queue.consume cola " + (string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue) + ", pero no existe en actions o no coinciden parámetros.");
        }

        private static JObject FindFileWriteAction(JArray actions, FileWriteRequest expected)
        {
            if (actions == null) return null;
            string expectedPath = expected == null || string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path;
            string expectedContent = expected == null || string.IsNullOrWhiteSpace(expected.Content) ? "Contenido generado por Asistente IA" : expected.Content;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "file.write", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                string path = Convert.ToString(p["path"]);
                string content = Convert.ToString(p["content"]);
                string encoding = Convert.ToString(p["encoding"]);
                string overwrite = Convert.ToString(p["overwrite"]);
                if (!FilePathSemanticEquals(path, expectedPath)) continue;
                if (!string.Equals(NormalizeSemanticText(content), NormalizeSemanticText(expectedContent), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase)) continue;
                if (!BoolTextEquals(overwrite, true)) continue;
                return action;
            }
            return null;
        }

        private static JObject FindFileReadAction(JArray actions, FileReadRequest expected)
        {
            if (actions == null) return null;
            string expectedPath = expected == null || string.IsNullOrWhiteSpace(expected.Path) ? @"C:\temp\wf_ai_regression.txt" : expected.Path;
            string expectedSalida = expected == null || string.IsNullOrWhiteSpace(expected.Salida) ? "archivo" : expected.Salida;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "file.read", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                string path = Convert.ToString(p["path"]);
                string salida = Convert.ToString(p["salida"]);
                string encoding = Convert.ToString(p["encoding"]);
                string asJson = Convert.ToString(p["asJson"]);
                if (!FilePathSemanticEquals(path, expectedPath)) continue;
                if (!string.Equals(salida, expectedSalida, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase)) continue;
                if (!BoolTextEquals(asJson, false)) continue;
                return action;
            }
            return null;
        }

        private static JObject FindStateVarsAction(JArray actions, StateVarsRequest expected)
        {
            if (actions == null || expected == null || !expected.HasChanges) return null;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "state.vars", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;

                JObject set = p["set"] as JObject;
                JArray remove = p["remove"] as JArray;

                bool ok = true;
                foreach (var kv in expected.Set)
                {
                    if (set == null || set[kv.Key] == null) { ok = false; break; }
                    string actual = NormalizeSemanticText(Convert.ToString(set[kv.Key]));
                    string exp = NormalizeSemanticText(kv.Value == null ? "" : Convert.ToString(kv.Value, CultureInfo.InvariantCulture));
                    if (!string.Equals(actual, exp, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
                }
                if (!ok) continue;

                foreach (string key in expected.Remove)
                {
                    bool found = false;
                    if (remove != null)
                    {
                        foreach (JToken tok in remove)
                        {
                            if (string.Equals(Convert.ToString(tok), key, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                        }
                    }
                    if (!found) { ok = false; break; }
                }
                if (!ok) continue;

                return action;
            }
            return null;
        }

        private static JObject FindQueuePublishAction(JArray actions, QueuePublishRequest expected)
        {
            if (actions == null) return null;
            string expectedBroker = expected == null || string.IsNullOrWhiteSpace(expected.Broker) ? "sql" : expected.Broker;
            string expectedQueue = expected == null || string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue;
            string expectedPayload = expected == null || string.IsNullOrWhiteSpace(expected.Payload) ? "Mensaje generado por Asistente IA" : expected.Payload;
            string expectedConnection = expected == null || string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "queue.publish", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["broker"]), expectedBroker, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(p["queue"]), expectedQueue, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(NormalizeSemanticText(Convert.ToString(p["payload"])), NormalizeSemanticText(expectedPayload), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(p["connectionStringName"]), expectedConnection, StringComparison.OrdinalIgnoreCase)) continue;
                return action;
            }
            return null;
        }

        private static JObject FindQueueConsumeAction(JArray actions, QueueConsumeRequest expected)
        {
            if (actions == null) return null;
            string expectedBroker = expected == null || string.IsNullOrWhiteSpace(expected.Broker) ? "sql" : expected.Broker;
            string expectedQueue = expected == null || string.IsNullOrWhiteSpace(expected.Queue) ? "banco-regresion" : expected.Queue;
            int expectedTake = expected == null || expected.Take <= 0 ? 1 : expected.Take;
            string expectedConnection = expected == null || string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName;
            string expectedOutputPrefix = expected == null || string.IsNullOrWhiteSpace(expected.OutputPrefix) ? "queue.consume" : expected.OutputPrefix;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "queue.consume", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["broker"]), expectedBroker, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(p["queue"]), expectedQueue, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IntTextEquals(Convert.ToString(p["take"]), expectedTake)) continue;
                if (!IntTextEquals(Convert.ToString(p["prefetch"]), expectedTake)) continue;
                if (!string.Equals(Convert.ToString(p["connectionStringName"]), expectedConnection, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(p["outputPrefix"]), expectedOutputPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                return action;
            }
            return null;
        }

        // fix66: al igual que con URLs, el splitter puede cortar rutas con extensión
        // (por ejemplo .txt). Para la auditoría aceptamos equivalencia exacta o prefijo
        // seguro cuando solo falta la extensión dentro de la cláusula diagnóstica.
        private static bool FilePathSemanticEquals(string actualPath, string expectedPath)
        {
            string actual = NormalizeFilePathForSemantic(actualPath);
            string expected = NormalizeFilePathForSemantic(expectedPath);
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected)) return false;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            if (!expected.Contains(".") && actual.Length > expected.Length && actual.StartsWith(expected + ".", StringComparison.OrdinalIgnoreCase)) return true;
            if (!actual.Contains(".") && expected.Length > actual.Length && expected.StartsWith(actual + ".", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string NormalizeFilePathForSemantic(string value)
        {
            string s = (value ?? string.Empty).Trim();
            while (s.EndsWith(",", StringComparison.Ordinal) || s.EndsWith(";", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1).Trim();
            return s;
        }

        private static bool BoolTextEquals(string value, bool expected)
        {
            bool parsed;
            if (bool.TryParse(value, out parsed)) return parsed == expected;
            string s = (value ?? string.Empty).Trim();
            if (expected) return string.Equals(s, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "si", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "sí", StringComparison.OrdinalIgnoreCase);
            return string.Equals(s, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(s);
        }

        private static bool IntTextEquals(string value, int expected)
        {
            int parsed;
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed == expected;
        }

        private static JObject FindHttpRequestAction(JArray actions, HttpRequestRequest expected)
        {
            if (actions == null) return null;
            string expectedMethod = expected == null || string.IsNullOrWhiteSpace(expected.Method) ? "GET" : expected.Method;
            string expectedUrl = expected == null || string.IsNullOrWhiteSpace(expected.Url) ? "/Api/Ping.ashx" : expected.Url;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "http.request", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                string method = Convert.ToString(p["method"]);
                string url = Convert.ToString(p["url"]);
                if (!string.Equals(method, expectedMethod, StringComparison.OrdinalIgnoreCase)) continue;
                if (!HttpUrlSemanticEquals(url, expectedUrl)) continue;
                return action;
            }
            return null;
        }

        // fix65b: el separador de cláusulas puede cortar URLs por el punto de la extensión
        // (ej.: /Api/Ping.ashx queda como /Api/Ping dentro de la cláusula HTTP). Para la
        // auditoría semántica aceptamos coincidencia exacta o prefijo seguro con extensión,
        // sin relajar el método ni el nodo generado.
        private static bool HttpUrlSemanticEquals(string actualUrl, string expectedUrl)
        {
            string actual = NormalizeHttpUrlForSemantic(actualUrl);
            string expected = NormalizeHttpUrlForSemantic(expectedUrl);

            if (string.IsNullOrWhiteSpace(expected)) expected = "/Api/Ping.ashx";
            if (string.IsNullOrWhiteSpace(actual)) return false;

            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;

            if (!expected.Contains(".")
                && actual.Length > expected.Length
                && actual.StartsWith(expected + ".", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!actual.Contains(".")
                && expected.Length > actual.Length
                && expected.StartsWith(actual + ".", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string NormalizeHttpUrlForSemantic(string url)
        {
            string s = (url ?? string.Empty).Trim();
            while (s.EndsWith(",", StringComparison.Ordinal) || s.EndsWith(";", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1).Trim();
            return s;
        }

        private static JObject FindSqlAction(JArray actions, SqlRequest expected)
        {
            if (actions == null) return null;
            string expectedConnection = expected == null || string.IsNullOrWhiteSpace(expected.ConnectionStringName) ? "DefaultConnection" : expected.ConnectionStringName;
            string expectedQuery = expected == null || string.IsNullOrWhiteSpace(expected.Query) ? "SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;" : expected.Query;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "data.sql", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                string connection = Convert.ToString(p["connectionStringName"]);
                string query = Convert.ToString(p["query"]);
                if (!string.Equals(connection, expectedConnection, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SqlTextEquals(query, expectedQuery)) continue;
                return action;
            }
            return null;
        }

        private static JObject FindLoggerAction(JArray actions, string expectedLevel, string expectedMessage)
        {
            if (actions == null) return null;
            string levelExpected = string.IsNullOrWhiteSpace(expectedLevel) ? "Info" : expectedLevel;
            string messageExpected = NormalizeSemanticText(expectedMessage);

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "util.logger", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;

                string level = Convert.ToString(p["level"]);
                string message = NormalizeSemanticText(Convert.ToString(p["message"]));
                if (!string.Equals(level, levelExpected, StringComparison.OrdinalIgnoreCase)) continue;

                if (string.IsNullOrWhiteSpace(messageExpected) || string.Equals(message, messageExpected, StringComparison.OrdinalIgnoreCase))
                    return action;
            }
            return null;
        }

        private static bool SqlTextEquals(string a, string b)
        {
            return string.Equals(NormalizeSqlText(a), NormalizeSqlText(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSqlText(string value)
        {
            string s = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            while (s.EndsWith(";", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1).Trim();
            return s;
        }

        private static string NormalizeSemanticText(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static void CheckSemanticCompositeCondition(PhraseClauseDiagnostic clause, JArray actions, JObject branchPlan, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;

            string n = clause.NormalizedText ?? string.Empty;
            string expectedMode = ResolveSemanticCompositeMode(n);
            JObject action = FindCompositeConditionAction(actions, expectedMode);

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "composite_condition",
                ["expectedMode"] = expectedMode,
                ["result"] = action == null ? "missing_action" : "ok"
            };

            if (action != null)
                check["label"] = Convert.ToString(action["label"]);

            checks.Add(check);

            if (action == null)
            {
                errors.Add("Cláusula " + clause.Index + ": esperaba control.if de condición compuesta modo " + expectedMode + ", pero no existe en actions.");
                return;
            }

            ValidateCompositeRuleIfMentioned(clause, action, "cae", "biz.notaCredito.cae", checks, errors);
            ValidateCompositeRuleIfMentioned(clause, action, "associated_document", "biz.notaCredito.comprobanteAsociado.numero", checks, errors);
            ValidateCompositeRuleIfMentioned(clause, action, "items", "biz.notaCredito.itemsCount", checks, errors);
            ValidateCompositeRuleIfMentioned(clause, action, "total", "biz.notaCredito.total", checks, errors);

            JObject branch = FindBranchByFieldKind(branchPlan, "compound");
            bool hasBranchPlan = branch != null;
            checks.Add(new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "composite_branch_plan",
                ["fieldKind"] = "compound",
                ["truePath"] = hasBranchPlan ? Convert.ToString(branch["truePath"]) : "",
                ["falsePath"] = hasBranchPlan ? Convert.ToString(branch["falsePath"]) : "",
                ["result"] = hasBranchPlan ? "ok" : "missing_branch_plan"
            });

            if (!hasBranchPlan)
                errors.Add("Cláusula " + clause.Index + ": la condición compuesta existe en actions, pero no existe branchPlan fieldKind=compound.");
        }

        private static string ResolveSemanticCompositeMode(string normalizedText)
        {
            string n = Normalize(normalizedText);
            if (ContainsAny(n, "cualquiera", "cualquier regla", "al menos una", "una de las reglas", "modo any", "modo or")
                || Regex.IsMatch(n, @"\bo\b", RegexOptions.IgnoreCase))
                return "any";
            return "all";
        }

        private static JObject FindCompositeConditionAction(JArray actions, string expectedMode)
        {
            if (actions == null) return null;
            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                JArray rules = p["rules"] as JArray;
                if (rules == null || rules.Count == 0) continue;
                string mode = Convert.ToString(p["rulesMode"]);
                if (string.Equals(mode, expectedMode, StringComparison.OrdinalIgnoreCase)) return action;
            }
            return null;
        }

        private static void ValidateCompositeRuleIfMentioned(PhraseClauseDiagnostic clause, JObject action, string ruleKind, string expectedField, JArray checks, JArray errors)
        {
            if (clause == null || action == null) return;

            string n = clause.NormalizedText ?? string.Empty;
            bool mentioned = false;
            string expectedOp = "not_empty";
            string expectedValue = "";

            if (string.Equals(ruleKind, "cae", StringComparison.OrdinalIgnoreCase))
            {
                mentioned = ContainsToken(n, "cae") || ContainsToken(n, "cai");
                expectedOp = ResolveSemanticPresenceOperatorForClause(n, "cae");
            }
            else if (string.Equals(ruleKind, "associated_document", StringComparison.OrdinalIgnoreCase))
            {
                mentioned = ContainsAny(n, "comprobante asociado", "numero asociado", "número asociado", "asociado");
                expectedOp = ResolveSemanticPresenceOperatorForClause(n, "associated_document");
            }
            else if (string.Equals(ruleKind, "items", StringComparison.OrdinalIgnoreCase))
            {
                mentioned = ContainsAny(n, "itemscount", "items count", "items", "ítems", "item", "ítem", "al menos un item", "al menos un ítem");
                expectedOp = ContainsAny(n, "no tiene", "sin", "falta") ? "==" : ">";
                expectedValue = "0";
            }
            else if (string.Equals(ruleKind, "total", StringComparison.OrdinalIgnoreCase))
            {
                mentioned = ContainsToken(n, "total") || ContainsToken(n, "importe");
                expectedOp = ">";
                if (ContainsAny(n, "menor o igual", "menor igual", "no supera", "no mayor")) expectedOp = "<=";
                else if (ContainsAny(n, "menor que", "menor a")) expectedOp = "<";
                else if (ContainsAny(n, "mayor o igual", "mayor igual")) expectedOp = ">=";
                else if (ContainsAny(n, "igual a", "es igual")) expectedOp = "==";
                expectedValue = ExtractAmount(n) ?? "";
            }

            if (!mentioned) return;

            bool exists = CompositeRuleExists(action, expectedField, expectedOp, expectedValue);
            checks.Add(new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "composite_rule",
                ["ruleKind"] = ruleKind,
                ["expectedField"] = expectedField,
                ["expectedOp"] = expectedOp,
                ["expectedValue"] = expectedValue,
                ["result"] = exists ? "ok" : "missing_rule"
            });

            if (!exists)
            {
                string valuePart = string.IsNullOrWhiteSpace(expectedValue) ? "" : " valor " + expectedValue;
                errors.Add("Cláusula " + clause.Index + ": esperaba regla compuesta " + expectedField + " " + expectedOp + valuePart + ", pero no existe en actions.");
            }
        }

        private static string ResolveSemanticPresenceOperatorForClause(string normalizedText, string fieldKind)
        {
            string n = Normalize(normalizedText);
            string k = Normalize(fieldKind);

            if (k == "cae" || k == "cai")
            {
                if (ContainsAny(n, "cae no esta vacio", "cae no está vacío", "cae no este vacio", "cae no esté vacío", "cae informado", "cae no vacio", "cae no vacío"))
                    return "not_empty";
                if (ContainsAny(n, "no tiene cae", "no tenga cae", "sin cae", "falta cae", "falta el cae", "cae faltante", "cae vacio", "cae vacío", "cae en blanco", "cae no informado"))
                    return "empty";
                return "not_empty";
            }

            if (ContainsAny(n, "comprobante asociado no esta vacio", "comprobante asociado no está vacío", "comprobante asociado no este vacio", "comprobante asociado no esté vacío", "comprobante asociado informado", "numero asociado informado", "número asociado informado"))
                return "not_empty";

            if (ContainsAny(n, "no tiene comprobante asociado", "sin comprobante asociado", "falta comprobante asociado", "falta el comprobante asociado", "comprobante asociado faltante", "comprobante asociado vacio", "comprobante asociado vacío", "comprobante asociado en blanco", "comprobante asociado no informado", "no tiene numero asociado", "sin numero asociado", "falta numero asociado", "número asociado faltante"))
                return "empty";

            return "not_empty";
        }

        private static bool CompositeRuleExists(JObject action, string expectedField, string expectedOp, string expectedValue)
        {
            if (action == null) return false;
            JObject p = action["params"] as JObject;
            if (p == null) return false;
            JArray rules = p["rules"] as JArray;
            if (rules == null) return false;

            foreach (JObject rule in rules)
            {
                string field = Convert.ToString(rule["field"]);
                string op = Convert.ToString(rule["op"]);
                string value = Convert.ToString(rule["value"]);

                if (!string.Equals(field, expectedField, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(op, expectedOp, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrWhiteSpace(expectedValue)
                    && !string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase)) continue;

                return true;
            }

            return false;
        }

        private static void CheckSemanticConditionBranch(PhraseClauseDiagnostic clause, JObject branchPlan, JArray checks, JArray warnings, JArray errors)
        {
            string role = clause.TaskRole;
            string expectedSide = SemanticBranchSideToPlanSide(clause.BranchSide);
            string expectedFieldKind = clause.ConditionKind;

            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "condition_branch",
                ["conditionKind"] = expectedFieldKind,
                ["expectedSide"] = expectedSide,
                ["expectedRole"] = role
            };

            if (string.IsNullOrWhiteSpace(role))
            {
                warnings.Add("Cláusula " + clause.Index + ": condición con rama pero sin rol/tarea detectada.");
                check["result"] = "warning_no_role";
                checks.Add(check);
                return;
            }

            JObject branch = FindBranchByFieldKind(branchPlan, expectedFieldKind);
            if (branch == null)
            {
                errors.Add("Cláusula " + clause.Index + ": el Phrase Engine detectó condición " + expectedFieldKind + " hacia " + role + ", pero el branchPlan legacy no tiene esa condición.");
                check["result"] = "missing_branch_plan";
                checks.Add(check);
                return;
            }

            string path = string.Equals(expectedSide, "true", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToString(branch["truePath"])
                : Convert.ToString(branch["falsePath"]);
            string actualRole = ExtractRoleFromPath(path);
            check["actualRole"] = actualRole;

            if (!string.Equals(actualRole, role, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Cláusula " + clause.Index + ": esperaba rama " + expectedSide + " hacia " + role + ", pero el grafo legacy la generó hacia " + actualRole + ".");
                check["result"] = "mismatch";
            }
            else
            {
                check["result"] = "ok";
            }

            checks.Add(check);
        }

        private static void CheckSemanticHumanTask(PhraseClauseDiagnostic clause, JArray actions, JArray checks, JArray warnings, JArray errors)
        {
            string role = clause.TaskRole;
            if (string.IsNullOrWhiteSpace(role))
            {
                warnings.Add("Cláusula " + clause.Index + ": se detectó tarea humana pero no se pudo resolver rol/usuario.");
                checks.Add(new JObject { ["clauseIndex"] = clause.Index, ["check"] = "human_task", ["result"] = "warning_no_role" });
                return;
            }

            bool exists = ActionExistsForNodeTypeAndRole(actions, "human.task", role);
            checks.Add(new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "human_task",
                ["expectedRole"] = role,
                ["result"] = exists ? "ok" : "missing_action"
            });

            if (!exists)
                errors.Add("Cláusula " + clause.Index + ": esperaba human.task para " + role + ", pero no existe en actions.");
        }

        private static void CheckSemanticNotification(PhraseClauseDiagnostic clause, JArray actions, JArray proposedConnections, JArray checks, JArray warnings, JArray errors)
        {
            string role = clause.NotifyRole;
            if (string.IsNullOrWhiteSpace(role))
            {
                warnings.Add("Cláusula " + clause.Index + ": se detectó notificación pero no se pudo resolver destino.");
                checks.Add(new JObject { ["clauseIndex"] = clause.Index, ["check"] = "notification", ["result"] = "warning_no_destination" });
                return;
            }

            bool exists = ActionExistsForNodeTypeAndRole(actions, "util.notify", role);
            var check = new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "notification",
                ["expectedRole"] = role,
                ["result"] = exists ? "ok" : "missing_action"
            };
            checks.Add(check);

            if (!exists)
            {
                errors.Add("Cláusula " + clause.Index + ": esperaba util.notify para " + role + ", pero el grafo legacy no generó notificación.");
                return;
            }

            // fix55: si la notificación vive dentro de un resultado humano
            // ("Si Dirección aprueba, notificar a Compras"), no alcanza con que exista
            // algún util.notify a Compras; debe estar en la rama APTO/NO APTO correcta.
            if (!string.IsNullOrWhiteSpace(clause.HumanOutcome) && !string.IsNullOrWhiteSpace(clause.Role))
            {
                string expectedCondition = string.Equals(clause.HumanOutcome, "no_apto", StringComparison.OrdinalIgnoreCase) ? "NO" : "SI";
                string resultIfLabel = FindHumanResultIfLabel(actions, clause.Role);

                // fix56b:
                // Antes se tomaba el primer util.notify del rol (por ejemplo la notificación de APTO)
                // y se verificaba contra la rama NO. Eso podía marcar falso error aunque en esa rama
                // existiera otra notificación al mismo rol después de un logger.
                // Ahora se busca un util.notify del rol esperado alcanzable desde la rama correcta.
                string notifyLabel = FindReachableNotifyLabelFromConditionalBranch(actions, proposedConnections, resultIfLabel, expectedCondition, role);
                bool reachable = !string.IsNullOrWhiteSpace(notifyLabel);

                checks.Add(new JObject
                {
                    ["clauseIndex"] = clause.Index,
                    ["check"] = "notification_branch",
                    ["humanRole"] = clause.Role,
                    ["outcome"] = clause.HumanOutcome,
                    ["expectedCondition"] = expectedCondition,
                    ["expectedNotifyRole"] = role,
                    ["resultIf"] = resultIfLabel,
                    ["notifyLabel"] = notifyLabel,
                    ["result"] = reachable ? "ok" : "mismatch"
                });

                if (!reachable)
                    errors.Add("Cláusula " + clause.Index + ": esperaba notificación a " + role + " en la rama " + expectedCondition + " del resultado de " + clause.Role + ", pero no está conectada en esa rama.");
            }
        }

        private static void CheckSemanticHumanResult(PhraseClauseDiagnostic clause, JArray actions, JArray proposedConnections, JArray checks, JArray warnings, JArray errors)
        {
            string role = clause.Role;
            string outcome = clause.HumanOutcome;
            if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(outcome))
            {
                warnings.Add("Cláusula " + clause.Index + ": resultado humano incompleto para validar.");
                checks.Add(new JObject { ["clauseIndex"] = clause.Index, ["check"] = "human_result", ["result"] = "warning_incomplete" });
                return;
            }

            bool taskExists = ActionExistsForNodeTypeAndRole(actions, "human.task", role);
            bool resultIfExists = HumanResultIfExists(actions, role);

            // fix62b: una cláusula con resultado humano no siempre pide logger.
            // Ejemplo validado: "Si Dirección la aprueba, notificar a COMPRAS..."
            // debe validar tarea + IF + notificación por rama, pero no logger.
            // Solo exigimos logger cuando la propia cláusula trae nivel de logger
            // (evento informativo / advertencia).
            bool expectsLogger = !string.IsNullOrWhiteSpace(clause.LoggerLevel);
            bool loggerExists = expectsLogger && LoggerForRoleAndOutcomeExists(actions, role, outcome);

            string result = taskExists && resultIfExists && (!expectsLogger || loggerExists) ? "ok" : "mismatch";
            checks.Add(new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "human_result",
                ["role"] = role,
                ["outcome"] = outcome,
                ["taskExists"] = taskExists,
                ["resultIfExists"] = resultIfExists,
                ["loggerExpected"] = expectsLogger,
                ["loggerExists"] = loggerExists,
                ["result"] = result
            });

            if (!taskExists)
                errors.Add("Cláusula " + clause.Index + ": resultado de tarea para " + role + ", pero no existe human.task para ese rol.");
            if (!resultIfExists)
                errors.Add("Cláusula " + clause.Index + ": resultado de tarea para " + role + ", pero no existe IF wf.tarea.resultado asociado.");
            if (expectsLogger && !loggerExists)
                errors.Add("Cláusula " + clause.Index + ": resultado " + outcome + " para " + role + ", pero no existe logger compatible.");

            // fix62: para múltiples tareas humanas no alcanza con que exista algún logger
            // compatible. El logger debe ser alcanzable desde la rama correcta del IF de
            // resultado de la tarea correspondiente (SI/APTO o NO/NO APTO). Esto evita que
            // un logger de COMPRAS satisfaga por error una rama de ADM_FIN, o viceversa.
            // fix62b: solo se aplica cuando la cláusula realmente pidió logger.
            if (expectsLogger && resultIfExists && loggerExists)
            {
                string expectedCondition = string.Equals(outcome, "no_apto", StringComparison.OrdinalIgnoreCase) ? "NO" : "SI";
                string resultIfLabel = FindHumanResultIfLabel(actions, role);
                string loggerLabel = FindReachableLoggerLabelFromConditionalBranch(actions, proposedConnections, resultIfLabel, expectedCondition, role, outcome);
                bool reachable = !string.IsNullOrWhiteSpace(loggerLabel);

                checks.Add(new JObject
                {
                    ["clauseIndex"] = clause.Index,
                    ["check"] = "human_result_logger_branch",
                    ["role"] = role,
                    ["outcome"] = outcome,
                    ["expectedCondition"] = expectedCondition,
                    ["resultIf"] = resultIfLabel,
                    ["loggerLabel"] = loggerLabel,
                    ["result"] = reachable ? "ok" : "mismatch"
                });

                if (!reachable)
                    errors.Add("Cláusula " + clause.Index + ": esperaba logger de resultado " + outcome + " para " + role + " en la rama " + expectedCondition + ", pero no está conectado en esa rama.");
            }
        }

        private static void CheckSemanticElseLoggerBranch(PhraseClauseDiagnostic clause, JArray actions, JArray proposedConnections, JArray checks, JArray warnings, JArray errors)
        {
            if (clause == null) return;
            string expectedLevel = string.IsNullOrWhiteSpace(clause.LoggerLevel) ? "Info" : clause.LoggerLevel;
            string mainIfLabel = FindFirstBusinessIfLabel(actions);
            bool reachable = PathContainsLoggerFromBranch(actions, proposedConnections, mainIfLabel, "NO", expectedLevel);

            checks.Add(new JObject
            {
                ["clauseIndex"] = clause.Index,
                ["check"] = "else_logger_branch",
                ["expectedCondition"] = "NO",
                ["expectedLoggerLevel"] = expectedLevel,
                ["mainIf"] = mainIfLabel,
                ["result"] = reachable ? "ok" : "mismatch"
            });

            if (!reachable)
                errors.Add("Cláusula " + clause.Index + ": esperaba logger " + expectedLevel + " en la rama NO del IF principal por 'caso contrario', pero el grafo legacy no lo generó en esa rama.");
        }

        private static void CheckGeneratedIfBranches(JArray actions, JArray proposedConnections, JArray checks, JArray warnings, JArray errors)
        {
            if (actions == null || proposedConnections == null) return;

            foreach (JObject action in actions)
            {
                if (!string.Equals(Convert.ToString(action["nodeType"]), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                string label = Convert.ToString(action["label"]);
                int trueCount = 0;
                int falseCount = 0;
                string trueTo = string.Empty;
                string falseTo = string.Empty;

                foreach (JObject conn in proposedConnections)
                {
                    if (!string.Equals(Convert.ToString(conn["from"]), label, StringComparison.OrdinalIgnoreCase)) continue;
                    // fix54b: Normalize() devuelve el texto con espacios centinela (" si ").
                    // Para comparar condiciones de proposedConnections hay que hacer Trim(),
                    // si no el diagnóstico cree erróneamente que no existen ramas SI/NO.
                    string cond = Normalize(Convert.ToString(conn["condition"])).Trim();
                    if (cond == "si" || cond == "true")
                    {
                        trueCount++;
                        trueTo = Convert.ToString(conn["to"]);
                    }
                    else if (cond == "no" || cond == "false")
                    {
                        falseCount++;
                        falseTo = Convert.ToString(conn["to"]);
                    }
                }

                var check = new JObject
                {
                    ["check"] = "control_if_branches",
                    ["label"] = label,
                    ["trueCount"] = trueCount,
                    ["falseCount"] = falseCount,
                    ["trueTo"] = trueTo,
                    ["falseTo"] = falseTo
                };

                if (trueCount == 1 && falseCount == 1 && !string.Equals(trueTo, falseTo, StringComparison.OrdinalIgnoreCase))
                {
                    check["result"] = "ok";
                }
                else
                {
                    check["result"] = "mismatch";
                    if (trueCount == 0) errors.Add("IF sin rama SI: " + label);
                    if (falseCount == 0) errors.Add("IF sin rama NO: " + label);
                    if (trueCount > 1) errors.Add("IF con más de una rama SI: " + label);
                    if (falseCount > 1) errors.Add("IF con más de una rama NO: " + label);
                    if (trueCount == 1 && falseCount == 1 && string.Equals(trueTo, falseTo, StringComparison.OrdinalIgnoreCase))
                        errors.Add("IF con SI y NO al mismo destino: " + label);
                }

                checks.Add(check);
            }
        }

        private static string SemanticBranchSideToPlanSide(string branchSide)
        {
            string s = branchSide ?? string.Empty;
            if (s.IndexOf("false", StringComparison.OrdinalIgnoreCase) >= 0) return "false";
            if (s.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0) return "true";
            return "true";
        }

        private static JObject FindBranchByFieldKind(JObject branchPlan, string fieldKind)
        {
            if (branchPlan == null || string.IsNullOrWhiteSpace(fieldKind)) return null;
            JArray branches = branchPlan["branches"] as JArray;
            if (branches == null) return null;
            foreach (JObject b in branches)
            {
                if (string.Equals(Convert.ToString(b["fieldKind"]), fieldKind, StringComparison.OrdinalIgnoreCase))
                    return b;
            }
            return null;
        }

        private static string ExtractRoleFromPath(string path)
        {
            string p = path ?? string.Empty;
            int idx = p.IndexOf(':');
            if (idx >= 0 && idx + 1 < p.Length) return p.Substring(idx + 1).Trim();
            return p.Trim();
        }

        private static bool ActionExistsForNodeTypeAndRole(JArray actions, string nodeType, string role)
        {
            if (actions == null || string.IsNullOrWhiteSpace(nodeType) || string.IsNullOrWhiteSpace(role)) return false;
            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), nodeType, StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (string.Equals(Convert.ToString(p["rol"]), role, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(Convert.ToString(p["rolDestino"]), role, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(Convert.ToString(p["destino"]), role, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool HumanResultIfExists(JArray actions, string role)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return false;
            string labelPart = Normalize(role);
            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["field"]), "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;
                string label = Normalize(Convert.ToString(a["label"]));
                if (label.Contains(labelPart)) return true;
            }
            return false;
        }

        private static bool LoggerForRoleAndOutcomeExists(JArray actions, string role, string outcome)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return false;
            string roleNorm = Normalize(role);
            string expectedLevel = string.Equals(outcome, "no_apto", StringComparison.OrdinalIgnoreCase) ? "Warn" : "Info";

            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), "util.logger", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["level"]), expectedLevel, StringComparison.OrdinalIgnoreCase)) continue;

                string label = Normalize(Convert.ToString(a["label"]));
                string message = Normalize(Convert.ToString(p["message"]));
                if (label.Contains(roleNorm) || message.Contains("direccion") || message.Contains("administracion") || message.Contains(roleNorm))
                    return true;
            }
            return false;
        }

        private static string ResolveHumanResultRoleForClause(string normalizedText)
        {
            string n = normalizedText ?? string.Empty;
            Match m = Regex.Match(n, @"\bsi\s+(?<actor>.*?)(?:\s+la|\s+lo|\s+el|\s+se)?\s+(?:aprueba|apruebe|aprobada|aprobado|rechaza|rechace|rechazada|rechazado)\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string actor = Normalize(m.Groups["actor"].Value).Trim();
                string role = ResolveRole(actor, null);
                if (!string.IsNullOrWhiteSpace(role)) return role;
            }
            return string.Empty;
        }

        private static string ResolveNotifyRoleForClause(string normalizedText)
        {
            string n = normalizedText ?? string.Empty;
            Match m = Regex.Match(n, @"\b(?:notificar|avisar|informar|mandar\s+aviso|dar\s+aviso)\s+a\s+(?<destino>.*?)(?:\s+indicando|\s+y\s+finalizar|\s+para|\s+que|\s*$)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string destino = Normalize(m.Groups["destino"].Value).Trim();
                string role = ResolveRole(destino, null);
                if (!string.IsNullOrWhiteSpace(role)) return role;
            }

            int idx = n.IndexOf("notificar", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = n.IndexOf("avisar", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = n.IndexOf("informar", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string tail = n.Substring(idx);
                string role = ResolveRole(tail, null);
                if (!string.IsNullOrWhiteSpace(role)) return role;
            }

            return string.Empty;
        }

        private static string FindHumanResultIfLabel(JArray actions, string role)
        {
            if (actions == null || string.IsNullOrWhiteSpace(role)) return string.Empty;
            string roleNorm = Normalize(role).Trim();
            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["field"]), "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;
                string label = Normalize(Convert.ToString(a["label"])).Trim();
                if (label.Contains(roleNorm)) return Convert.ToString(a["label"]);
            }
            return string.Empty;
        }

        private static string FindFirstBusinessIfLabel(JArray actions)
        {
            if (actions == null) return string.Empty;
            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p != null && string.Equals(Convert.ToString(p["field"]), "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;
                return Convert.ToString(a["label"]);
            }
            return string.Empty;
        }

        private static string FindActionLabelForNodeTypeAndRole(JArray actions, string nodeType, string role)
        {
            if (actions == null || string.IsNullOrWhiteSpace(nodeType) || string.IsNullOrWhiteSpace(role)) return string.Empty;
            foreach (JObject a in actions)
            {
                if (!string.Equals(Convert.ToString(a["nodeType"]), nodeType, StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = a["params"] as JObject;
                if (p == null) continue;
                if (string.Equals(Convert.ToString(p["rol"]), role, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Convert.ToString(p["rolDestino"]), role, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Convert.ToString(p["destino"]), role, StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(a["label"]);
                }
            }
            return string.Empty;
        }

        private static string FindReachableNotifyLabelFromConditionalBranch(JArray actions, JArray proposedConnections, string fromLabel, string condition, string role)
        {
            if (actions == null || proposedConnections == null || string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(role)) return string.Empty;

            string condExpected = Normalize(condition).Trim();
            var starts = new List<string>();
            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), fromLabel, StringComparison.OrdinalIgnoreCase)) continue;
                string cond = Normalize(Convert.ToString(c["condition"])).Trim();
                if (cond == condExpected || (condExpected == "si" && cond == "true") || (condExpected == "no" && cond == "false"))
                    starts.Add(Convert.ToString(c["to"]));
            }

            foreach (string start in starts)
            {
                string found = FindReachableNotifyLabel(actions, proposedConnections, start, role, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            return string.Empty;
        }

        private static string FindReachableNotifyLabel(JArray actions, JArray proposedConnections, string current, string role, int depth, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(role)) return string.Empty;
            if (depth > 30) return string.Empty;
            if (visited.Contains(current)) return string.Empty;
            visited.Add(current);

            JObject action = FindActionByLabel(actions, current);
            if (action != null
                && string.Equals(Convert.ToString(action["nodeType"]), "util.notify", StringComparison.OrdinalIgnoreCase)
                && ActionMatchesRole(action, role))
            {
                return Convert.ToString(action["label"]);
            }

            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), current, StringComparison.OrdinalIgnoreCase)) continue;
                string found = FindReachableNotifyLabel(actions, proposedConnections, Convert.ToString(c["to"]), role, depth + 1, visited);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            return string.Empty;
        }

        private static string FindReachableLoggerLabelFromConditionalBranch(JArray actions, JArray proposedConnections, string fromLabel, string condition, string role, string outcome)
        {
            if (actions == null || proposedConnections == null || string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(role)) return string.Empty;

            string condExpected = Normalize(condition).Trim();
            var starts = new List<string>();
            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), fromLabel, StringComparison.OrdinalIgnoreCase)) continue;
                string cond = Normalize(Convert.ToString(c["condition"])).Trim();
                if (cond == condExpected || (condExpected == "si" && cond == "true") || (condExpected == "no" && cond == "false"))
                    starts.Add(Convert.ToString(c["to"]));
            }

            foreach (string start in starts)
            {
                string found = FindReachableLoggerLabel(actions, proposedConnections, start, role, outcome, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            return string.Empty;
        }

        private static string FindReachableLoggerLabel(JArray actions, JArray proposedConnections, string current, string role, string outcome, int depth, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(role)) return string.Empty;
            if (depth > 30) return string.Empty;
            if (visited.Contains(current)) return string.Empty;
            visited.Add(current);

            JObject action = FindActionByLabel(actions, current);
            if (action != null)
            {
                string nodeType = Convert.ToString(action["nodeType"]);
                if (string.Equals(nodeType, "util.logger", StringComparison.OrdinalIgnoreCase)
                    && LoggerActionMatchesRoleAndOutcome(action, role, outcome))
                {
                    return Convert.ToString(action["label"]);
                }

                // En una rama de resultado humano no cruzamos otra tarea ni otro IF:
                // si ocurre eso, ya estamos en otra decisión y no en el resultado actual.
                if (string.Equals(nodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nodeType, "control.if", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
            }

            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), current, StringComparison.OrdinalIgnoreCase)) continue;
                string found = FindReachableLoggerLabel(actions, proposedConnections, Convert.ToString(c["to"]), role, outcome, depth + 1, visited);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }

            return string.Empty;
        }

        private static bool LoggerActionMatchesRoleAndOutcome(JObject action, string role, string outcome)
        {
            if (action == null || string.IsNullOrWhiteSpace(role)) return false;
            JObject p = action["params"] as JObject;
            if (p == null) return false;

            string expectedLevel = string.Equals(outcome, "no_apto", StringComparison.OrdinalIgnoreCase) ? "Warn" : "Info";
            string level = Convert.ToString(p["level"]);
            if (!string.Equals(level, expectedLevel, StringComparison.OrdinalIgnoreCase)) return false;

            string text = Normalize(Convert.ToString(action["label"]) + " " + Convert.ToString(p["message"]));
            string roleNorm = Normalize(role);

            if (text.Contains(roleNorm)) return true;
            if (string.Equals(role, "COMPRAS", StringComparison.OrdinalIgnoreCase) && text.Contains("compras")) return true;
            if (string.Equals(role, "DIR_GENERAL", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "direccion", "dir general", "dir_general")) return true;
            if (string.Equals(role, "ADM_FIN", StringComparison.OrdinalIgnoreCase) && ContainsAny(text, "administracion", "adm fin", "adm_fin", "finanzas")) return true;

            return false;
        }

        private static bool ActionMatchesRole(JObject action, string role)
        {
            if (action == null || string.IsNullOrWhiteSpace(role)) return false;
            JObject p = action["params"] as JObject;
            if (p == null) return false;

            return string.Equals(Convert.ToString(p["rol"]), role, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Convert.ToString(p["rolDestino"]), role, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Convert.ToString(p["destino"]), role, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReachableFromConditionalBranch(JArray proposedConnections, string fromLabel, string condition, string targetLabel)
        {
            if (proposedConnections == null || string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(targetLabel)) return false;
            string condExpected = Normalize(condition).Trim();
            var starts = new List<string>();
            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), fromLabel, StringComparison.OrdinalIgnoreCase)) continue;
                string cond = Normalize(Convert.ToString(c["condition"])).Trim();
                if (cond == condExpected || (condExpected == "si" && cond == "true") || (condExpected == "no" && cond == "false"))
                    starts.Add(Convert.ToString(c["to"]));
            }
            foreach (string start in starts)
            {
                if (IsReachableIgnoringConditions(proposedConnections, start, targetLabel, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static bool IsReachableIgnoringConditions(JArray proposedConnections, string current, string target, int depth, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(target)) return false;
            if (depth > 30) return false;
            if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (visited.Contains(current)) return false;
            visited.Add(current);

            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), current, StringComparison.OrdinalIgnoreCase)) continue;
                string next = Convert.ToString(c["to"]);
                if (IsReachableIgnoringConditions(proposedConnections, next, target, depth + 1, visited)) return true;
            }
            return false;
        }

        private static bool PathContainsLoggerFromBranch(JArray actions, JArray proposedConnections, string fromLabel, string condition, string expectedLevel)
        {
            if (actions == null || proposedConnections == null || string.IsNullOrWhiteSpace(fromLabel)) return false;
            string condExpected = Normalize(condition).Trim();
            var starts = new List<string>();
            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), fromLabel, StringComparison.OrdinalIgnoreCase)) continue;
                string cond = Normalize(Convert.ToString(c["condition"])).Trim();
                if (cond == condExpected || (condExpected == "no" && cond == "false") || (condExpected == "si" && cond == "true"))
                    starts.Add(Convert.ToString(c["to"]));
            }
            foreach (string start in starts)
            {
                if (ReachablePathContainsLogger(actions, proposedConnections, start, expectedLevel, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static bool ReachablePathContainsLogger(JArray actions, JArray proposedConnections, string current, string expectedLevel, int depth, HashSet<string> visited)
        {
            if (string.IsNullOrWhiteSpace(current) || depth > 30) return false;
            if (visited.Contains(current)) return false;
            visited.Add(current);

            JObject action = FindActionByLabel(actions, current);
            if (action != null)
            {
                string nodeType = Convert.ToString(action["nodeType"]);
                if (string.Equals(nodeType, "util.logger", StringComparison.OrdinalIgnoreCase))
                {
                    JObject p = action["params"] as JObject;
                    string level = p == null ? string.Empty : Convert.ToString(p["level"]);
                    if (string.IsNullOrWhiteSpace(expectedLevel) || string.Equals(level, expectedLevel, StringComparison.OrdinalIgnoreCase)) return true;
                }

                // Para validar un "caso contrario, registrar..." no aceptamos que la rama pase antes
                // por una tarea humana u otro IF: esa semántica implica acción directa de la rama NO.
                if (string.Equals(nodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nodeType, "control.if", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            foreach (JObject c in proposedConnections)
            {
                if (!string.Equals(Convert.ToString(c["from"]), current, StringComparison.OrdinalIgnoreCase)) continue;
                if (ReachablePathContainsLogger(actions, proposedConnections, Convert.ToString(c["to"]), expectedLevel, depth + 1, visited)) return true;
            }
            return false;
        }

        private static JObject FindActionByLabel(JArray actions, string label)
        {
            if (actions == null || string.IsNullOrWhiteSpace(label)) return null;
            foreach (JObject a in actions)
            {
                if (string.Equals(Convert.ToString(a["label"]), label, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }

        private static string ResolveHumanOutcome(string normalizedText)
        {
            string n = normalizedText ?? string.Empty;
            if (n.Contains("no apto") || n.Contains("rechaza") || n.Contains("rechazada") || n.Contains("rechazado")) return "no_apto";
            if (n.Contains("apto") || n.Contains("aprueba") || n.Contains("aprobada") || n.Contains("aprobado")) return "apto";
            return string.Empty;
        }

        private class PhraseEngineDiagnostic
        {
            public string OriginalText { get; set; }
            public string NormalizedText { get; set; }
            public List<PhraseClauseDiagnostic> Clauses { get; private set; }
            public List<string> Concepts { get; private set; }
            public List<string> NodeTypes { get; private set; }
            public string PrimaryRole { get; set; }
            public string PrimaryHumanOutcome { get; set; }
            public string FirstNumber { get; set; }

            public PhraseEngineDiagnostic()
            {
                OriginalText = string.Empty;
                NormalizedText = string.Empty;
                Clauses = new List<PhraseClauseDiagnostic>();
                Concepts = new List<string>();
                NodeTypes = new List<string>();
                PrimaryRole = string.Empty;
                PrimaryHumanOutcome = string.Empty;
                FirstNumber = string.Empty;
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

            public List<PhraseClauseDiagnostic> DebugClauses()
            {
                return Clauses;
            }
        }

        private class PhraseClauseDiagnostic
        {
            public int Index { get; set; }
            public string Text { get; set; }
            public string NormalizedText { get; set; }

            // fix53: diagnóstico semántico por cláusula.
            // Estos datos son solo diagnóstico; no modifican todavía la construcción legacy del grafo.
            public string ClauseType { get; set; }
            public string BranchMarker { get; set; }
            public string BranchSide { get; set; }
            public string Role { get; set; }
            public string TaskRole { get; set; }
            public string NotifyRole { get; set; }
            public string HumanOutcome { get; set; }
            public string LoggerLevel { get; set; }
            public string Number { get; set; }
            public string ConditionKind { get; set; }
            public string ConditionField { get; set; }
            public string ConditionOperator { get; set; }
            public string ConditionValue { get; set; }
            public bool IsNegative { get; set; }
            public string ActionHint { get; set; }
            public List<string> Concepts { get; private set; }
            public List<string> NodeTypes { get; private set; }

            public PhraseClauseDiagnostic()
            {
                Text = string.Empty;
                NormalizedText = string.Empty;
                ClauseType = string.Empty;
                BranchMarker = string.Empty;
                BranchSide = string.Empty;
                Role = string.Empty;
                TaskRole = string.Empty;
                NotifyRole = string.Empty;
                HumanOutcome = string.Empty;
                LoggerLevel = string.Empty;
                Number = string.Empty;
                ConditionKind = string.Empty;
                ConditionField = string.Empty;
                ConditionOperator = string.Empty;
                ConditionValue = string.Empty;
                IsNegative = false;
                ActionHint = string.Empty;
                Concepts = new List<string>();
                NodeTypes = new List<string>();
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
        }

        private class BranchAnalysis
        {
            public string CaeFalseRole { get; set; }
            public string TotalTrueRole { get; set; }
            public string TotalFalseRole { get; set; }
            public string TotalFalseActionLabel { get; set; }

            public BranchAnalysis()
            {
                CaeFalseRole = "";
                TotalTrueRole = "";
                TotalFalseRole = "";
                TotalFalseActionLabel = "";
            }

            public bool HasBranchInfo
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(CaeFalseRole)
                        || !string.IsNullOrWhiteSpace(TotalTrueRole)
                        || !string.IsNullOrWhiteSpace(TotalFalseRole)
                        || !string.IsNullOrWhiteSpace(TotalFalseActionLabel);
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
