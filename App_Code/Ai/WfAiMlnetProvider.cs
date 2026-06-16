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
            bool wantsCaeValidation = ContainsAny(norm, "cae", "cai");
            bool wantsDocument = docTipo.Length > 0 || HasIntent(predictions, "CARGAR_DOCUMENTO") || ContainsAny(norm, "cargar", "subir", "leer", "documento", "factura", "nota credito", " nc ");
            bool wantsHumanTask = role.Length > 0 || userKey.Length > 0 || HasIntent(predictions, "CREAR_TAREA_ROL") || HasIntent(predictions, "CONDICION_Y_TAREA") || ContainsAny(norm, "enviar a", "mandar a", "derivar a", "pasar a", "aprobar", "revision");
            bool wantsLogger = HasIntent(predictions, "REGISTRAR_LOG") || ContainsAny(norm, "log", "registrar", "dejar constancia");
            bool wantsEnd = HasIntent(predictions, "FINALIZAR_FLUJO") || ContainsAny(norm, "finalizar", "terminar", "fin del flujo");

            BranchAnalysis branches = AnalyzeBranches(norm, catalog, amount);
            bool branchTasksCreated = false;

            var actions = new JArray();
            var missing = new JArray();
            var warnings = new JArray();

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
                    actions.Add(AddNode("human.task", "Corregir en " + RoleFriendlyName(branches.CaeFalseRole), HumanTaskParams(branches.CaeFalseRole, "Corregir en " + RoleFriendlyName(branches.CaeFalseRole), "Rama negativa de CAE generada por el Asistente IA.")));
                    branchTasksCreated = true;
                }
                else if (ContainsAny(norm, "si no tiene cae", "si falta cae", "sin cae", "no tiene cae", "falta cae"))
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
                    actions.Add(AddNode("human.task", HumanTaskTitle(trueRole, ""), HumanTaskParams(trueRole, HumanTaskTitle(trueRole, ""), "Rama positiva de importe generada por el Asistente IA.")));
                    branchTasksCreated = true;
                }

                if (!string.IsNullOrWhiteSpace(falseRole))
                {
                    actions.Add(AddNode("human.task", HumanTaskTitle(falseRole, ""), HumanTaskParams(falseRole, HumanTaskTitle(falseRole, ""), "Rama negativa de importe generada por el Asistente IA.")));
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

            if (wantsHumanTask && !branchTasksCreated)
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

            warnings.Add("Proveedor ML.NET: interpretación local usando modelo entrenado externo. Todavía no se aplica al canvas automáticamente.");
            if (branches.HasBranchInfo)
                warnings.Add("Branch Planner v1: se detectaron ramas lógicas. La propuesta todavía no dibuja conexiones automáticamente en el canvas.");

            return new JObject
            {
                ["assistantVersion"] = "1.4-mlnet-docfield-resolver",
                ["intent"] = "build_workflow",
                ["confidence"] = AggregateConfidence(predictions),
                ["messageToUser"] = BuildMessage(actions, docTipo, role, userKey, amount, branches),
                ["actions"] = actions,
                ["missingData"] = missing,
                ["warnings"] = warnings,
                ["branchPlan"] = BuildBranchPlan(wantsCaeValidation, amount, branches),
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

            if (!string.IsNullOrWhiteSpace(role) && (branches == null || !branches.HasBranchInfo)) parts.Add("derivación al rol " + role);
            if (!string.IsNullOrWhiteSpace(userKey)) parts.Add("derivación al usuario " + userKey);

            if (parts.Count == 0)
                return "Recibí la intención y preparé una propuesta inicial para revisar.";

            return "Preparé una propuesta con " + string.Join(", ", parts.ToArray()) + ".";
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

            foreach (string clause in SplitClausesForBranches(normalizedText))
            {
                string c = Normalize(clause);
                string r = ResolveRole(c, catalog);
                if (string.IsNullOrWhiteSpace(r)) continue;

                bool mentionsCae = ContainsAny(c, "cae", "cai");
                bool caeNegative = mentionsCae && ContainsAny(c,
                    "no tiene cae", "no posee cae", "falta cae", "sin cae", "si no tiene cae", "si falta cae");

                bool totalNegative = ContainsAny(c,
                    "no supera", "no es mayor", "menor a", "menor que", "menor o igual", "no llega a", "por debajo");

                bool totalPositive = !totalNegative && ContainsAny(c,
                    "supera", "mayor a", "mayor que", "mas de", ">");

                if (caeNegative && string.IsNullOrWhiteSpace(b.CaeFalseRole))
                    b.CaeFalseRole = r;

                if (totalNegative && string.IsNullOrWhiteSpace(b.TotalFalseRole))
                    b.TotalFalseRole = r;
                else if (totalPositive && string.IsNullOrWhiteSpace(b.TotalTrueRole))
                    b.TotalTrueRole = r;
            }

            return b;
        }

        private static List<string> SplitClausesForBranches(string normalizedText)
        {
            var result = new List<string>();
            string t = Normalize(normalizedText).Trim();

            t = Regex.Replace(t, @"\s*,\s*", "|");
            t = Regex.Replace(t, @"\s+y\s+si\s+", "|si ", RegexOptions.IgnoreCase);
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

        private static bool ContainsPhrase(string normalizedText, string phrase)
        {
            string haystack = Normalize(normalizedText).Trim();
            string needle = Normalize(phrase).Trim();
            if (needle.Length == 0) return false;
            return (" " + haystack + " ").IndexOf(" " + needle + " ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (text == null) text = "";
            string padded = " " + text + " ";
            foreach (string v in values)
            {
                string n = Normalize(v);
                if (padded.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
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
