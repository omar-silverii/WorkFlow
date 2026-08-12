using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI.WebControls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_AiRegression : BasePage
    {
        protected override string[] RequiredPermissions { get { return new[] { "WF_ADMIN" }; } }

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Workflows"; } catch { }

            if (!IsPostBack)
            {
                BindCases();
                pnlIntro.Visible = true;
                litSummary.Text = "";
                litDetails.Text = "";
            }
        }

        protected void btnRunSelected_Click(object sender, EventArgs e)
        {
            pnlIntro.Visible = false;

            var cases = LoadCases();
            string id = (ddlCases.SelectedValue ?? "").Trim();
            var item = cases.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                lblMessage.CssClass = "small mt-2 d-block text-danger";
                lblMessage.Text = "No encontré el caso seleccionado.";
                return;
            }

            RenderResults(new List<AiRegressionRunResult> { RunCase(item) }, null);
        }

        protected void btnRunAll_Click(object sender, EventArgs e)
        {
            pnlIntro.Visible = false;

            var cases = LoadCases();
            var results = new List<AiRegressionRunResult>();

            foreach (var item in cases)
                results.Add(RunCase(item));

            RenderResults(results, RunConstructionEquivalence());
        }

        private void BindCases()
        {
            ddlCases.Items.Clear();
            foreach (var item in LoadCases())
            {
                string suffix = item.Enabled ? "" : " (deshabilitado)";
                ddlCases.Items.Add(new ListItem(item.Id + " — " + item.Name + suffix, item.Id));
            }
        }

        private List<AiRegressionCase> LoadCases()
        {
            string path = Server.MapPath("~/App_Data/WF_AI/ai_regression_cases.json");
            if (!File.Exists(path))
                return BuiltInCases();

            string json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonConvert.DeserializeObject<List<AiRegressionCase>>(json) ?? new List<AiRegressionCase>();

            if (list.Count == 0)
                return BuiltInCases();

            foreach (var item in list)
                item.EnsureDefaults();

            return list;
        }

        private AiRegressionRunResult RunCase(AiRegressionCase item)
        {
            var run = new AiRegressionRunResult
            {
                Case = item,
                StartedAt = DateTime.Now,
                Checks = new List<AiRegressionCheck>(),
                PlanJson = "",
                NodeTypes = ExpectedNodeTypes(item),
                SemanticStrong = IsStrongSemanticCase(item)
            };

            if (!item.Enabled)
            {
                run.Status = "SKIP";
                run.Checks.Add(AiRegressionCheck.Skip("Caso deshabilitado en ai_regression_cases.json."));
                return run;
            }

            try
            {
                var catalog = new WfAiCatalogProvider().Build();
                var model = new WfAiMlnetProvider().Interpret(item.Phrase, catalog, "");

                if (model == null || !model.Ok || model.Plan == null)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("El proveedor IA no devolvió un plan válido. " + Safe(model == null ? "" : model.ErrorMessage)));
                    run.Status = "FALLA";
                    return run;
                }

                WfAiResolvedPlanResult initialCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, item.Phrase, "phrase");
                JObject plan = initialCommon.Plan;

                // FIX84A/B inspecciona el borrador inicial porque allí deben aparecer las dudas reales.
                EvaluateFix84A(run, item, plan, catalog);

                // FIX84B3/FIX84C2B: las validaciones funcionales y la equivalencia común deben
                // mirar el plan resultante del diálogo. Así un dato faltante que se resuelve
                // correctamente (por ejemplo el contenido de queue.publish) no cuenta como falla.
                JObject evaluatedPlan = EvaluateFix84BDialogue(run, item, plan, catalog) ?? plan;
                WfAiResolvedPlanResult effectiveCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(evaluatedPlan, item.Phrase, "phrase_resolved");
                evaluatedPlan = effectiveCommon.Plan;
                EvaluateFix84C1Common(run, effectiveCommon);

                var validation = new WfAiPlanValidator().Validate(evaluatedPlan, catalog);
                foreach (string error in effectiveCommon.Errors)
                {
                    validation.Ok = false;
                    validation.Errors.Add(error);
                }
                foreach (string warning in effectiveCommon.Warnings)
                    validation.Warnings.Add(warning);

                EvaluateValidation(run, item, validation);

                run.PlanJson = evaluatedPlan.ToString(Formatting.Indented);
                run.NodeTypes = ExtractNodeTypes(evaluatedPlan, item);
                EvaluateSemantic(run, item, evaluatedPlan);
                EvaluateNodes(run, item, evaluatedPlan);
                EvaluateConnections(run, item, evaluatedPlan);

                run.Status = run.Checks.Any(x => !x.Ok && !x.Skipped) ? "FALLA" : "OK";
            }
            catch (Exception ex)
            {
                run.Checks.Add(AiRegressionCheck.Fail("Excepción ejecutando el caso: " + ex.Message));
                run.Status = "FALLA";
            }

            return run;
        }

        private static void EvaluateFix84C1Common(AiRegressionRunResult run, WfAiResolvedPlanResult common)
        {
            if (common == null || common.Nodes == null || common.Nodes.Count == 0)
            {
                run.Checks.Add(AiRegressionCheck.Skip("fix84c2b: el caso no contiene nodos cubiertos por la capa común."));
                return;
            }

            if (common.Errors != null && common.Errors.Count > 0)
            {
                run.Checks.Add(AiRegressionCheck.Fail("fix84c2b nodo resuelto común: " + string.Join(" | ", common.Errors.ToArray())));
                return;
            }

            run.Checks.Add(AiRegressionCheck.Pass("fix84c2b nodo resuelto común OK: " + common.Nodes.Count + " nodo(s) común(es) normalizado(s)."));
        }

        private static void EvaluateValidation(AiRegressionRunResult run, AiRegressionCase item, WfAiValidationResult validation)
        {
            if (!item.Expected.CheckValidation)
            {
                run.Checks.Add(AiRegressionCheck.Skip("Validador funcional no requerido para este caso."));
                return;
            }

            bool expected = item.Expected.ValidationOk;
            bool actual = validation != null && validation.Ok;

            if (expected == actual)
                run.Checks.Add(AiRegressionCheck.Pass("Validador funcional OK = " + actual));
            else
                run.Checks.Add(AiRegressionCheck.Fail("Validador funcional esperado " + expected + " pero fue " + actual + ". Errores: " + JoinList(validation == null ? null : validation.Errors)));
        }

        // fix84a: prueba directa de la capa contractual sin cambiar las expectativas legacy del caso.
        // Un caso completo que ya no tiene missingData tampoco debe adquirir dudas bloqueantes nuevas
        // por valores genéricos o por una definición contractual inconsistente.
        private static void EvaluateFix84A(AiRegressionRunResult run, AiRegressionCase item, JObject plan, WfAiCatalog catalog)
        {
            try
            {
                var draft = new WfAiInterpretationDraftBuilder().Build(item == null ? "" : item.Phrase, plan, catalog);

                if (draft.RegistryErrors != null && draft.RegistryErrors.Count > 0)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84a contrato inválido: " + string.Join("; ", draft.RegistryErrors.ToArray())));
                    return;
                }

                int coveredActions = 0;
                var actions = plan == null ? null : plan["actions"] as JArray;
                if (actions != null)
                {
                    foreach (JToken token in actions)
                    {
                        JObject action = token as JObject;
                        if (action == null) continue;
                        string nodeType = Convert.ToString(action["nodeType"] ?? "").Trim();
                        if (WfAiConstructionContractRegistry.Find(nodeType) != null) coveredActions++;
                    }
                }

                if (coveredActions == 0)
                {
                    run.Checks.Add(AiRegressionCheck.Skip("fix84a: el caso no contiene nodos de la muestra contractual inicial."));
                    return;
                }

                if (draft.Nodes.Count != coveredActions)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84a interpretó " + draft.Nodes.Count + " de " + coveredActions + " nodo(s) cubierto(s)."));
                    return;
                }

                var legacyMissing = plan == null ? null : plan["missingData"] as JArray;
                bool legacyPlanComplete = legacyMissing == null || legacyMissing.Count == 0;
                if (legacyPlanComplete && draft.BlockingClarificationCount > 0)
                {
                    if (item != null && item.Dialogue != null && item.Dialogue.Enabled)
                    {
                        run.Checks.Add(AiRegressionCheck.Pass("fix84b detectó " + draft.BlockingClarificationCount + " aclaración(es) bloqueante(s) prevista(s) para el diálogo."));
                        return;
                    }

                    var questions = new List<string>();
                    foreach (WfAiClarification clarification in draft.Clarifications)
                    {
                        if (clarification != null && clarification.Blocking && !string.IsNullOrWhiteSpace(clarification.Question))
                            questions.Add(clarification.Question);
                    }
                    run.Checks.Add(AiRegressionCheck.Fail("fix84a agregó aclaraciones bloqueantes a un plan legacy completo: " + string.Join(" | ", questions.ToArray())));
                    return;
                }

                run.Checks.Add(AiRegressionCheck.Pass("fix84a contrato OK: " + coveredActions + " nodo(s) cubierto(s), aclaraciones bloqueantes=" + draft.BlockingClarificationCount + "."));
            }
            catch (Exception ex)
            {
                run.Checks.Add(AiRegressionCheck.Fail("fix84a lanzó una excepción: " + ex.Message));
            }
        }

        // fix84b: valida una conversación contractual completa sin reescribir la frase original.
        // El caso declara la duda esperada y una respuesta estructurada; el servidor debe aplicar
        // la decisión sobre una copia del plan y regenerar un borrador sin aclaraciones bloqueantes.
        private static JObject EvaluateFix84BDialogue(AiRegressionRunResult run, AiRegressionCase item, JObject plan, WfAiCatalog catalog)
        {
            if (item == null || item.Dialogue == null || !item.Dialogue.Enabled)
                return plan;

            try
            {
                var builder = new WfAiInterpretationDraftBuilder();
                var initialDraft = builder.Build(item.Phrase, plan, catalog);

                if (string.IsNullOrWhiteSpace(initialDraft.Fingerprint))
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b no generó fingerprint para el borrador inicial."));
                    return plan;
                }

                if (initialDraft.BlockingClarificationCount != item.Dialogue.ExpectedInitialBlocking)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b esperaba " + item.Dialogue.ExpectedInitialBlocking + " aclaración(es) inicial(es), pero obtuvo " + initialDraft.BlockingClarificationCount + "."));
                    return plan;
                }

                if (!string.IsNullOrWhiteSpace(item.Dialogue.ExpectedQuestionContains))
                {
                    bool found = initialDraft.Clarifications.Any(x => x != null
                        && x.Blocking
                        && (x.Question ?? "").IndexOf(item.Dialogue.ExpectedQuestionContains, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!found)
                    {
                        run.Checks.Add(AiRegressionCheck.Fail("fix84b no generó la pregunta esperada que contiene: " + item.Dialogue.ExpectedQuestionContains));
                        return plan;
                    }
                }

                var resolution = new WfAiClarificationResolver().Resolve(plan, initialDraft, item.Dialogue.Answers, catalog);
                if (resolution.Errors != null && resolution.Errors.Count > 0)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b no pudo aplicar la respuesta estructurada: " + string.Join(" | ", resolution.Errors.ToArray())));
                    return plan;
                }

                WfAiResolvedPlanResult finalCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(resolution.Plan, item.Phrase, "phrase");
                JObject finalPlan = finalCommon.Plan;

                var finalDraft = builder.Build(
                    item.Phrase,
                    finalPlan,
                    catalog,
                    resolution.AcceptedAnswerIds,
                    initialDraft.Fingerprint);

                if (finalDraft.BlockingClarificationCount != item.Dialogue.ExpectedFinalBlocking)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b esperaba " + item.Dialogue.ExpectedFinalBlocking + " aclaración(es) finales, pero obtuvo " + finalDraft.BlockingClarificationCount + "."));
                    return plan;
                }

                if (!string.Equals(initialDraft.SourceText, item.Phrase, StringComparison.Ordinal)
                    || !string.Equals(finalDraft.SourceText, item.Phrase, StringComparison.Ordinal))
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b alteró la frase original durante la aclaración."));
                    return plan;
                }

                if (!string.Equals(initialDraft.Fingerprint, finalDraft.Fingerprint, StringComparison.Ordinal))
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b cambió el fingerprint del borrador base durante la misma conversación."));
                    return plan;
                }

                var finalValidation = new WfAiPlanValidator().Validate(finalPlan, catalog);
                if (finalValidation != null)
                {
                    foreach (string error in finalCommon.Errors)
                    {
                        finalValidation.Ok = false;
                        finalValidation.Errors.Add(error);
                    }
                }
                if (finalValidation == null || !finalValidation.Ok)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("fix84b dejó un plan inválido después de resolver el diálogo. Errores: " + JoinList(finalValidation == null ? null : finalValidation.Errors)));
                    return plan;
                }

                run.Checks.Add(AiRegressionCheck.Pass(
                    "fix84b diálogo OK: " + initialDraft.BlockingClarificationCount
                    + " duda(s) inicial(es) -> " + finalDraft.BlockingClarificationCount
                    + ", respuesta estructurada aplicada sin reescribir la frase; validaciones posteriores usan el plan resuelto."));

                return finalPlan;
            }
            catch (Exception ex)
            {
                run.Checks.Add(AiRegressionCheck.Fail("fix84b diálogo lanzó una excepción: " + ex.Message));
                return plan;
            }
        }

        private static void EvaluateSemantic(AiRegressionRunResult run, AiRegressionCase item, JObject plan)
        {
            bool anySemanticCheck = item.Expected.CheckSemanticOk || item.Expected.CheckSemanticWarnings || item.Expected.CheckSemanticErrors;

            if (!anySemanticCheck)
            {
                run.Checks.Add(AiRegressionCheck.Skip("Auditor semántico no requerido para este caso legacy. Se validan nodos y conexiones."));
                return;
            }

            bool actualSemanticOk = ReadBool(plan.SelectToken("mlnet.resolved.phraseSemanticConsistencyOk"));
            if (item.Expected.CheckSemanticOk)
            {
                if (item.Expected.SemanticOk == actualSemanticOk)
                    run.Checks.Add(AiRegressionCheck.Pass("phraseSemanticConsistencyOk = " + actualSemanticOk));
                else
                    run.Checks.Add(AiRegressionCheck.Fail("phraseSemanticConsistencyOk esperado " + item.Expected.SemanticOk + " pero fue " + actualSemanticOk));
            }
            else
            {
                run.Checks.Add(AiRegressionCheck.Skip("phraseSemanticConsistencyOk no requerido para este caso."));
            }

            var semanticWarnings = plan.SelectToken("mlnet.resolved.phraseSemanticConsistency.warnings") as JArray;
            var semanticErrors = plan.SelectToken("mlnet.resolved.phraseSemanticConsistency.errors") as JArray;

            if (item.Expected.CheckSemanticWarnings)
            {
                int count = semanticWarnings == null ? 0 : semanticWarnings.Count;
                if (!item.Expected.SemanticWarningsEmpty)
                {
                    run.Checks.Add(AiRegressionCheck.Skip("phraseSemanticConsistency.warnings no exige vacío en este caso."));
                }
                else if (count == 0)
                    run.Checks.Add(AiRegressionCheck.Pass("phraseSemanticConsistency.warnings vacío"));
                else
                    run.Checks.Add(AiRegressionCheck.Fail("phraseSemanticConsistency.warnings debía estar vacío. Cantidad: " + count));
            }

            if (item.Expected.CheckSemanticErrors)
            {
                int count = semanticErrors == null ? 0 : semanticErrors.Count;
                if (!item.Expected.SemanticErrorsEmpty)
                {
                    run.Checks.Add(AiRegressionCheck.Skip("phraseSemanticConsistency.errors no exige vacío en este caso."));
                }
                else if (count == 0)
                    run.Checks.Add(AiRegressionCheck.Pass("phraseSemanticConsistency.errors vacío"));
                else
                    run.Checks.Add(AiRegressionCheck.Fail("phraseSemanticConsistency.errors debía estar vacío. Cantidad: " + count));
            }
        }

        private static void EvaluateNodes(AiRegressionRunResult run, AiRegressionCase item, JObject plan)
        {
            if (!item.Expected.CheckNodes)
            {
                run.Checks.Add(AiRegressionCheck.Skip("Nodos esperados no requeridos para este caso."));
                return;
            }

            var actions = plan["actions"] as JArray;
            if (actions == null)
            {
                run.Checks.Add(AiRegressionCheck.Fail("El plan no contiene actions[]."));
                return;
            }

            foreach (var expected in item.Expected.Nodes)
            {
                JObject found = FindNode(actions, expected);
                if (found == null)
                {
                    run.Checks.Add(AiRegressionCheck.Fail("Nodo esperado no encontrado: " + expected.Type + " / " + expected.Label));
                    continue;
                }

                var missingParams = new List<string>();
                JObject paramsObj = found["params"] as JObject;

                foreach (var p in expected.Params)
                {
                    string actual = paramsObj == null ? "" : Convert.ToString(paramsObj[p.Key] ?? "").Trim();
                    string exp = (p.Value ?? "").Trim();
                    if (!string.Equals(actual, exp, StringComparison.OrdinalIgnoreCase))
                        missingParams.Add(p.Key + " esperado=" + exp + " actual=" + actual);
                }

                if (missingParams.Count == 0)
                    run.Checks.Add(AiRegressionCheck.Pass("Nodo OK: " + expected.Type + " / " + expected.Label));
                else
                    run.Checks.Add(AiRegressionCheck.Fail("Nodo con parámetros distintos: " + expected.Type + " / " + expected.Label + ". " + string.Join("; ", missingParams.ToArray())));
            }
        }

        private static void EvaluateConnections(AiRegressionRunResult run, AiRegressionCase item, JObject plan)
        {
            if (!item.Expected.CheckConnections)
            {
                run.Checks.Add(AiRegressionCheck.Skip("Conexiones esperadas no requeridas para este caso."));
                return;
            }

            var proposed = plan["proposedConnections"] as JArray;
            if (proposed == null)
            {
                run.Checks.Add(AiRegressionCheck.Fail("El plan no contiene proposedConnections[]."));
                return;
            }

            foreach (var expected in item.Expected.Connections)
            {
                JObject found = FindConnection(proposed, expected);
                if (found != null)
                    run.Checks.Add(AiRegressionCheck.Pass("Conexión OK: " + expected.From + " -> " + expected.To + FormatCondition(expected.Condition)));
                else
                    run.Checks.Add(AiRegressionCheck.Fail("Conexión esperada no encontrada: " + expected.From + " -> " + expected.To + FormatCondition(expected.Condition)));
            }
        }

        private void RenderResults(List<AiRegressionRunResult> results, ConstructionEquivalenceSummary equivalence)
        {
            int ok = results.Count(x => x.Status == "OK");
            int fail = results.Count(x => x.Status == "FALLA");
            int skip = results.Count(x => x.Status == "SKIP");

            lblMessage.CssClass = fail > 0 ? "small mt-2 d-block text-danger" : "small mt-2 d-block text-success";
            lblMessage.Text = "Resultado: OK=" + ok + " / FALLA=" + fail + " / SKIP=" + skip + ".";

            var allNodeTypes = results
                .SelectMany(x => x.NodeTypes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"card ws-card mb-3\"><div class=\"card-body\">");
            sb.AppendLine("<div class=\"d-flex align-items-center justify-content-between flex-wrap gap-2 mb-3\"><div><div class=\"fw-bold\">Resumen</div><div class=\"small ws-muted\">Ejecución: " + Html(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")) + "</div></div><span id=\"wsAiVisibleCount\" class=\"ws-chip\">" + results.Count + " caso(s) visibles</span></div>");

            sb.AppendLine("<div class=\"ws-stat-grid mb-3\">");
            sb.AppendLine(RenderStat("OK", ok, "ws-badge-ok"));
            sb.AppendLine(RenderStat("FALLA", fail, "ws-badge-fail"));
            sb.AppendLine(RenderStat("SKIP", skip, "ws-badge-skip"));
            sb.AppendLine(RenderStat("Semántica fuerte", results.Count(x => x.SemanticStrong), "ws-semantic-chip"));
            sb.AppendLine("</div>");

            sb.AppendLine("<div class=\"ws-filterbar mb-3\">");
            sb.AppendLine("<span class=\"small fw-bold me-1\">Filtro estado</span>");
            sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-filter-btn active\" data-status=\"\">Todos</button>");
            sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-success ws-ai-filter-btn\" data-status=\"OK\">OK</button>");
            sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-danger ws-ai-filter-btn\" data-status=\"FALLA\">FALLA</button>");
            sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-filter-btn\" data-status=\"SKIP\">SKIP</button>");
            sb.AppendLine("<span class=\"small fw-bold ms-md-2\">Tipo de nodo</span>");
            sb.AppendLine("<select id=\"wsAiNodeFilter\" class=\"form-select form-select-sm\" style=\"width:auto; min-width:220px\">");
            sb.AppendLine("<option value=\"\">Todos los tipos</option>");
            foreach (string nodeType in allNodeTypes)
                sb.AppendLine("<option value=\"" + Attr(nodeType) + "\">" + Html(nodeType) + "</option>");
            sb.AppendLine("</select>");
            sb.AppendLine("</div>");

            sb.AppendLine(RenderNodeTypeSummary(results));

            sb.AppendLine("<div class=\"ws-table-wrap\"><table class=\"table table-sm table-hover mb-0\">");
            sb.AppendLine("<thead class=\"table-light\"><tr><th>Caso</th><th>Estado</th><th>Tipo</th><th>Checks</th><th>Frase</th><th style=\"width:260px\">Acciones</th></tr></thead><tbody>");

            foreach (var r in results)
            {
                string badge = r.Status == "OK" ? "ws-badge-ok" : (r.Status == "SKIP" ? "ws-badge-skip" : "ws-badge-fail");
                int checksOk = r.Checks.Count(x => x.Ok);
                int checksFail = r.Checks.Count(x => !x.Ok && !x.Skipped);
                int checksSkip = r.Checks.Count(x => x.Skipped);
                string nodeTypes = NodeTypesData(r.NodeTypes);

                sb.AppendLine("<tr class=\"ws-ai-case-row\" data-status=\"" + Attr(r.Status) + "\" data-node-types=\"" + Attr(nodeTypes) + "\">");
                sb.AppendLine("<td><strong>" + Html(r.Case.Id) + "</strong><br/><span class=\"small ws-muted\">" + Html(r.Case.Name) + "</span>" + RenderSemanticSmall(r) + "</td>");
                sb.AppendLine("<td><span class=\"ws-chip " + badge + "\">" + Html(r.Status) + "</span></td>");
                sb.AppendLine("<td>" + RenderNodeTypes(r.NodeTypes, 3) + "</td>");
                sb.AppendLine("<td><span class=\"ws-check-ok\">OK " + checksOk + "</span> / <span class=\"ws-check-fail\">FALLA " + checksFail + "</span><span class=\"ws-muted\"> / SKIP " + checksSkip + "</span></td>");
                sb.AppendLine("<td class=\"small\">" + Html(TrimForTable(r.Case.Phrase)) + "</td>");
                sb.AppendLine("<td><div class=\"ws-case-actions ws-case-actions-compact\">");
                sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-copy-btn\" data-copy-value=\"" + Attr(r.Case.Phrase) + "\" data-copy-ok=\"Frase copiada.\">Copiar frase</button>");
                sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-primary ws-ai-open-constructor-btn\" data-phrase=\"" + Attr(r.Case.Phrase) + "\">Abrir Constructor</button>");
                if (!string.IsNullOrWhiteSpace(r.PlanJson))
                    sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-copy-btn\" data-copy-target=\"wsAiJson_" + SafeDomId(r.Case.Id) + "\" data-copy-ok=\"JSON técnico copiado.\">Copiar JSON</button>");
                sb.AppendLine("</div></td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table></div>");
            sb.AppendLine("</div></div>");
            if (equivalence != null)
                sb.AppendLine(RenderConstructionEquivalence(equivalence));
            litSummary.Text = sb.ToString();

            litDetails.Text = RenderDetails(results);
        }

        private ConstructionEquivalenceSummary RunConstructionEquivalence()
        {
            var summary = new ConstructionEquivalenceSummary();
            try
            {
                var catalog = new WfAiCatalogProvider().Build();
                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C1_LOGGER",
                    "util.logger",
                    "Registrar Operación completada.",
                    new[] { "util.start", "util.logger", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "util.logger",
                        ["label"] = "Registrar evento",
                        ["params"] = new JObject { ["message"] = "Operación completada" }
                    }));

                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C1_QUEUE_CONSUME",
                    "queue.consume",
                    "Usar una cola llamada Pedidos y después leer un mensaje.",
                    new[] { "util.start", "queue.consume", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "queue.consume",
                        ["label"] = "Consumir cola Pedidos",
                        ["params"] = new JObject { ["queue"] = "Pedidos", ["take"] = 1 }
                    }));

                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C2A_QUEUE_PUBLISH_TEXT",
                    "queue.publish",
                    "Publicar en la cola Pedidos quiero un mate.",
                    new[] { "util.start", "queue.publish", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "queue.publish",
                        ["label"] = "Publicar en cola Pedidos",
                        ["params"] = new JObject
                        {
                            ["queue"] = "Pedidos",
                            ["payload"] = "quiero un mate"
                        }
                    }));

                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C2A_QUEUE_PUBLISH_FIELDS",
                    "queue.publish",
                    "Publicar en la cola Pedidos origen: Prueba e instanceId: ${wf.instanceId}.",
                    new[] { "util.start", "queue.publish", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "queue.publish",
                        ["label"] = "Publicar en cola Pedidos",
                        ["params"] = new JObject
                        {
                            ["queue"] = "Pedidos",
                            ["payload"] = new JObject
                            {
                                ["origen"] = "Prueba",
                                ["instanceId"] = "${wf.instanceId}"
                            }
                        }
                    }));


                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C2AB_QUEUE_PUBLISH_EXPLICIT_ASSIGNMENTS",
                    "queue.publish",
                    "Publicar en la cola Pedidos con origen = Prueba; instancia = actual.",
                    new[] { "util.start", "queue.publish", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "queue.publish",
                        ["label"] = "Publicar en cola Pedidos",
                        ["params"] = new JObject
                        {
                            ["queue"] = "Pedidos",
                            ["payload"] = new JObject
                            {
                                ["origen"] = "Prueba",
                                ["instancia"] = "${wf.instanceId}"
                            }
                        }
                    }));


                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C2B_HUMAN_TASK_ROLE",
                    "human.task",
                    "Crear una tarea. Rol = COMPRAS; Título = Revisar factura.",
                    new[] { "util.start", "human.task", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "human.task",
                        ["label"] = "Tarea humana",
                        ["params"] = new JObject
                        {
                            ["rol"] = "COMPRAS",
                            ["titulo"] = "Revisar factura"
                        }
                    },
                    true));

                summary.Items.Add(RunEquivalenceCase(
                    catalog,
                    "C2B_HUMAN_TASK_USER",
                    "human.task",
                    "Crear una tarea. Usuario = USUARIO1; Título = Revisar factura.",
                    new[] { "util.start", "human.task", "util.end" },
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "human.task",
                        ["label"] = "Tarea humana",
                        ["params"] = new JObject
                        {
                            ["usuarioAsignado"] = "OMARD\\USUARIO1",
                            ["titulo"] = "Revisar factura"
                        }
                    },
                    true));

                summary.Items.Add(RunHumanTaskAmbiguityCase(catalog));

                // FIX84C2Bg2: en diseño, human.task expone APTO / NO_APTO.
                // RECHAZADO / Volver atrás queda fuera del Constructor y pertenece a la ejecución.
                summary.Items.Add(RunC2Bg2SingleNegativeBranchCase(
                    catalog,
                    "C2BG2_NO_APTO_EXPLICITO",
                    "Crear una tarea. Rol = COMPRAS; Título = Revisar factura. Si Compras indica NO APTO, registrar una advertencia indicando que debe corregirse."));

                summary.Items.Add(RunC2Bg2SingleNegativeBranchCase(
                    catalog,
                    "C2BG2_RECHAZA_COMO_NO_APTO",
                    "Crear una tarea. Rol = COMPRAS; Título = Revisar factura. Si Compras la rechaza, registrar una advertencia indicando que debe corregirse."));

                summary.Items.Add(RunC2Bg2SiblingBranchesCase(catalog));

                // FIX84C2Bg2b: "registrar una advertencia" es una intención explícita
                // de util.logger Warn; no debe convertirse en util.notify ni requerir una
                // segunda decisión funcional. La comprobación del fallback visual se completa
                // manualmente en WorkflowUI porque esa capa vive en JavaScript cliente.
                summary.Items.Add(RunC2Bg2bExplicitWarningLoggerCase(catalog));

                // FIX84C2Bg2c: el contrato objetivo de Paso a paso para una Human Task con
                // APTO/NO APTO debe converger al mismo tramo técnico que Frase y usar un solo Fin.
                // La ausencia del falso fallback SI/NO se valida además manualmente en WorkflowUI.
                summary.Items.Add(RunC2Bg2cStepByStepHumanBranchesCase(catalog));

                // FIX84C2Bg2d: un valor ya elegido dentro de Paso a paso es dato declarativo.
                // Message = "Informar" debe seguir siendo texto de util.logger y nunca convertirse
                // en util.notify ni en una nueva intención al normalizar el plan.
                summary.Items.Add(RunC2Bg2dDeclarativeMessageCase(catalog));

                // FIX84C2C1: control.if entra al camino común contractual. En esta etapa no
                // reemplazamos el parser natural histórico; verificamos que candidatos equivalentes
                // de Frase y Paso a paso terminen en la misma definición canónica.
                summary.Items.Add(RunC2C1ControlIfSimpleCase(catalog));
                summary.Items.Add(RunC2C1ControlIfNoValueCase(catalog));
                summary.Items.Add(RunC2C1ControlIfAllCase(catalog));
                summary.Items.Add(RunC2C1ControlIfAnyCase(catalog));

                // FIX84C2C2: ahora probamos Frase natural real contra el mismo contrato C2C1.
                // No se cambian los casos históricos A-S para hacer pasar esta serie.
                summary.Items.Add(RunC2C2NaturalTotalCase(catalog));
                summary.Items.Add(RunC2C2NaturalNoSuperaCase(catalog));
                summary.Items.Add(RunC2C2NaturalCaeCase(catalog));
                summary.Items.Add(RunC2C2NaturalAllCase(catalog));
                summary.Items.Add(RunC2C2NaturalAnyCase(catalog));
                // FIX84C2C2b: prueba de punta a punta de acciones naturales en ambas ramas.
                summary.Items.Add(RunC2C2bNaturalBranchActionsCase(catalog));

                // FIX84C2D1: util.notify entra al camino contractual común. Protegemos destino
                // por rol, usuario humano corto resuelto contra catálogo y diálogo cuando falta mensaje.
                summary.Items.Add(RunC2D1NotifyRoleCase(catalog));
                summary.Items.Add(RunC2D1NotifyUserCase(catalog));
                summary.Items.Add(RunC2D1NotifyMissingMessageCase(catalog));

                // FIX84C2D2: file.write entra al camino contractual común. Protegemos texto
                // literal, dato disponible inferido naturalmente, valores declarativos que no
                // deben disparar otras intenciones y diálogo cuando faltan ruta/contenido.
                summary.Items.Add(RunC2D2FileWriteLiteralCase(catalog));
                summary.Items.Add(RunC2D2FileWriteAvailableDataCase(catalog));
                summary.Items.Add(RunC2D2FileWriteDeclarativeContentCase(catalog));
                summary.Items.Add(RunC2D2FileWriteMissingDataCase(catalog));

                // FIX84C2D3: file.read entra al camino común. Protegemos lectura de texto,
                // salida/JSON explícitos, path como dato declarativo y diálogo cuando falta ruta.
                summary.Items.Add(RunC2D3FileReadTextCase(catalog));
                summary.Items.Add(RunC2D3FileReadJsonOutputCase(catalog));
                summary.Items.Add(RunC2D3FileReadDeclarativePathCase(catalog));
                summary.Items.Add(RunC2D3FileReadMissingPathCase(catalog));

                // FIX84C2D4: state.vars entra al camino común. Protegemos set/remove,
                // valores declarativos y diálogo cuando falta el cambio real.
                summary.Items.Add(RunC2D4StateSetCase(catalog));
                summary.Items.Add(RunC2D4StateRemoveCase(catalog));
                summary.Items.Add(RunC2D4StateDeclarativeValueCase(catalog));
                summary.Items.Add(RunC2D4StateMissingChangeCase(catalog));
            }
            catch (Exception ex)
            {
                summary.Items.Add(new ConstructionEquivalenceItem
                {
                    Id = "C2B_GENERAL",
                    NodeType = "FIX84C2B",
                    Phrase = "",
                    Ok = false,
                    Message = "Excepción ejecutando equivalencia: " + ex.Message
                });
            }
            return summary;
        }

        private static ConstructionEquivalenceItem RunEquivalenceCase(
            WfAiCatalog catalog,
            string id,
            string nodeType,
            string phrase,
            string[] expectedPhraseNodeTypes,
            JObject stepAction,
            bool requirePhraseUiReady = false)
        {
            var item = new ConstructionEquivalenceItem
            {
                Id = id,
                NodeType = nodeType,
                Phrase = phrase
            };

            var model = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            if (model == null || !model.Ok || model.Plan == null)
            {
                item.Message = "La frase no produjo un plan válido.";
                return item;
            }

            var builder = new WfAiResolvedNodeBuilder(catalog);
            WfAiResolvedPlanResult phraseResult = builder.ResolvePlan(model.Plan, phrase, "phrase");
            JObject stepPlan = new JObject
            {
                ["intent"] = "build_workflow",
                ["actions"] = new JArray((JObject)stepAction.DeepClone()),
                ["missingData"] = new JArray(),
                ["proposedConnections"] = new JArray()
            };
            WfAiResolvedPlanResult stepResult = builder.ResolvePlan(stepPlan, "", "step_by_step");

            if (phraseResult.Errors.Count > 0 || stepResult.Errors.Count > 0)
            {
                item.Message = "Error de resolución común. Frase: " + JoinList(phraseResult.Errors) + " Paso a paso: " + JoinList(stepResult.Errors);
                return item;
            }

            string[] actualPhraseNodeTypes = AddNodeTypes(phraseResult.Plan);
            string[] expectedNodeTypes = expectedPhraseNodeTypes ?? new string[0];
            if (!actualPhraseNodeTypes.SequenceEqual(expectedNodeTypes, StringComparer.OrdinalIgnoreCase))
            {
                item.Message = "La frase generó una forma de plan inesperada. Esperado="
                    + string.Join(" → ", expectedNodeTypes)
                    + " / Real=" + string.Join(" → ", actualPhraseNodeTypes);
                return item;
            }

            JObject phraseAction = FindActionByType(phraseResult.Plan, nodeType);
            JObject normalizedStepAction = FindActionByType(stepResult.Plan, nodeType);
            if (phraseAction == null || normalizedStepAction == null)
            {
                item.Message = "No se encontró el nodo esperado en ambos caminos.";
                return item;
            }

            JObject phraseCanonical = CanonicalResolvedAction(phraseAction, nodeType);
            JObject stepCanonical = CanonicalResolvedAction(normalizedStepAction, nodeType);
            item.PhraseJson = phraseCanonical.ToString(Formatting.None);
            item.StepJson = stepCanonical.ToString(Formatting.None);
            item.Ok = JToken.DeepEquals(phraseCanonical, stepCanonical);
            item.Message = item.Ok
                ? "Frase y Paso a paso convergen en el mismo nodo resuelto, parámetros normalizados y forma de plan esperada."
                : "Diferencia semántica. Frase=" + item.PhraseJson + " / Paso a paso=" + item.StepJson;

            // FIX84C2Bc/C2Bd: en human.task el título resuelto debe ser también el label visible y mantener referencias coherentes.
            // Esto protege el recorrido real del canvas: no alcanza con que params.titulo sea correcto.
            if (item.Ok && string.Equals(nodeType, "human.task", StringComparison.OrdinalIgnoreCase))
            {
                string expectedTaskLabel = Convert.ToString((phraseAction["params"] as JObject)?["titulo"] ?? "").Trim();
                string phraseLabel = Convert.ToString(phraseAction["label"] ?? "").Trim();
                string stepLabel = Convert.ToString(normalizedStepAction["label"] ?? "").Trim();
                if (expectedTaskLabel.Length == 0
                    || !phraseLabel.Equals(expectedTaskLabel, StringComparison.OrdinalIgnoreCase)
                    || !stepLabel.Equals(expectedTaskLabel, StringComparison.OrdinalIgnoreCase))
                {
                    item.Ok = false;
                    item.Message = "El nodo converge en parámetros, pero el label visual no usa el título resuelto. "
                        + "Título=" + expectedTaskLabel + " / Frase=" + phraseLabel + " / Paso a paso=" + stepLabel;
                }
                else
                {
                    item.Message += " El label visible también usa el título resuelto.";
                }
            }

            // FIX84C2Bb: para human.task no alcanza con comparar el nodo aislado.
            // Recorremos el mismo tramo semántico que usa Dibujar propuesta:
            // plan candidato -> nodo común -> borrador contractual -> plan efectivo -> borrador final.
            // Una asignación explícita válida no puede dejar una aclaración bloqueante legacy.
            if (item.Ok && requirePhraseUiReady)
            {
                string uiError;
                if (!PhraseUiReadyWithoutAnswers(catalog, phrase, phraseResult.Plan, out uiError))
                {
                    item.Ok = false;
                    item.Message = "El nodo converge, pero Dibujar propuesta todavía queda bloqueado: " + uiError;
                }
                else
                {
                    item.Message += " Dibujar propuesta queda listo sin aclaraciones innecesarias.";
                }
            }

            return item;
        }

        private static bool PhraseUiReadyWithoutAnswers(
            WfAiCatalog catalog,
            string phrase,
            JObject commonPlan,
            out string error)
        {
            error = string.Empty;
            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft baseDraft = draftBuilder.Build(phrase, commonPlan, catalog);

            if (baseDraft == null)
            {
                error = "no se pudo construir el borrador contractual.";
                return false;
            }

            if (baseDraft.BlockingClarificationCount > 0)
            {
                error = "el borrador inicial conserva " + baseDraft.BlockingClarificationCount.ToString(CultureInfo.InvariantCulture)
                    + " aclaración(es) bloqueante(s): " + BlockingClarificationSummary(baseDraft);
                return false;
            }

            var commonBuilder = new WfAiResolvedNodeBuilder(catalog);
            WfAiResolvedPlanResult effectiveCommon = commonBuilder.ResolvePlan(commonPlan, phrase, "phrase_resolved");
            if (effectiveCommon == null || effectiveCommon.Errors.Count > 0)
            {
                error = "la resolución efectiva devolvió errores: "
                    + (effectiveCommon == null ? "resultado nulo" : JoinList(effectiveCommon.Errors));
                return false;
            }

            WfAiInterpretationDraft finalDraft = draftBuilder.Build(
                phrase,
                effectiveCommon.Plan,
                catalog,
                null,
                baseDraft.Fingerprint);

            if (finalDraft == null)
            {
                error = "no se pudo construir el borrador final.";
                return false;
            }

            if (finalDraft.BlockingClarificationCount > 0)
            {
                error = "el borrador final conserva " + finalDraft.BlockingClarificationCount.ToString(CultureInfo.InvariantCulture)
                    + " aclaración(es) bloqueante(s): " + BlockingClarificationSummary(finalDraft);
                return false;
            }

            // FIX84C2Bd: replicar también la validación estructural que usa el endpoint real.
            // C2Bc cambió el label visible de human.task, pero proposedConnections todavía podía
            // apuntar al label anterior; el borrador contractual quedaba sin dudas y aun así la UI
            // entraba al fallback GUIADO por errores de conexiones. Esta comprobación evita ese falso OK.
            WfAiValidationResult finalValidation = new WfAiPlanValidator().Validate(effectiveCommon.Plan, catalog);
            if (finalValidation == null || !finalValidation.Ok || finalValidation.Errors.Count > 0)
            {
                error = "la validación final del plan conserva errores: "
                    + (finalValidation == null ? "resultado nulo" : JoinList(finalValidation.Errors));
                return false;
            }

            return true;
        }

        private static string BlockingClarificationSummary(WfAiInterpretationDraft draft)
        {
            if (draft == null || draft.Clarifications == null) return string.Empty;
            return string.Join(" | ", draft.Clarifications
                .Where(c => c != null && c.Blocking)
                .Select(c => (c.Source ?? "") + ":" + (c.Parameter ?? "") + ":" + (c.Question ?? ""))
                .ToArray());
        }

        private static ConstructionEquivalenceItem RunHumanTaskAmbiguityCase(WfAiCatalog catalog)
        {
            const string phrase = "Crear una tarea. Rol = COMPRAS; Usuario = USUARIO1; Título = Revisar factura.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2B_HUMAN_TASK_DESTINATION_AMBIGUOUS",
                NodeType = "human.task",
                Phrase = phrase
            };

            var model = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            if (model == null || !model.Ok || model.Plan == null)
            {
                item.Message = "La frase no produjo un plan válido para comprobar la ambigüedad.";
                return item;
            }

            var common = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase");
            JObject task = FindActionByType(common.Plan, "human.task");
            JObject p = task == null ? null : task["params"] as JObject;
            bool bothPresent = p != null
                && !string.IsNullOrWhiteSpace(Convert.ToString(p["rol"] ?? ""))
                && !string.IsNullOrWhiteSpace(Convert.ToString(p["usuarioAsignado"] ?? ""));

            var draft = new WfAiInterpretationDraftBuilder().Build(phrase, common.Plan, catalog);
            bool asksDestination = draft != null && draft.Clarifications != null && draft.Clarifications.Any(c =>
                c != null
                && string.Equals(c.NodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Parameter, "taskDestination", StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Status, WfAiInterpretationStatus.Ambiguous, StringComparison.OrdinalIgnoreCase)
                && c.Blocking);

            bool commonBlocks = common.Errors != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("único destino", StringComparison.OrdinalIgnoreCase) >= 0
                || (e ?? string.Empty).IndexOf("rol y usuario", StringComparison.OrdinalIgnoreCase) >= 0);

            WfAiClarification destinationQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c =>
                    c != null
                    && string.Equals(c.NodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "taskDestination", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

            bool dialogueResolves = false;
            if (destinationQuestion != null)
            {
                var answers = new JObject
                {
                    [destinationQuestion.Id] = new JObject
                    {
                        ["kind"] = "role",
                        ["value"] = "COMPRAS"
                    }
                };

                WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(
                    common.Plan, draft, answers, catalog);
                WfAiResolvedPlanResult resolvedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                    resolution.Plan, phrase, "phrase_resolved");
                JObject resolvedTask = FindActionByType(resolvedCommon.Plan, "human.task");
                JObject resolvedParams = resolvedTask == null ? null : resolvedTask["params"] as JObject;
                var resolvedDraft = new WfAiInterpretationDraftBuilder().Build(phrase, resolvedCommon.Plan, catalog);

                bool onlyRole = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["rol"] ?? ""), "COMPRAS", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(Convert.ToString(resolvedParams["usuarioAsignado"] ?? ""));
                bool noBlockingDestination = resolvedDraft == null || resolvedDraft.Clarifications == null || !resolvedDraft.Clarifications.Any(c =>
                    c != null
                    && string.Equals(c.NodeType, "human.task", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "taskDestination", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

                dialogueResolves = resolution.Errors.Count == 0
                    && resolvedCommon.Errors.Count == 0
                    && onlyRole
                    && noBlockingDestination;
            }

            item.Ok = bothPresent && asksDestination && commonBlocks && dialogueResolves;
            item.Message = item.Ok
                ? "Rol + Usuario no se resuelve en silencio: bloquea, pregunta y una respuesta guiada deja un único destino válido."
                : "La ambigüedad Rol + Usuario o su resolución guiada no quedó protegida como se esperaba.";
            return item;
        }

        private static ConstructionEquivalenceItem RunC2Bg2SingleNegativeBranchCase(WfAiCatalog catalog, string id, string phrase)
        {
            var item = new ConstructionEquivalenceItem
            {
                Id = id,
                NodeType = "human.task",
                Phrase = phrase
            };

            var model = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            if (model == null || !model.Ok || model.Plan == null)
            {
                item.Message = "La frase no produjo un plan válido.";
                return item;
            }

            WfAiResolvedPlanResult common = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase");
            if (common == null || common.Plan == null || common.Errors.Count > 0)
            {
                item.Message = "La resolución común devolvió errores: " + (common == null ? "resultado nulo" : JoinList(common.Errors));
                return item;
            }

            JObject plan = common.Plan;
            JObject task = FindActionByType(plan, "human.task");
            JObject resultIf = FindHumanResultIfAction(plan, "COMPRAS");
            JObject negativeLogger = FindOutcomeLoggerAction(plan, "COMPRAS", false);
            JObject end = FindActionByType(plan, "util.end");
            WfAiValidationResult validation = new WfAiPlanValidator().Validate(plan, catalog);
            WfAiInterpretationDraft draft = new WfAiInterpretationDraftBuilder().Build(phrase, plan, catalog);

            string taskLabel = ActionLabelForRegression(task);
            string resultLabel = ActionLabelForRegression(resultIf);
            string loggerLabel = ActionLabelForRegression(negativeLogger);
            string endLabel = ActionLabelForRegression(end);
            JObject taskParams = task == null ? null : task["params"] as JObject;
            JObject loggerParams = negativeLogger == null ? null : negativeLogger["params"] as JObject;
            string taskTitle = Convert.ToString(taskParams == null ? "" : taskParams["titulo"] ?? "");
            string loggerMessage = Convert.ToString(loggerParams == null ? "" : loggerParams["message"] ?? "");

            bool shapeOk = task != null && resultIf != null && negativeLogger != null && end != null
                && HasPlanConnection(plan, taskLabel, resultLabel, "")
                && HasPlanConnection(plan, resultLabel, endLabel, "SI")
                && HasPlanConnection(plan, resultLabel, loggerLabel, "NO")
                && HasPlanConnection(plan, loggerLabel, endLabel, "")
                && !HasPlanConnection(plan, resultLabel, loggerLabel, "SI")
                && !HasPlanConnection(plan, resultLabel, endLabel, "NO");

            bool contentOk = loggerMessage.IndexOf("debe corregirse", StringComparison.OrdinalIgnoreCase) >= 0;
            bool titleOk = string.Equals(taskTitle.Trim(), "Revisar factura", StringComparison.OrdinalIgnoreCase)
                && string.Equals(taskLabel, "Revisar factura", StringComparison.OrdinalIgnoreCase);
            bool noArtificialQuestion = draft != null && draft.BlockingClarificationCount == 0;
            bool validationOk = validation != null && validation.Ok && validation.Errors.Count == 0;

            item.Ok = shapeOk && contentOk && titleOk && noArtificialQuestion && validationOk;
            item.Message = item.Ok
                ? "APTO continúa por la salida normal y NO APTO ejecuta la advertencia como rama hermana; no aparece una segunda pregunta SI/NO."
                : "La tarea simple no quedó bifurcada correctamente. Forma=" + shapeOk
                    + ", mensaje=" + contentOk
                    + ", título=" + titleOk
                    + ", aclaraciones=" + (draft == null ? -1 : draft.BlockingClarificationCount)
                    + ", validación=" + validationOk;
            return item;
        }

        private static ConstructionEquivalenceItem RunC2Bg2SiblingBranchesCase(WfAiCatalog catalog)
        {
            const string phrase = "Crear una tarea. Rol = COMPRAS; Título = Revisar factura. Si Compras la aprueba, registrar un evento informativo indicando que fue aprobada. Si Compras la rechaza, registrar una advertencia indicando que debe corregirse.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2BG2_APTO_NO_APTO_HERMANAS",
                NodeType = "human.task",
                Phrase = phrase
            };

            var model = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            WfAiResolvedPlanResult common = model == null || !model.Ok || model.Plan == null
                ? null
                : new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase");
            JObject plan = common == null ? null : common.Plan;

            JObject task = FindActionByType(plan, "human.task");
            JObject resultIf = FindHumanResultIfAction(plan, "COMPRAS");
            JObject approvedLogger = FindOutcomeLoggerAction(plan, "COMPRAS", true);
            JObject negativeLogger = FindOutcomeLoggerAction(plan, "COMPRAS", false);
            JObject end = FindActionByType(plan, "util.end");
            WfAiValidationResult validation = plan == null ? null : new WfAiPlanValidator().Validate(plan, catalog);
            WfAiInterpretationDraft draft = plan == null ? null : new WfAiInterpretationDraftBuilder().Build(phrase, plan, catalog);

            string taskLabel = ActionLabelForRegression(task);
            string resultLabel = ActionLabelForRegression(resultIf);
            string approvedLabel = ActionLabelForRegression(approvedLogger);
            string negativeLabel = ActionLabelForRegression(negativeLogger);
            string endLabel = ActionLabelForRegression(end);
            JObject taskParams = task == null ? null : task["params"] as JObject;
            string taskTitle = Convert.ToString(taskParams == null ? "" : taskParams["titulo"] ?? "");

            bool siblings = task != null && resultIf != null && approvedLogger != null && negativeLogger != null && end != null
                && HasPlanConnection(plan, taskLabel, resultLabel, "")
                && HasPlanConnection(plan, resultLabel, approvedLabel, "SI")
                && HasPlanConnection(plan, resultLabel, negativeLabel, "NO")
                && HasPlanConnection(plan, approvedLabel, endLabel, "")
                && HasPlanConnection(plan, negativeLabel, endLabel, "")
                && !HasPlanConnection(plan, approvedLabel, negativeLabel, "")
                && !HasPlanConnection(plan, negativeLabel, approvedLabel, "");

            bool titleOk = string.Equals(taskTitle.Trim(), "Revisar factura", StringComparison.OrdinalIgnoreCase)
                && string.Equals(taskLabel, "Revisar factura", StringComparison.OrdinalIgnoreCase);
            bool noArtificialQuestion = draft != null && draft.BlockingClarificationCount == 0;
            bool validationOk = common != null && common.Errors.Count == 0
                && validation != null && validation.Ok && validation.Errors.Count == 0;

            item.Ok = siblings && titleOk && noArtificialQuestion && validationOk;
            item.Message = item.Ok
                ? "APTO y NO APTO son salidas hermanas de la misma tarea: ninguna acción de una rama queda en serie con la otra."
                : "APTO/NO APTO no quedaron como ramas hermanas. Ramas=" + siblings
                    + ", título=" + titleOk
                    + ", aclaraciones=" + (draft == null ? -1 : draft.BlockingClarificationCount)
                    + ", validación=" + validationOk;
            return item;
        }

        private static ConstructionEquivalenceItem RunC2Bg2bExplicitWarningLoggerCase(WfAiCatalog catalog)
        {
            const string phrase = "Crear una tarea. Rol = COMPRAS; Título = Revisar factura. Si Compras la rechaza, registrar una advertencia indicando que debe corregirse.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2BG2B_REGISTRAR_ADVERTENCIA_WARN",
                NodeType = "human.task",
                Phrase = phrase
            };

            var model = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            WfAiResolvedPlanResult common = model == null || !model.Ok || model.Plan == null
                ? null
                : new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase");
            JObject plan = common == null ? null : common.Plan;

            JObject task = FindActionByType(plan, "human.task");
            JObject resultIf = FindHumanResultIfAction(plan, "COMPRAS");
            JObject negativeLogger = FindOutcomeLoggerAction(plan, "COMPRAS", false);
            JObject notify = FindActionByType(plan, "util.notify");
            JObject end = FindActionByType(plan, "util.end");
            WfAiValidationResult validation = plan == null ? null : new WfAiPlanValidator().Validate(plan, catalog);
            WfAiInterpretationDraft draft = plan == null ? null : new WfAiInterpretationDraftBuilder().Build(phrase, plan, catalog);

            JObject loggerParams = negativeLogger == null ? null : negativeLogger["params"] as JObject;
            string level = Convert.ToString(loggerParams == null ? "" : loggerParams["level"] ?? "");
            string message = Convert.ToString(loggerParams == null ? "" : loggerParams["message"] ?? "");
            string taskLabel = ActionLabelForRegression(task);
            string resultLabel = ActionLabelForRegression(resultIf);
            string loggerLabel = ActionLabelForRegression(negativeLogger);
            string endLabel = ActionLabelForRegression(end);

            bool loggerOk = negativeLogger != null
                && string.Equals(level.Trim(), "Warn", StringComparison.OrdinalIgnoreCase)
                && message.IndexOf("debe corregirse", StringComparison.OrdinalIgnoreCase) >= 0;
            bool noNotify = notify == null;
            bool branchOk = task != null && resultIf != null && negativeLogger != null && end != null
                && HasPlanConnection(plan, taskLabel, resultLabel, "")
                && HasPlanConnection(plan, resultLabel, loggerLabel, "NO")
                && HasPlanConnection(plan, resultLabel, endLabel, "SI")
                && HasPlanConnection(plan, loggerLabel, endLabel, "");
            bool noContractualQuestion = draft != null && draft.BlockingClarificationCount == 0;
            bool validationOk = common != null && common.Errors.Count == 0
                && validation != null && validation.Ok && validation.Errors.Count == 0;

            item.Ok = loggerOk && noNotify && branchOk && noContractualQuestion && validationOk;
            item.Message = item.Ok
                ? "'Registrar una advertencia' queda como util.logger Warn dentro de NO APTO, sin util.notify ni aclaración contractual adicional."
                : "La advertencia explícita no quedó cerrada como Logger/Warn. Logger=" + loggerOk
                    + ", notifyExtra=" + (!noNotify)
                    + ", rama=" + branchOk
                    + ", aclaraciones=" + (draft == null ? -1 : draft.BlockingClarificationCount)
                    + ", validación=" + validationOk;
            return item;
        }

        private static ConstructionEquivalenceItem RunC2Bg2cStepByStepHumanBranchesCase(WfAiCatalog catalog)
        {
            const string phrase = "Crear una tarea. Rol = COMPRAS; Título = Revisar factura. Si Compras la rechaza, registrar una advertencia indicando que debe corregirse.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2BG2C_PASO_A_PASO_RAMAS_HUMANAS",
                NodeType = "human.task",
                Phrase = phrase
            };

            var phraseModel = new WfAiMlnetProvider().Interpret(phrase, catalog, "");
            WfAiResolvedPlanResult phraseCommon = phraseModel == null || !phraseModel.Ok || phraseModel.Plan == null
                ? null
                : new WfAiResolvedNodeBuilder(catalog).ResolvePlan(phraseModel.Plan, phrase, "phrase");

            var stepPlan = new JObject
            {
                ["intent"] = "build_workflow",
                ["actions"] = new JArray
                {
                    new JObject { ["action"] = "ADD_NODE", ["nodeType"] = "util.start", ["label"] = "Inicio", ["params"] = new JObject() },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "human.task", ["label"] = "Revisar factura",
                        ["params"] = new JObject { ["rol"] = "COMPRAS", ["titulo"] = "Revisar factura" }
                    },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "control.if", ["label"] = "Resultado de COMPRAS aprobado",
                        ["params"] = new JObject { ["field"] = "wf.tarea.resultado", ["op"] = "==", ["value"] = "apto" }
                    },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "util.logger", ["label"] = "Registrar advertencia",
                        ["params"] = new JObject { ["level"] = "Warn", ["message"] = "debe corregirse" }
                    },
                    new JObject { ["action"] = "ADD_NODE", ["nodeType"] = "util.end", ["label"] = "Fin", ["params"] = new JObject() }
                },
                ["missingData"] = new JArray(),
                ["proposedConnections"] = new JArray
                {
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Inicio", ["to"] = "Revisar factura", ["fromNodeType"] = "util.start", ["toNodeType"] = "human.task" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Revisar factura", ["to"] = "Resultado de COMPRAS aprobado", ["fromNodeType"] = "human.task", ["toNodeType"] = "control.if" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Resultado de COMPRAS aprobado", ["to"] = "Fin", ["fromNodeType"] = "control.if", ["toNodeType"] = "util.end", ["condition"] = "SI" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Resultado de COMPRAS aprobado", ["to"] = "Registrar advertencia", ["fromNodeType"] = "control.if", ["toNodeType"] = "util.logger", ["condition"] = "NO" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Registrar advertencia", ["to"] = "Fin", ["fromNodeType"] = "util.logger", ["toNodeType"] = "util.end" }
                }
            };

            WfAiResolvedPlanResult stepCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(stepPlan, "", "step_by_step");
            JObject phrasePlan = phraseCommon == null ? null : phraseCommon.Plan;
            JObject normalizedStepPlan = stepCommon == null ? null : stepCommon.Plan;

            JObject phraseTask = FindActionByType(phrasePlan, "human.task");
            JObject stepTask = FindActionByType(normalizedStepPlan, "human.task");
            JObject phraseLogger = FindOutcomeLoggerAction(phrasePlan, "COMPRAS", false);
            JObject stepLogger = FindActionByType(normalizedStepPlan, "util.logger");
            JObject stepIf = FindHumanResultIfAction(normalizedStepPlan, "COMPRAS");
            JObject stepEnd = FindActionByType(normalizedStepPlan, "util.end");

            string[] stepTypes = AddNodeTypes(normalizedStepPlan);
            bool oneEnd = stepTypes.Count(t => string.Equals(t, "util.end", StringComparison.OrdinalIgnoreCase)) == 1;
            bool expectedTypes = stepTypes.SequenceEqual(new[] { "util.start", "human.task", "control.if", "util.logger", "util.end" }, StringComparer.OrdinalIgnoreCase);

            string taskLabel = ActionLabelForRegression(stepTask);
            string ifLabel = ActionLabelForRegression(stepIf);
            string loggerLabel = ActionLabelForRegression(stepLogger);
            string endLabel = ActionLabelForRegression(stepEnd);
            bool shapeOk = normalizedStepPlan != null && stepTask != null && stepIf != null && stepLogger != null && stepEnd != null
                && HasPlanConnection(normalizedStepPlan, "Inicio", taskLabel, "")
                && HasPlanConnection(normalizedStepPlan, taskLabel, ifLabel, "")
                && HasPlanConnection(normalizedStepPlan, ifLabel, endLabel, "SI")
                && HasPlanConnection(normalizedStepPlan, ifLabel, loggerLabel, "NO")
                && HasPlanConnection(normalizedStepPlan, loggerLabel, endLabel, "");

            bool taskEquivalent = phraseTask != null && stepTask != null
                && JToken.DeepEquals(CanonicalResolvedAction(phraseTask, "human.task"), CanonicalResolvedAction(stepTask, "human.task"));
            bool loggerEquivalent = phraseLogger != null && stepLogger != null
                && JToken.DeepEquals(CanonicalResolvedAction(phraseLogger, "util.logger"), CanonicalResolvedAction(stepLogger, "util.logger"));

            WfAiValidationResult stepValidation = normalizedStepPlan == null ? null : new WfAiPlanValidator().Validate(normalizedStepPlan, catalog);
            bool validationOk = stepCommon != null && stepCommon.Errors.Count == 0
                && stepValidation != null && stepValidation.Ok && stepValidation.Errors.Count == 0;

            item.Ok = expectedTypes && oneEnd && shapeOk && taskEquivalent && loggerEquivalent && validationOk;
            item.Message = item.Ok
                ? "Paso a paso normaliza al mismo tramo semántico de Frase: Human Task → resultado APTO/NO APTO, Logger Warn en NO APTO y un único Fin común."
                : "Paso a paso no converge al contrato objetivo. tipos=" + expectedTypes
                    + ", unFin=" + oneEnd
                    + ", forma=" + shapeOk
                    + ", tarea=" + taskEquivalent
                    + ", logger=" + loggerEquivalent
                    + ", validación=" + validationOk;
            return item;
        }

        private static ConstructionEquivalenceItem RunC2Bg2dDeclarativeMessageCase(WfAiCatalog catalog)
        {
            const string phrase = "crear una tarea con Rol = COMPRAS; Título = Revisar la factura, si la tarea de COMPRAS queda no apta registrar un log Warn con mensaje Informar, si la tarea de COMPRAS queda apta finalizar.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2BG2D_PASO_A_PASO_VALORES_DECLARATIVOS",
                NodeType = "human.task",
                Phrase = phrase
            };

            var stepPlan = new JObject
            {
                ["intent"] = "build_workflow",
                ["actions"] = new JArray
                {
                    new JObject { ["action"] = "ADD_NODE", ["nodeType"] = "util.start", ["label"] = "Inicio", ["params"] = new JObject() },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "human.task", ["label"] = "Revisar la factura",
                        ["params"] = new JObject { ["rol"] = "COMPRAS", ["titulo"] = "Revisar la factura" }
                    },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "control.if", ["label"] = "Resultado de COMPRAS aprobado",
                        ["params"] = new JObject { ["field"] = "wf.tarea.resultado", ["op"] = "==", ["value"] = "apto" }
                    },
                    new JObject
                    {
                        ["action"] = "ADD_NODE", ["nodeType"] = "util.logger", ["label"] = "Registrar log Warn",
                        ["params"] = new JObject { ["level"] = "Warn", ["message"] = "Informar" }
                    },
                    new JObject { ["action"] = "ADD_NODE", ["nodeType"] = "util.end", ["label"] = "Fin", ["params"] = new JObject() }
                },
                ["missingData"] = new JArray(),
                ["proposedConnections"] = new JArray
                {
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Inicio", ["to"] = "Revisar la factura", ["fromNodeType"] = "util.start", ["toNodeType"] = "human.task" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Revisar la factura", ["to"] = "Resultado de COMPRAS aprobado", ["fromNodeType"] = "human.task", ["toNodeType"] = "control.if" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Resultado de COMPRAS aprobado", ["to"] = "Fin", ["fromNodeType"] = "control.if", ["toNodeType"] = "util.end", ["condition"] = "SI" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Resultado de COMPRAS aprobado", ["to"] = "Registrar log Warn", ["fromNodeType"] = "control.if", ["toNodeType"] = "util.logger", ["condition"] = "NO" },
                    new JObject { ["action"] = "CONNECT_NODES", ["from"] = "Registrar log Warn", ["to"] = "Fin", ["fromNodeType"] = "util.logger", ["toNodeType"] = "util.end" }
                }
            };

            WfAiResolvedPlanResult common = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(stepPlan, phrase, "step_by_step");
            JObject plan = common == null ? null : common.Plan;
            JObject logger = FindActionByType(plan, "util.logger");
            JObject notify = FindActionByType(plan, "util.notify");
            JObject loggerParams = logger == null ? null : logger["params"] as JObject;
            string level = Convert.ToString(loggerParams == null ? "" : loggerParams["level"] ?? "");
            string message = Convert.ToString(loggerParams == null ? "" : loggerParams["message"] ?? "");
            WfAiValidationResult validation = plan == null ? null : new WfAiPlanValidator().Validate(plan, catalog);

            bool sourceOk = common != null && string.Equals(common.SourceKind, "step_by_step", StringComparison.OrdinalIgnoreCase);
            bool loggerOk = logger != null
                && string.Equals(level.Trim(), "Warn", StringComparison.OrdinalIgnoreCase)
                && string.Equals(message.Trim(), "Informar", StringComparison.Ordinal);
            bool noNotify = notify == null;
            bool validationOk = common != null && common.Errors.Count == 0
                && validation != null && validation.Ok && validation.Errors.Count == 0;

            item.Ok = sourceOk && loggerOk && noNotify && validationOk;
            item.Message = item.Ok
                ? "Paso a paso conserva Message = Informar como dato declarativo de util.logger Warn; no aparece util.notify ni se reinterpretan propiedades como nuevas intenciones."
                : "El valor declarativo de Paso a paso fue alterado o reinterpretado. source=" + sourceOk
                    + ", logger=" + loggerOk
                    + ", notifyExtra=" + (!noNotify)
                    + ", validación=" + validationOk;
            return item;
        }

        private static ConstructionEquivalenceItem RunC2C1ControlIfSimpleCase(WfAiCatalog catalog)
        {
            return RunC2C1ControlIfContractCase(
                catalog,
                "C2C1_IF_SIMPLE",
                "Condición simple: Total = 200000.",
                new JObject
                {
                    ["field"] = "${biz.notaCredito.total}",
                    ["op"] = "=",
                    ["value"] = "200000"
                },
                new JObject
                {
                    ["field"] = "biz.notaCredito.total",
                    ["op"] = "==",
                    ["value"] = "200000"
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C1ControlIfNoValueCase(WfAiCatalog catalog)
        {
            return RunC2C1ControlIfContractCase(
                catalog,
                "C2C1_IF_SIN_VALOR",
                "Condición simple sin valor: CAE no está vacío.",
                new JObject
                {
                    ["field"] = "${biz.notaCredito.cae}",
                    ["op"] = "not_empty",
                    ["value"] = "este valor debe desaparecer"
                },
                new JObject
                {
                    ["field"] = "biz.notaCredito.cae",
                    ["op"] = "not_empty"
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C1ControlIfAllCase(WfAiCatalog catalog)
        {
            return RunC2C1ControlIfContractCase(
                catalog,
                "C2C1_IF_ALL",
                "Condición compuesta ALL: CAE informado Y total > 200000.",
                new JObject
                {
                    ["rulesMode"] = "Y",
                    ["rules"] = new JArray
                    {
                        new JObject { ["fieldPath"] = "${biz.notaCredito.cae}", ["operator"] = "not_empty", ["value"] = "ignorar" },
                        new JObject { ["field"] = "biz.notaCredito.total", ["op"] = ">", ["value"] = "200000" }
                    }
                },
                new JObject
                {
                    ["rulesMode"] = "all",
                    ["rules"] = new JArray
                    {
                        new JObject { ["field"] = "biz.notaCredito.cae", ["op"] = "not_empty" },
                        new JObject { ["field"] = "biz.notaCredito.total", ["op"] = ">", ["value"] = "200000" }
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2C1ControlIfAnyCase(WfAiCatalog catalog)
        {
            return RunC2C1ControlIfContractCase(
                catalog,
                "C2C1_IF_ANY",
                "Condición compuesta ANY: CAE vacío O comprobante asociado informado.",
                new JObject
                {
                    ["rulesMode"] = "OR",
                    ["rules"] = new JArray
                    {
                        new JObject { ["fieldPath"] = "${biz.notaCredito.cae}", ["operator"] = "empty" },
                        new JObject { ["field"] = "biz.notaCredito.comprobanteAsociado.numero", ["op"] = "not_empty" }
                    }
                },
                new JObject
                {
                    ["rulesMode"] = "any",
                    ["rules"] = new JArray
                    {
                        new JObject { ["field"] = "biz.notaCredito.cae", ["op"] = "empty" },
                        new JObject { ["field"] = "biz.notaCredito.comprobanteAsociado.numero", ["op"] = "not_empty" }
                    }
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C1ControlIfContractCase(
            WfAiCatalog catalog,
            string id,
            string description,
            JObject phraseParams,
            JObject stepParams,
            bool verifyCompoundClarification)
        {
            var item = new ConstructionEquivalenceItem
            {
                Id = id,
                NodeType = "control.if",
                Phrase = description
            };

            var phrasePlan = C2C1IfPlan(phraseParams);
            var stepPlan = C2C1IfPlan(stepParams);
            var builder = new WfAiResolvedNodeBuilder(catalog);
            WfAiResolvedPlanResult phraseCommon = builder.ResolvePlan(phrasePlan, description, "phrase");
            WfAiResolvedPlanResult stepCommon = builder.ResolvePlan(stepPlan, string.Empty, "step_by_step");

            JObject phraseAction = FindActionByType(phraseCommon == null ? null : phraseCommon.Plan, "control.if");
            JObject stepAction = FindActionByType(stepCommon == null ? null : stepCommon.Plan, "control.if");
            JObject phraseCanonical = CanonicalResolvedAction(phraseAction, "control.if");
            JObject stepCanonical = CanonicalResolvedAction(stepAction, "control.if");

            bool covered = WfAiResolvedNodeBuilder.IsCovered("control.if");
            bool commonOk = phraseCommon != null && stepCommon != null
                && phraseCommon.Errors.Count == 0 && stepCommon.Errors.Count == 0
                && phraseAction != null && stepAction != null
                && JToken.DeepEquals(phraseCanonical, stepCanonical);

            bool draftOk = false;
            if (commonOk)
            {
                var phraseDraft = new WfAiInterpretationDraftBuilder().Build(description, phraseCommon.Plan, catalog);
                var stepDraft = new WfAiInterpretationDraftBuilder().Build(string.Empty, stepCommon.Plan, catalog);
                draftOk = phraseDraft != null && stepDraft != null
                    && phraseDraft.BlockingClarificationCount == 0
                    && stepDraft.BlockingClarificationCount == 0;
            }

            bool clarificationOk = true;
            if (verifyCompoundClarification)
                clarificationOk = C2C1CompoundClarificationResolves(catalog, stepParams);

            item.PhraseJson = phraseCanonical.ToString(Formatting.None);
            item.StepJson = stepCanonical.ToString(Formatting.None);
            item.Ok = covered && commonOk && draftOk && clarificationOk;
            item.Message = item.Ok
                ? "Frase y Paso a paso convergen en el mismo control.if contractual; operadores/campos/modo quedan canónicos y la definición completa no pide aclaraciones adicionales."
                    + (verifyCompoundClarification ? " El diálogo ConditionBuilder también resuelve reglas múltiples." : string.Empty)
                : "control.if no convergió correctamente. Cubierto=" + covered
                    + ", común=" + commonOk
                    + ", borrador=" + draftOk
                    + ", aclaraciónCompuesta=" + clarificationOk
                    + ", Frase=" + item.PhraseJson
                    + ", Paso=" + item.StepJson
                    + ", erroresFrase=" + (phraseCommon == null ? "n/a" : JoinList(phraseCommon.Errors))
                    + ", erroresPaso=" + (stepCommon == null ? "n/a" : JoinList(stepCommon.Errors));
            return item;
        }

        private static JObject C2C1IfPlan(JObject parameters)
        {
            return new JObject
            {
                ["intent"] = "build_workflow",
                ["actions"] = new JArray
                {
                    new JObject
                    {
                        ["action"] = "ADD_NODE",
                        ["nodeType"] = "control.if",
                        ["label"] = "Condición C2C1",
                        ["params"] = parameters == null ? new JObject() : parameters.DeepClone()
                    }
                },
                ["missingData"] = new JArray(),
                ["proposedConnections"] = new JArray()
            };
        }

        private static bool C2C1CompoundClarificationResolves(WfAiCatalog catalog, JObject expectedParams)
        {
            JObject basePlan = C2C1IfPlan(new JObject());
            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft draft = draftBuilder.Build(string.Empty, basePlan, catalog);
            if (draft == null || draft.Clarifications == null) return false;

            WfAiClarification clarification = draft.Clarifications.FirstOrDefault(c => c != null
                && string.Equals(c.NodeType, "control.if", StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Parameter, "conditionDefinition", StringComparison.OrdinalIgnoreCase));
            if (clarification == null || string.IsNullOrWhiteSpace(clarification.Id)) return false;

            JObject answer = new JObject
            {
                ["mode"] = "compound",
                ["rulesMode"] = Convert.ToString(expectedParams == null ? null : expectedParams["rulesMode"]),
                ["rules"] = expectedParams == null || expectedParams["rules"] == null
                    ? new JArray()
                    : expectedParams["rules"].DeepClone()
            };
            JObject answers = new JObject { [clarification.Id] = answer };
            WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(basePlan, draft, answers, catalog);
            if (resolution == null || resolution.Errors.Count > 0) return false;

            WfAiResolvedPlanResult common = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(resolution.Plan, string.Empty, "phrase_resolved");
            JObject resolvedAction = FindActionByType(common == null ? null : common.Plan, "control.if");
            JObject expectedAction = FindActionByType(new WfAiResolvedNodeBuilder(catalog).ResolvePlan(C2C1IfPlan(expectedParams), string.Empty, "step_by_step").Plan, "control.if");
            return common != null && common.Errors.Count == 0 && resolvedAction != null && expectedAction != null
                && JToken.DeepEquals(CanonicalResolvedAction(resolvedAction, "control.if"), CanonicalResolvedAction(expectedAction, "control.if"));
        }

        private static ConstructionEquivalenceItem RunC2D1NotifyRoleCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D1_NOTIFY_ROLE",
                "util.notify",
                "Notificar internamente al rol COMPRAS con mensaje factura aprobada.",
                new[] { "util.start", "util.notify", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "util.notify",
                    ["label"] = "Notificar",
                    ["params"] = new JObject
                    {
                        ["rolDestino"] = "COMPRAS",
                        ["mensaje"] = "factura aprobada"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D1NotifyUserCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D1_NOTIFY_USUARIO_CORTO",
                "util.notify",
                "Notificar internamente al usuario USUARIO1 con mensaje revisión disponible.",
                new[] { "util.start", "util.notify", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "util.notify",
                    ["label"] = "Notificar",
                    ["params"] = new JObject
                    {
                        ["usuarioDestino"] = "USUARIO1",
                        ["mensaje"] = "revisión disponible"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D1NotifyMissingMessageCase(WfAiCatalog catalog)
        {
            const string phrase = "Notificar internamente.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2D1_NOTIFY_DATOS_FALTANTES",
                NodeType = "util.notify",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult common = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            JObject notify = FindActionByType(common == null ? null : common.Plan, "util.notify");
            JObject parameters = notify == null ? null : notify["params"] as JObject;

            bool noInventedMessage = parameters != null
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["mensaje"] ?? ""));
            bool noInventedDestination = parameters != null
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["rolDestino"] ?? ""))
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["usuarioDestino"] ?? ""));
            bool commonBlocksMessage = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("mensaje", StringComparison.OrdinalIgnoreCase) >= 0);
            bool commonBlocksDestination = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("rol o un usuario", StringComparison.OrdinalIgnoreCase) >= 0);

            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft draft = common == null ? null : draftBuilder.Build(phrase, common.Plan, catalog);
            WfAiClarification messageQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "util.notify", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "mensaje", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);
            WfAiClarification destinationQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "util.notify", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "notificationDestination", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

            bool dialogueResolves = false;
            if (messageQuestion != null && destinationQuestion != null)
            {
                const string answerText = "La factura está lista para revisar";
                var answers = new JObject
                {
                    [destinationQuestion.Id] = new JObject
                    {
                        ["kind"] = "role",
                        ["value"] = "COMPRAS"
                    },
                    [messageQuestion.Id] = answerText
                };
                WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(
                    common.Plan, draft, answers, catalog);
                WfAiResolvedPlanResult resolvedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                    resolution.Plan, phrase, "phrase_resolved");
                JObject resolvedNotify = FindActionByType(resolvedCommon == null ? null : resolvedCommon.Plan, "util.notify");
                JObject resolvedParams = resolvedNotify == null ? null : resolvedNotify["params"] as JObject;
                WfAiInterpretationDraft resolvedDraft = resolvedCommon == null
                    ? null
                    : draftBuilder.Build(phrase, resolvedCommon.Plan, catalog);

                bool exactMessage = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["mensaje"] ?? ""), answerText, StringComparison.Ordinal);
                bool exactDestination = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["rolDestino"] ?? ""), "COMPRAS", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(Convert.ToString(resolvedParams["usuarioDestino"] ?? ""));
                bool noBlocking = resolvedDraft == null || resolvedDraft.BlockingClarificationCount == 0;

                dialogueResolves = resolution != null
                    && resolution.Errors.Count == 0
                    && resolvedCommon != null
                    && resolvedCommon.Errors.Count == 0
                    && exactMessage
                    && exactDestination
                    && noBlocking;
            }

            item.PhraseJson = common == null || common.Plan == null ? "{}" : common.Plan.ToString(Formatting.None);
            item.StepJson = "rolDestino = COMPRAS; mensaje = respuesta explícita del usuario";
            item.Ok = model != null && model.Ok
                && common != null
                && noInventedMessage
                && noInventedDestination
                && commonBlocksMessage
                && commonBlocksDestination
                && messageQuestion != null
                && destinationQuestion != null
                && dialogueResolves;
            item.Message = item.Ok
                ? "Sin destinatario ni mensaje explícitos, util.notify no inventa decisiones: pregunta ambos datos y la respuesta guiada deja un único destino real y el mensaje exacto."
                : "Los datos faltantes no quedaron protegidos correctamente. mensajeNoInventado=" + noInventedMessage
                    + ", destinoNoInventado=" + noInventedDestination
                    + ", bloqueaMensaje=" + commonBlocksMessage
                    + ", bloqueaDestino=" + commonBlocksDestination
                    + ", preguntaMensaje=" + (messageQuestion != null)
                    + ", preguntaDestino=" + (destinationQuestion != null)
                    + ", resuelve=" + dialogueResolves
                    + ", erroresComún=" + (common == null ? "n/a" : JoinList(common.Errors));
            return item;
        }

        private static ConstructionEquivalenceItem RunC2D2FileWriteLiteralCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D2_FILE_WRITE_TEXTO_LITERAL",
                "file.write",
                @"Escribir archivo en C:\temp\D2_literal.txt con contenido Prueba D2. Después finalizar.",
                new[] { "util.start", "file.write", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.write",
                    ["label"] = "Archivo: Escribir",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\D2_literal.txt",
                        ["content"] = "Prueba D2"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D2FileWriteAvailableDataCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D2_FILE_WRITE_DATO_DISPONIBLE",
                "file.write",
                @"Escribir el ID de instancia en C:\temp\D2_instancia.txt. Después finalizar.",
                new[] { "util.start", "file.write", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.write",
                    ["label"] = "Archivo: Escribir",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\D2_instancia.txt",
                        ["content"] = "${wf.instanceId}"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D2FileWriteDeclarativeContentCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D2_FILE_WRITE_CONTENIDO_DECLARATIVO",
                "file.write",
                @"Escribir archivo en C:\temp\D2_dato.txt con contenido Notificar internamente al rol COMPRAS. Después finalizar.",
                new[] { "util.start", "file.write", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.write",
                    ["label"] = "Archivo: Escribir",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\D2_dato.txt",
                        ["content"] = "Notificar internamente al rol COMPRAS"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D2FileWriteMissingDataCase(WfAiCatalog catalog)
        {
            const string phrase = "Escribir archivo. Después finalizar.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2D2_FILE_WRITE_DATOS_FALTANTES",
                NodeType = "file.write",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult common = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            JObject write = FindActionByType(common == null ? null : common.Plan, "file.write");
            JObject parameters = write == null ? null : write["params"] as JObject;

            bool noInventedPath = parameters != null
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["path"] ?? ""));
            bool noInventedSource = parameters != null
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["content"] ?? ""))
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["origen"] ?? ""));
            bool commonBlocksPath = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("ruta", StringComparison.OrdinalIgnoreCase) >= 0);
            bool commonBlocksSource = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("contenido", StringComparison.OrdinalIgnoreCase) >= 0
                || (e ?? string.Empty).IndexOf("dato", StringComparison.OrdinalIgnoreCase) >= 0);

            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft draft = common == null ? null : draftBuilder.Build(phrase, common.Plan, catalog);
            WfAiClarification pathQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "file.write", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "path", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);
            WfAiClarification sourceQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "file.write", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "fileWriteSource", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

            bool dialogueResolves = false;
            if (pathQuestion != null && sourceQuestion != null)
            {
                const string answerPath = @"C:\temp\D2_aclarado.txt";
                const string answerContent = "Texto decidido por el usuario";
                var answers = new JObject
                {
                    [pathQuestion.Id] = answerPath,
                    [sourceQuestion.Id] = answerContent
                };

                WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(
                    common.Plan, draft, answers, catalog);
                WfAiResolvedPlanResult resolvedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                    resolution.Plan, phrase, "phrase_resolved");
                JObject resolvedWrite = FindActionByType(resolvedCommon == null ? null : resolvedCommon.Plan, "file.write");
                JObject resolvedParams = resolvedWrite == null ? null : resolvedWrite["params"] as JObject;
                WfAiInterpretationDraft resolvedDraft = resolvedCommon == null
                    ? null
                    : draftBuilder.Build(phrase, resolvedCommon.Plan, catalog);

                bool exactValues = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["path"] ?? ""), answerPath, StringComparison.Ordinal)
                    && string.Equals(Convert.ToString(resolvedParams["content"] ?? ""), answerContent, StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(Convert.ToString(resolvedParams["origen"] ?? ""));
                bool safeDefaults = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["encoding"] ?? ""), "utf-8", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["overwrite"] ?? ""), "True", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["zipMode"] ?? ""), "none", StringComparison.OrdinalIgnoreCase);
                bool noBlocking = resolvedDraft == null || resolvedDraft.BlockingClarificationCount == 0;

                dialogueResolves = resolution != null
                    && resolution.Errors.Count == 0
                    && resolvedCommon != null
                    && resolvedCommon.Errors.Count == 0
                    && exactValues
                    && safeDefaults
                    && noBlocking;
            }

            item.PhraseJson = common == null || common.Plan == null ? "{}" : common.Plan.ToString(Formatting.None);
            item.StepJson = @"path = C:\temp\D2_aclarado.txt; content = Texto decidido por el usuario";
            item.Ok = model != null && model.Ok
                && common != null
                && noInventedPath
                && noInventedSource
                && commonBlocksPath
                && commonBlocksSource
                && pathQuestion != null
                && sourceQuestion != null
                && dialogueResolves;
            item.Message = item.Ok
                ? "Sin ruta ni contenido, file.write no inventa decisiones: pregunta ambos datos y el diálogo deja el nodo válido con defaults técnicos seguros."
                : "Los datos faltantes de file.write no quedaron protegidos. rutaNoInventada=" + noInventedPath
                    + ", fuenteNoInventada=" + noInventedSource
                    + ", bloqueaRuta=" + commonBlocksPath
                    + ", bloqueaFuente=" + commonBlocksSource
                    + ", preguntaRuta=" + (pathQuestion != null)
                    + ", preguntaFuente=" + (sourceQuestion != null)
                    + ", resuelve=" + dialogueResolves
                    + ", erroresComún=" + (common == null ? "n/a" : JoinList(common.Errors));
            return item;
        }

        private static ConstructionEquivalenceItem RunC2D3FileReadTextCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D3_FILE_READ_TEXTO",
                "file.read",
                @"Leer archivo C:\temp\D3_texto.txt. Después finalizar.",
                new[] { "util.start", "file.read", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.read",
                    ["label"] = "Archivo: Leer",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\D3_texto.txt"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D3FileReadJsonOutputCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D3_FILE_READ_JSON_SALIDA",
                "file.read",
                @"Leer archivo C:\temp\D3_datos.json como JSON y guardar el contenido en datos.archivo. Después finalizar.",
                new[] { "util.start", "file.read", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.read",
                    ["label"] = "Archivo: Leer",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\D3_datos.json",
                        ["salida"] = "datos.archivo",
                        ["asJson"] = true
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D3FileReadDeclarativePathCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D3_FILE_READ_PATH_DECLARATIVO",
                "file.read",
                @"Leer archivo C:\temp\Notificar_COMPRAS.txt. Después finalizar.",
                new[] { "util.start", "file.read", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "file.read",
                    ["label"] = "Archivo: Leer",
                    ["params"] = new JObject
                    {
                        ["path"] = @"C:\temp\Notificar_COMPRAS.txt"
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D3FileReadMissingPathCase(WfAiCatalog catalog)
        {
            const string phrase = "Leer archivo. Después finalizar.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2D3_FILE_READ_RUTA_FALTANTE",
                NodeType = "file.read",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult common = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            JObject read = FindActionByType(common == null ? null : common.Plan, "file.read");
            JObject parameters = read == null ? null : read["params"] as JObject;

            bool noInventedPath = parameters != null
                && string.IsNullOrWhiteSpace(Convert.ToString(parameters["path"] ?? ""));
            bool safeDefaults = parameters != null
                && string.Equals(Convert.ToString(parameters["salida"] ?? ""), "archivo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(parameters["asJson"] ?? ""), "False", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(parameters["encoding"] ?? ""), "utf-8", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(parameters["zipMode"] ?? ""), "auto", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(parameters["useCache"] ?? ""), "True", StringComparison.OrdinalIgnoreCase);
            bool commonBlocksPath = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("ruta", StringComparison.OrdinalIgnoreCase) >= 0);

            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft draft = common == null ? null : draftBuilder.Build(phrase, common.Plan, catalog);
            WfAiClarification pathQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "file.read", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "path", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

            bool dialogueResolves = false;
            if (pathQuestion != null)
            {
                const string answerPath = @"C:\temp\D3_aclarado.txt";
                var answers = new JObject { [pathQuestion.Id] = answerPath };
                WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(
                    common.Plan, draft, answers, catalog);
                WfAiResolvedPlanResult resolvedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                    resolution.Plan, phrase, "phrase_resolved");
                JObject resolvedRead = FindActionByType(resolvedCommon == null ? null : resolvedCommon.Plan, "file.read");
                JObject resolvedParams = resolvedRead == null ? null : resolvedRead["params"] as JObject;
                WfAiInterpretationDraft resolvedDraft = resolvedCommon == null
                    ? null
                    : draftBuilder.Build(phrase, resolvedCommon.Plan, catalog);

                bool exactPath = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["path"] ?? ""), answerPath, StringComparison.Ordinal);
                bool resolvedDefaults = resolvedParams != null
                    && string.Equals(Convert.ToString(resolvedParams["salida"] ?? ""), "archivo", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["asJson"] ?? ""), "False", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["encoding"] ?? ""), "utf-8", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["zipMode"] ?? ""), "auto", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Convert.ToString(resolvedParams["useCache"] ?? ""), "True", StringComparison.OrdinalIgnoreCase);
                bool noBlocking = resolvedDraft == null || resolvedDraft.BlockingClarificationCount == 0;

                dialogueResolves = resolution != null
                    && resolution.Errors.Count == 0
                    && resolvedCommon != null
                    && resolvedCommon.Errors.Count == 0
                    && exactPath
                    && resolvedDefaults
                    && noBlocking;
            }

            item.PhraseJson = common == null || common.Plan == null ? "{}" : common.Plan.ToString(Formatting.None);
            item.StepJson = @"path = C:\temp\D3_aclarado.txt; salida = archivo";
            item.Ok = model != null && model.Ok
                && common != null
                && noInventedPath
                && safeDefaults
                && commonBlocksPath
                && pathQuestion != null
                && dialogueResolves;
            item.Message = item.Ok
                ? "Sin ruta explícita, file.read no inventa un archivo: pregunta la ruta y conserva sólo defaults técnicos seguros."
                : "La ruta faltante de file.read no quedó protegida. rutaNoInventada=" + noInventedPath
                    + ", defaults=" + safeDefaults
                    + ", bloqueaRuta=" + commonBlocksPath
                    + ", preguntaRuta=" + (pathQuestion != null)
                    + ", resuelve=" + dialogueResolves
                    + ", erroresComún=" + (common == null ? "n/a" : JoinList(common.Errors));
            return item;
        }

        private static ConstructionEquivalenceItem RunC2D4StateSetCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D4_STATE_SET_SIMPLE",
                "state.vars",
                "Guardar variable biz.prueba.d4 como OK_D4. Después finalizar.",
                new[] { "util.start", "state.vars", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "state.vars",
                    ["label"] = "Guardar variable",
                    ["params"] = new JObject
                    {
                        ["set"] = new JObject { ["biz.prueba.d4"] = "OK_D4" }
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D4StateRemoveCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D4_STATE_REMOVE",
                "state.vars",
                "Quitar variable biz.prueba.d4. Después finalizar.",
                new[] { "util.start", "state.vars", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "state.vars",
                    ["label"] = "Quitar variable",
                    ["params"] = new JObject
                    {
                        ["remove"] = new JArray("biz.prueba.d4")
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D4StateDeclarativeValueCase(WfAiCatalog catalog)
        {
            return RunEquivalenceCase(
                catalog,
                "C2D4_STATE_VALOR_DECLARATIVO",
                "state.vars",
                "Guardar variable biz.prueba.mensaje como Notificar internamente al rol COMPRAS. Después finalizar.",
                new[] { "util.start", "state.vars", "util.end" },
                new JObject
                {
                    ["action"] = "ADD_NODE",
                    ["nodeType"] = "state.vars",
                    ["label"] = "Guardar variable",
                    ["params"] = new JObject
                    {
                        ["set"] = new JObject { ["biz.prueba.mensaje"] = "Notificar internamente al rol COMPRAS" }
                    }
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2D4StateMissingChangeCase(WfAiCatalog catalog)
        {
            const string phrase = "Guardar variable. Después finalizar.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2D4_STATE_DATOS_FALTANTES",
                NodeType = "state.vars",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult common = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            JObject state = FindActionByType(common == null ? null : common.Plan, "state.vars");
            JObject parameters = state == null ? null : state["params"] as JObject;

            bool noInventedSet = parameters != null && !(parameters["set"] is JObject);
            bool noInventedRemove = parameters != null && !(parameters["remove"] is JArray);
            bool commonBlocks = common != null && common.Errors.Any(e =>
                (e ?? string.Empty).IndexOf("guardar o quitar", StringComparison.OrdinalIgnoreCase) >= 0);

            var draftBuilder = new WfAiInterpretationDraftBuilder();
            WfAiInterpretationDraft draft = common == null ? null : draftBuilder.Build(phrase, common.Plan, catalog);
            WfAiClarification changeQuestion = draft == null || draft.Clarifications == null
                ? null
                : draft.Clarifications.FirstOrDefault(c => c != null
                    && string.Equals(c.NodeType, "state.vars", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(c.Parameter, "stateVarsChange", StringComparison.OrdinalIgnoreCase)
                    && c.Blocking);

            bool dialogueResolves = false;
            if (changeQuestion != null)
            {
                const string answer = "biz.prueba.d4 = OK_D4";
                var answers = new JObject { [changeQuestion.Id] = answer };
                WfAiClarificationResolutionResult resolution = new WfAiClarificationResolver().Resolve(
                    common.Plan, draft, answers, catalog);
                WfAiResolvedPlanResult resolvedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                    resolution.Plan, phrase, "phrase_resolved");
                JObject resolvedState = FindActionByType(resolvedCommon == null ? null : resolvedCommon.Plan, "state.vars");
                JObject resolvedParams = resolvedState == null ? null : resolvedState["params"] as JObject;
                JObject resolvedSet = resolvedParams == null ? null : resolvedParams["set"] as JObject;
                WfAiInterpretationDraft resolvedDraft = resolvedCommon == null
                    ? null
                    : draftBuilder.Build(phrase, resolvedCommon.Plan, catalog);

                bool exact = resolvedSet != null
                    && string.Equals(Convert.ToString(resolvedSet["biz.prueba.d4"] ?? ""), "OK_D4", StringComparison.Ordinal);
                bool noBlocking = resolvedDraft == null || resolvedDraft.BlockingClarificationCount == 0;

                dialogueResolves = resolution != null
                    && resolution.Errors.Count == 0
                    && resolvedCommon != null
                    && resolvedCommon.Errors.Count == 0
                    && exact
                    && noBlocking;
            }

            item.PhraseJson = common == null || common.Plan == null ? "{}" : common.Plan.ToString(Formatting.None);
            item.StepJson = "biz.prueba.d4 = OK_D4";
            item.Ok = model != null && model.Ok
                && common != null
                && state != null
                && noInventedSet
                && noInventedRemove
                && commonBlocks
                && changeQuestion != null
                && dialogueResolves;
            item.Message = item.Ok
                ? "Sin variable/valor explícitos, state.vars no inventa datos: pregunta el cambio y la respuesta guiada deja un set válido."
                : "Los datos faltantes de state.vars no quedaron protegidos. setNoInventado=" + noInventedSet
                    + ", removeNoInventado=" + noInventedRemove
                    + ", bloquea=" + commonBlocks
                    + ", pregunta=" + (changeQuestion != null)
                    + ", resuelve=" + dialogueResolves
                    + ", erroresComún=" + (common == null ? "n/a" : JoinList(common.Errors));
            return item;
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalTotalCase(WfAiCatalog catalog)
        {
            return RunC2C2NaturalConditionCase(
                catalog,
                "C2C2_NATURAL_TOTAL_SUPERA",
                "Cargar una factura electrónica. Si el total supera 100000, finalizar.",
                new JObject
                {
                    ["field"] = "biz.factura.total",
                    ["op"] = ">",
                    ["value"] = "100000"
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalNoSuperaCase(WfAiCatalog catalog)
        {
            return RunC2C2NaturalConditionCase(
                catalog,
                "C2C2_NATURAL_NO_SUPERA_CASO_CONTRARIO",
                "Cargar una nota de crédito. Si no supera los 200000 pesos, enviarla a COMPRAS para revisar. Caso contrario, enviarla a DIR_GENERAL para aprobar. Después finalizar.",
                new JObject
                {
                    ["field"] = "biz.notaCredito.total",
                    ["op"] = ">",
                    ["value"] = "200000"
                },
                true);
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalCaeCase(WfAiCatalog catalog)
        {
            return RunC2C2NaturalConditionCase(
                catalog,
                "C2C2_NATURAL_CAE_INFORMADO",
                "Cargar una nota de crédito. Si tiene CAE, finalizar.",
                new JObject
                {
                    ["field"] = "biz.notaCredito.cae",
                    ["op"] = "not_empty"
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalAllCase(WfAiCatalog catalog)
        {
            return RunC2C2NaturalConditionCase(
                catalog,
                "C2C2_NATURAL_ALL",
                "Cargar una nota de crédito. Si tiene CAE, el total es mayor a 200000 y tiene al menos un ítem, enviarla a DIR_GENERAL para aprobar. Caso contrario, enviarla a COMPRAS para revisar. Después finalizar.",
                new JObject
                {
                    ["rulesMode"] = "all",
                    ["rules"] = new JArray
                    {
                        new JObject { ["field"] = "biz.notaCredito.cae", ["op"] = "not_empty" },
                        new JObject { ["field"] = "biz.notaCredito.itemsCount", ["op"] = ">", ["value"] = "0" },
                        new JObject { ["field"] = "biz.notaCredito.total", ["op"] = ">", ["value"] = "200000" }
                    }
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalAnyCase(WfAiCatalog catalog)
        {
            return RunC2C2NaturalConditionCase(
                catalog,
                "C2C2_NATURAL_ANY_NEGADO",
                "Cargar una nota de crédito. Si no tiene CAE o falta el comprobante asociado, enviarla a COMPRAS para corregir. Caso contrario, enviarla a ADM_FIN para aprobar. Después finalizar.",
                new JObject
                {
                    ["rulesMode"] = "any",
                    ["rules"] = new JArray
                    {
                        new JObject { ["field"] = "biz.notaCredito.cae", ["op"] = "empty" },
                        new JObject { ["field"] = "biz.notaCredito.comprobanteAsociado.numero", ["op"] = "empty" }
                    }
                },
                false);
        }

        private static ConstructionEquivalenceItem RunC2C2bNaturalBranchActionsCase(WfAiCatalog catalog)
        {
            const string phrase = "Cargar una factura electrónica. Si el total supera 100000, escribir el número de factura en C:\\temp\\SalidaSI.txt. Caso contrario, escribir el CUIT del emisor en C:\\temp\\SalidaNO.txt. Después finalizar.";
            var item = new ConstructionEquivalenceItem
            {
                Id = "C2C2B_RAMA_SI_NO_FILE_WRITE",
                NodeType = "control.if",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult common = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            JObject plan = common == null ? null : common.Plan;
            JObject condition = FindFirstBusinessIfAction(plan);
            JObject yesWrite = FindFileWriteActionByPath(plan, @"C:\temp\SalidaSI.txt");
            JObject noWrite = FindFileWriteActionByPath(plan, @"C:\temp\SalidaNO.txt");
            JObject end = FindActionByType(plan, "util.end");

            JObject conditionParams = condition == null ? null : condition["params"] as JObject;
            JObject yesParams = yesWrite == null ? null : yesWrite["params"] as JObject;
            JObject noParams = noWrite == null ? null : noWrite["params"] as JObject;

            bool conditionOk = conditionParams != null
                && string.Equals(Convert.ToString(conditionParams["field"] ?? ""), "biz.factura.total", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(conditionParams["op"] ?? ""), ">", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(conditionParams["value"] ?? ""), "100000", StringComparison.OrdinalIgnoreCase);

            bool writesOk = yesParams != null && noParams != null
                && string.Equals(Convert.ToString(yesParams["content"] ?? ""), "${biz.factura.numero}", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Convert.ToString(noParams["content"] ?? ""), "${biz.factura.emisor.cuit}", StringComparison.OrdinalIgnoreCase);

            string ifLabel = ActionLabelForRegression(condition);
            string yesLabel = ActionLabelForRegression(yesWrite);
            string noLabel = ActionLabelForRegression(noWrite);
            string endLabel = ActionLabelForRegression(end);
            bool connectionsOk = condition != null && yesWrite != null && noWrite != null && end != null
                && HasPlanConnection(plan, ifLabel, yesLabel, "SI")
                && HasPlanConnection(plan, ifLabel, noLabel, "NO")
                && HasPlanConnection(plan, yesLabel, endLabel, "")
                && HasPlanConnection(plan, noLabel, endLabel, "");

            JArray missing = plan == null ? null : plan["missingData"] as JArray;
            bool noArtificialMissing = missing == null || missing.Count == 0;

            item.PhraseJson = plan == null ? "{}" : plan.ToString(Formatting.None);
            item.StepJson = "SI => file.write C:\\temp\\SalidaSI.txt / ${biz.factura.numero}; NO => file.write C:\\temp\\SalidaNO.txt / ${biz.factura.emisor.cuit}";
            item.Ok = model != null && model.Ok
                && common != null && common.Errors.Count == 0
                && conditionOk && writesOk && connectionsOk && noArtificialMissing;
            item.Message = item.Ok
                ? "La frase completa conserva ambas acciones: SI escribe número de factura, NO escribe CUIT del emisor y ambas ramas convergen en Fin sin preguntas SI/NO artificiales."
                : "Falló la composición natural de ramas. Condición=" + conditionOk
                    + ", writes=" + writesOk
                    + ", conexiones=" + connectionsOk
                    + ", missingVacio=" + noArtificialMissing
                    + ", errorModelo=" + (model == null ? "n/a" : (model.ErrorMessage ?? string.Empty))
                    + ", erroresComún=" + (common == null ? "n/a" : JoinList(common.Errors));
            return item;
        }

        private static JObject FindFileWriteActionByPath(JObject plan, string path)
        {
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return null;
            foreach (JObject action in actions.OfType<JObject>())
            {
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(action["nodeType"] ?? ""), "file.write", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                string actualPath = p == null ? string.Empty : Convert.ToString(p["path"] ?? "").Trim();
                if (string.Equals(actualPath, path ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return action;
            }
            return null;
        }

        private static ConstructionEquivalenceItem RunC2C2NaturalConditionCase(
            WfAiCatalog catalog,
            string id,
            string phrase,
            JObject expectedParams,
            bool verifyNoSuperaInversion)
        {
            var item = new ConstructionEquivalenceItem
            {
                Id = id,
                NodeType = "control.if",
                Phrase = phrase
            };

            WfAiLocalModelResult model = new WfAiMlnetProvider().Interpret(phrase, catalog, "{}");
            WfAiResolvedPlanResult naturalCommon = model != null && model.Ok && model.Plan != null
                ? new WfAiResolvedNodeBuilder(catalog).ResolvePlan(model.Plan, phrase, "phrase")
                : null;
            WfAiResolvedPlanResult expectedCommon = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(
                C2C1IfPlan(expectedParams),
                string.Empty,
                "step_by_step");

            JObject naturalIf = FindFirstBusinessIfAction(naturalCommon == null ? null : naturalCommon.Plan);
            JObject expectedIf = FindActionByType(expectedCommon == null ? null : expectedCommon.Plan, "control.if");
            JObject naturalCanonical = CanonicalResolvedAction(naturalIf, "control.if");
            JObject expectedCanonical = CanonicalResolvedAction(expectedIf, "control.if");

            bool canonicalOk = model != null && model.Ok
                && naturalCommon != null && naturalCommon.Errors.Count == 0
                && expectedCommon != null && expectedCommon.Errors.Count == 0
                && naturalIf != null && expectedIf != null
                && JToken.DeepEquals(naturalCanonical, expectedCanonical);

            bool inversionOk = true;
            if (verifyNoSuperaInversion)
            {
                string ifLabel = naturalIf == null ? string.Empty : Convert.ToString(naturalIf["label"] ?? "").Trim();
                inversionOk = HasPlanConnection(naturalCommon == null ? null : naturalCommon.Plan, ifLabel, "Aprobación Dirección", "SI")
                    && HasPlanConnection(naturalCommon == null ? null : naturalCommon.Plan, ifLabel, "Enviar a Compras", "NO");
            }

            item.PhraseJson = naturalCanonical.ToString(Formatting.None);
            item.StepJson = expectedCanonical.ToString(Formatting.None);
            item.Ok = canonicalOk && inversionOk;
            item.Message = item.Ok
                ? "La frase natural produce el mismo control.if contractual de C2C1."
                    + (verifyNoSuperaInversion ? " «No supera» conserva la normalización histórica: IF total > umbral, SI al caso contrario y NO a la rama expresada." : string.Empty)
                : "La frase natural no convergió al contrato esperado. Canónico=" + canonicalOk
                    + ", inversiónNoSupera=" + inversionOk
                    + ", natural=" + item.PhraseJson
                    + ", esperado=" + item.StepJson
                    + ", errorModelo=" + (model == null ? "n/a" : (model.ErrorMessage ?? string.Empty))
                    + ", erroresComún=" + (naturalCommon == null ? "n/a" : JoinList(naturalCommon.Errors));
            return item;
        }

        private static JObject FindFirstBusinessIfAction(JObject plan)
        {
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return null;
            foreach (JObject action in actions.OfType<JObject>())
            {
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(action["nodeType"] ?? ""), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                string field = p == null ? string.Empty : Convert.ToString(p["field"] ?? "").Trim();
                if (string.Equals(field, "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;
                return action;
            }
            return null;
        }

        private static JObject FindHumanResultIfAction(JObject plan, string role)
        {
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return null;
            foreach (JObject action in actions.OfType<JObject>())
            {
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(action["nodeType"] ?? ""), "control.if", StringComparison.OrdinalIgnoreCase)) continue;
                JObject p = action["params"] as JObject;
                if (p == null) continue;
                if (!string.Equals(Convert.ToString(p["field"] ?? ""), "wf.tarea.resultado", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(p["value"] ?? ""), "apto", StringComparison.OrdinalIgnoreCase)) continue;
                string label = Convert.ToString(action["label"] ?? "");
                if (string.IsNullOrWhiteSpace(role) || label.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0) return action;
            }
            return null;
        }

        private static JObject FindOutcomeLoggerAction(JObject plan, string role, bool approved)
        {
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return null;
            string prefix = approved ? "Registrar aprobación de " : "Registrar rechazo de ";
            foreach (JObject action in actions.OfType<JObject>())
            {
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(action["nodeType"] ?? ""), "util.logger", StringComparison.OrdinalIgnoreCase)) continue;
                string label = Convert.ToString(action["label"] ?? "");
                if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(role) || label.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0))
                    return action;
            }
            return null;
        }

        private static string ActionLabelForRegression(JObject action)
        {
            return action == null ? string.Empty : Convert.ToString(action["label"] ?? "").Trim();
        }

        private static bool HasPlanConnection(JObject plan, string fromLabel, string toLabel, string condition)
        {
            if (plan == null || string.IsNullOrWhiteSpace(fromLabel) || string.IsNullOrWhiteSpace(toLabel)) return false;
            JArray connections = plan["proposedConnections"] as JArray;
            if (connections == null) return false;

            foreach (JObject c in connections.OfType<JObject>())
            {
                if (!string.Equals(Convert.ToString(c["from"] ?? "").Trim(), fromLabel, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Convert.ToString(c["to"] ?? "").Trim(), toLabel, StringComparison.OrdinalIgnoreCase)) continue;
                string actualCondition = Convert.ToString(c["condition"] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(condition))
                {
                    if (string.IsNullOrWhiteSpace(actualCondition) || string.Equals(actualCondition, "always", StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(actualCondition, condition, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string[] AddNodeTypes(JObject plan)
        {
            var result = new List<string>();
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return result.ToArray();

            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null) continue;
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;

                string nodeType = Convert.ToString(action["nodeType"] ?? "").Trim();
                if (nodeType.Length > 0) result.Add(nodeType);
            }

            return result.ToArray();
        }

        private static JObject FindActionByType(JObject plan, string nodeType)
        {
            JArray actions = plan == null ? null : plan["actions"] as JArray;
            if (actions == null) return null;
            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null) continue;
                if (!string.Equals(Convert.ToString(action["action"] ?? ""), "ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(Convert.ToString(action["nodeType"] ?? ""), nodeType, StringComparison.OrdinalIgnoreCase)) return action;
            }
            return null;
        }

        private static JObject CanonicalResolvedAction(JObject action, string nodeType)
        {
            var result = new JObject { ["nodeType"] = nodeType };
            var normalizedParams = new JObject();
            JObject actual = action == null ? null : action["params"] as JObject;
            WfAiNodeConstructionContract contract = WfAiConstructionContractRegistry.Find(nodeType);
            if (contract != null && actual != null)
            {
                foreach (WfAiParameterContract parameter in contract.Parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name)) continue;
                    JToken value = actual[parameter.Name];
                    if (value != null) normalizedParams[parameter.Name] = value.DeepClone();
                }
            }
            result["params"] = normalizedParams;
            return result;
        }

        private static string RenderConstructionEquivalence(ConstructionEquivalenceSummary summary)
        {
            if (summary == null || summary.Items == null || summary.Items.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            var c1 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C1_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2ab = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2AB_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2b = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2B_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2bg2 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2BG2_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2bg2b = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2BG2B_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2bg2c = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2BG2C_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2bg2d = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2BG2D_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2c1 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2C1_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2c2 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2C2_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2c2b = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2C2B_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2d1 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2D1_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2d2 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2D2_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2d3 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2D3_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2d4 = summary.Items.Where(x => x != null && (x.Id ?? string.Empty).StartsWith("C2D4_", StringComparison.OrdinalIgnoreCase)).ToList();
            var c2a = summary.Items.Where(x => x != null
                && (x.Id ?? string.Empty).StartsWith("C2A_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2AB_", StringComparison.OrdinalIgnoreCase)).ToList();

            if (c1.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Equivalencia de construcción FIX84C1b",
                    "Compara Frase ↔ Paso a paso para Logger y Queue Consume sobre el nodo resuelto común y verifica que la frase no genere nodos extra.",
                    c1));

            if (c2a.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Equivalencia de construcción FIX84C2A — Queue Publish",
                    "Compara Frase ↔ Paso a paso para contenido simple y estructurado; exige Inicio → queue.publish → Fin y conserva la forma real del mensaje.",
                    c2a));

            if (c2ab.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Equivalencia de construcción FIX84C2Ab — Sintaxis de precisión opcional",
                    "Valida que Nombre = valor sea una ayuda opcional: crea campos reales del mensaje y resuelve referencias humanas conocidas sin exigir JSON ni ${...}.",
                    c2ab));


            if (c2b.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Equivalencia de construcción FIX84C2Bf — Tarea humana",
                    "Compara Frase ↔ Paso a paso para destino por Rol y por Usuario, verifica que Dibujar propuesta no pida datos ya explícitos y que Rol + Usuario simultáneos obliguen a aclarar un único destino.",
                    c2b));

            if (c2bg2.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Semántica FIX84C2Bg2 — APTO / NO APTO de Human Task",
                    "Verifica que una tarea humana simple bifurque correctamente: APTO continúa por la salida normal, NO APTO ejecuta su rama y «rechaza» en lenguaje de diseño se interpreta como resultado negativo sin introducir backtrack en el Constructor.",
                    c2bg2));

            if (c2bg2b.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Semántica FIX84C2Bg2b — advertencia explícita en rama humana",
                    "Protege que «registrar una advertencia» se resuelva como util.logger Warn dentro de NO APTO, sin crear util.notify ni pedir una decisión contractual inexistente. La ausencia del fallback visual redundante se valida además en WorkflowUI.",
                    c2bg2b));

            if (c2bg2c.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Convergencia FIX84C2Bg2c — Paso a paso Human Task APTO / NO APTO",
                    "Valida el contrato objetivo de Paso a paso: la estructura APTO/NO APTO no se reconstruye desde la frase descriptiva, NO APTO conserva Logger Warn y las ramas convergen en un único Fin común.",
                    c2bg2c));

            if (c2bg2d.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Convergencia FIX84C2Bg2d — valores declarativos de Paso a paso",
                    "Protege que títulos, mensajes, descripciones y otros valores ya elegidos en editores estructurados sigan siendo datos. El caso usa Logger Warn / Message = Informar y exige que no aparezca util.notify.",
                    c2bg2d));

            if (c2c1.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Contrato común FIX84C2C1 — control.if",
                    "Verifica convergencia Frase ↔ Paso a paso para condición simple, operador sin valor y reglas múltiples ALL/ANY. Normaliza el nodo sin tocar sus ramas SI/NO ni el runtime.",
                    c2c1));

            if (c2c2.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Interpretación natural FIX84C2C2 — control.if",
                    "Verifica que frases naturales de total, presencia, no supera/caso contrario y reglas ALL/ANY lleguen al mismo contrato C2C1 antes de construir las ramas.",
                    c2c2));

            if (c2c2b.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Composición FIX84C2C2b — acciones naturales en ramas SI / NO",
                    "Valida de punta a punta que una condición natural conserve las acciones escritas en cada rama, sus parámetros y la convergencia final, sin pedir completar una rama ya expresada.",
                    c2c2b));

            if (c2d1.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Contrato común FIX84C2D1 — util.notify",
                    "Verifica Frase ↔ Paso a paso para destino por rol y por usuario real, y protege que el Constructor no invente el mensaje cuando falta: debe pedirlo mediante el diálogo de aclaración.",
                    c2d1));

            if (c2d2.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Contrato común FIX84C2D2 — file.write",
                    "Verifica Frase ↔ Paso a paso para texto literal y Datos disponibles, protege que path/content sean datos declarativos y exige aclaración real cuando faltan ruta o contenido.",
                    c2d2));

            if (c2d3.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Contrato común FIX84C2D3 — file.read",
                    "Verifica Frase ↔ Paso a paso para lectura de texto, salida/JSON explícitos, protege path como dato declarativo y exige aclaración real cuando falta la ruta.",
                    c2d3));

            if (c2d4.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Contrato común FIX84C2D4 — state.vars",
                    "Verifica Frase ↔ Paso a paso para guardar/quitar variables, protege claves y valores como datos declarativos y exige aclaración real cuando falta el cambio.",
                    c2d4));

            var other = summary.Items.Where(x => x != null
                && !(x.Id ?? string.Empty).StartsWith("C1_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2A_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2AB_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2B_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2BG2_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2BG2B_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2BG2C_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2BG2D_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2C1_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2C2_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2C2B_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2D1_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2D2_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2D3_", StringComparison.OrdinalIgnoreCase)
                && !(x.Id ?? string.Empty).StartsWith("C2D4_", StringComparison.OrdinalIgnoreCase)).ToList();
            if (other.Count > 0)
                sb.AppendLine(RenderConstructionEquivalenceGroup(
                    "Equivalencia de construcción",
                    "Controles adicionales de convergencia.",
                    other));

            return sb.ToString();
        }

        private static string RenderConstructionEquivalenceGroup(string title, string description, List<ConstructionEquivalenceItem> items)
        {
            int ok = items.Count(x => x.Ok);
            int total = items.Count;
            string badge = ok == total && total > 0 ? "ws-badge-ok" : "ws-badge-fail";
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"card ws-card mb-3\"><div class=\"card-body\">");
            sb.AppendLine("<div class=\"d-flex align-items-center justify-content-between flex-wrap gap-2 mb-2\"><div><div class=\"fw-bold\">" + Html(title) + "</div><div class=\"small ws-muted\">" + Html(description) + "</div></div><span class=\"ws-chip " + badge + "\">" + ok + "/" + total + "</span></div>");
            sb.AppendLine("<div class=\"ws-table-wrap\"><table class=\"table table-sm mb-0\"><thead class=\"table-light\"><tr><th>Caso</th><th>Nodo</th><th>Estado</th><th>Control</th></tr></thead><tbody>");
            foreach (ConstructionEquivalenceItem item in items)
            {
                string itemBadge = item.Ok ? "ws-badge-ok" : "ws-badge-fail";
                sb.AppendLine("<tr><td><strong>" + Html(item.Id) + "</strong><br/><span class=\"small ws-muted\">" + Html(item.Phrase) + "</span></td><td><span class=\"ws-node-chip\">" + Html(item.NodeType) + "</span></td><td><span class=\"ws-chip " + itemBadge + "\">" + (item.Ok ? "OK" : "FALLA") + "</span></td><td class=\"small\">" + Html(item.Message) + "</td></tr>");
            }
            sb.AppendLine("</tbody></table></div></div></div>");
            return sb.ToString();
        }

        private static string RenderDetails(List<AiRegressionRunResult> results)
        {
            var sb = new StringBuilder();

            foreach (var r in results)
            {
                string badge = r.Status == "OK" ? "ws-badge-ok" : (r.Status == "SKIP" ? "ws-badge-skip" : "ws-badge-fail");
                string safeId = SafeDomId(r.Case.Id);
                string jsonPreId = "wsAiJson_" + safeId;
                string nodeTypes = NodeTypesData(r.NodeTypes);

                sb.AppendLine("<div class=\"card ws-card mb-3 ws-ai-case-detail\" data-status=\"" + Attr(r.Status) + "\" data-node-types=\"" + Attr(nodeTypes) + "\"><div class=\"card-body\">");
                sb.AppendLine("<div class=\"d-flex align-items-start justify-content-between flex-wrap gap-2 mb-2\">");
                sb.AppendLine("<div><div class=\"fw-bold\">" + Html(r.Case.Id) + " — " + Html(r.Case.Name) + "</div><div class=\"small ws-muted\">" + Html(r.Case.Description) + "</div><div class=\"mt-1\">" + RenderNodeTypes(r.NodeTypes, 8) + RenderSemanticChip(r) + "</div></div>");
                sb.AppendLine("<span class=\"ws-chip " + badge + "\">" + Html(r.Status) + "</span>");
                sb.AppendLine("</div>");

                sb.AppendLine("<div class=\"ws-phrase-box small mb-2\"><strong>Frase:</strong> " + Html(r.Case.Phrase) + "</div>");
                sb.AppendLine("<div class=\"ws-case-actions mb-3\">");
                sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-copy-btn\" data-copy-value=\"" + Attr(r.Case.Phrase) + "\" data-copy-ok=\"Frase copiada.\">Copiar frase</button>");
                sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-primary ws-ai-open-constructor-btn\" data-phrase=\"" + Attr(r.Case.Phrase) + "\">Abrir Constructor IA con frase</button>");
                if (!string.IsNullOrWhiteSpace(r.PlanJson))
                    sb.AppendLine("<button type=\"button\" class=\"btn btn-sm btn-outline-secondary ws-ai-copy-btn\" data-copy-target=\"" + Attr(jsonPreId) + "\" data-copy-ok=\"JSON técnico copiado.\">Copiar JSON técnico</button>");
                sb.AppendLine("</div>");

                sb.AppendLine("<div class=\"ws-table-wrap mb-3\"><table class=\"table table-sm mb-0\"><thead class=\"table-light\"><tr><th style=\"width:90px\">Estado</th><th>Control</th></tr></thead><tbody>");

                foreach (var c in r.Checks)
                {
                    string cls = c.Skipped ? "ws-badge-skip" : (c.Ok ? "ws-badge-ok" : "ws-badge-fail");
                    string txt = c.Skipped ? "SKIP" : (c.Ok ? "OK" : "FALLA");
                    sb.AppendLine("<tr><td><span class=\"ws-chip " + cls + "\">" + txt + "</span></td><td>" + Html(c.Message) + "</td></tr>");
                }

                sb.AppendLine("</tbody></table></div>");

                if (!string.IsNullOrWhiteSpace(r.PlanJson))
                {
                    sb.AppendLine("<details><summary class=\"small fw-bold mb-2\">Ver JSON técnico generado</summary>");
                    sb.AppendLine("<pre id=\"" + Attr(jsonPreId) + "\" class=\"ws-pre\">" + Html(r.PlanJson) + "</pre>");
                    sb.AppendLine("</details>");
                }

                sb.AppendLine("</div></div>");
            }

            return sb.ToString();
        }

        private static string RenderStat(string label, int value, string chipClass)
        {
            return "<div class=\"ws-stat\"><div class=\"small ws-muted\">" + Html(label) + "</div><div class=\"num\">" + value + "</div><span class=\"ws-chip " + chipClass + "\">" + Html(label) + "</span></div>";
        }

        private static string RenderNodeTypeSummary(List<AiRegressionRunResult> results)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in results)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string nodeType in r.NodeTypes ?? new List<string>())
                {
                    string key = (nodeType ?? "").Trim();
                    if (key.Length == 0 || seen.Contains(key)) continue;
                    seen.Add(key);
                    if (!counts.ContainsKey(key)) counts[key] = 0;
                    counts[key]++;
                }
            }

            if (counts.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"mb-3\"><div class=\"small fw-bold mb-1\">Resumen por tipo de nodo</div>");
            sb.AppendLine("<div>");
            foreach (var kv in counts.OrderBy(x => x.Key))
                sb.AppendLine("<span class=\"ws-node-chip\">" + Html(kv.Key) + " <strong class=\"ms-1\">" + kv.Value + "</strong></span>");
            sb.AppendLine("</div></div>");
            return sb.ToString();
        }

        private static string RenderSemanticSmall(AiRegressionRunResult r)
        {
            if (r == null || !r.SemanticStrong) return "";
            return "<br/><span class=\"ws-chip ws-semantic-chip mt-1\">semántico fuerte</span>";
        }

        private static string RenderSemanticChip(AiRegressionRunResult r)
        {
            if (r == null || !r.SemanticStrong) return "";
            return "<span class=\"ws-chip ws-semantic-chip ms-1\">semántico fuerte</span>";
        }

        private static string RenderNodeTypes(List<string> nodeTypes, int max)
        {
            if (nodeTypes == null || nodeTypes.Count == 0)
                return "<span class=\"small ws-muted\">Sin nodos</span>";

            var sb = new StringBuilder();
            int take = Math.Max(1, max);
            foreach (string nodeType in nodeTypes.Take(take))
                sb.Append("<span class=\"ws-node-chip\">" + Html(nodeType) + "</span>");

            if (nodeTypes.Count > take)
                sb.Append("<span class=\"small ws-muted\">+" + (nodeTypes.Count - take) + "</span>");

            return sb.ToString();
        }

        private static string NodeTypesData(List<string> nodeTypes)
        {
            if (nodeTypes == null || nodeTypes.Count == 0) return "||";
            return "|" + string.Join("|", nodeTypes.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToArray()) + "|";
        }

        private static List<string> ExtractNodeTypes(JObject plan, AiRegressionCase item)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actions = plan == null ? null : plan["actions"] as JArray;

            if (actions != null)
            {
                foreach (JToken token in actions)
                {
                    JObject action = token as JObject;
                    if (action == null) continue;

                    string act = Convert.ToString(action["action"] ?? "").Trim();
                    string type = Convert.ToString(action["nodeType"] ?? "").Trim();
                    if (!act.Equals("ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                    if (type.Length == 0 || seen.Contains(type)) continue;

                    seen.Add(type);
                    result.Add(type);
                }
            }

            if (result.Count == 0)
                return ExpectedNodeTypes(item);

            return result;
        }

        private static List<string> ExpectedNodeTypes(AiRegressionCase item)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (item == null || item.Expected == null || item.Expected.Nodes == null)
                return result;

            foreach (var n in item.Expected.Nodes)
            {
                string type = (n == null ? "" : (n.Type ?? "")).Trim();
                if (type.Length == 0 || seen.Contains(type)) continue;
                seen.Add(type);
                result.Add(type);
            }

            return result;
        }

        private static bool IsStrongSemanticCase(AiRegressionCase item)
        {
            if (item == null || item.Expected == null)
                return false;

            return item.Expected.CheckSemanticOk
                && item.Expected.SemanticOk
                && item.Expected.CheckSemanticWarnings
                && item.Expected.SemanticWarningsEmpty
                && item.Expected.CheckSemanticErrors
                && item.Expected.SemanticErrorsEmpty;
        }

        private static string SafeDomId(string value)
        {
            string raw = value ?? "";
            var sb = new StringBuilder();
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(ch);
                else sb.Append('_');
            }
            if (sb.Length == 0) sb.Append("case");
            return sb.ToString();
        }

        private static string Attr(string value)
        {
            return HttpUtility.HtmlAttributeEncode(value ?? "");
        }

        private static JObject FindNode(JArray actions, NodeExpectation expected)
        {
            foreach (JToken token in actions)
            {
                JObject action = token as JObject;
                if (action == null) continue;

                string act = Convert.ToString(action["action"] ?? "").Trim();
                string type = Convert.ToString(action["nodeType"] ?? "").Trim();
                string label = Convert.ToString(action["label"] ?? "").Trim();

                if (!act.Equals("ADD_NODE", StringComparison.OrdinalIgnoreCase)) continue;
                if (!type.Equals(expected.Type ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(expected.Label) && !label.Equals(expected.Label, StringComparison.OrdinalIgnoreCase)) continue;

                return action;
            }

            return null;
        }

        private static JObject FindConnection(JArray connections, ConnectionExpectation expected)
        {
            foreach (JToken token in connections)
            {
                JObject c = token as JObject;
                if (c == null) continue;

                string from = Convert.ToString(c["from"] ?? "").Trim();
                string to = Convert.ToString(c["to"] ?? "").Trim();
                string condition = Convert.ToString(c["condition"] ?? "").Trim();

                if (!from.Equals(expected.From ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (!to.Equals(expected.To ?? "", StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrWhiteSpace(expected.Condition))
                {
                    string exp = NormalizeCondition(expected.Condition);
                    string act = NormalizeCondition(condition);
                    if (!exp.Equals(act, StringComparison.OrdinalIgnoreCase)) continue;
                }

                return c;
            }

            return null;
        }

        private static string NormalizeCondition(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == "si" || v == "sí" || v == "true") return "true";
            if (v == "no" || v == "false") return "false";
            if (v.Length == 0 || v == "always") return "always";
            return v;
        }

        private static string FormatCondition(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return "";
            return " [" + condition + "]";
        }

        private static bool ReadBool(JToken token)
        {
            if (token == null) return false;
            bool b;
            if (bool.TryParse(Convert.ToString(token), out b)) return b;
            return false;
        }

        private static string JoinList(List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(" | ", list.ToArray());
        }

        private static string Html(string value)
        {
            return HttpUtility.HtmlEncode(value ?? "");
        }

        private static string Safe(string value)
        {
            return value ?? "";
        }

        private static string TrimForTable(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length <= 220) return v;
            return v.Substring(0, 220) + "...";
        }

        private static List<AiRegressionCase> BuiltInCases()
        {
            return new List<AiRegressionCase>();
        }

        private class ConstructionEquivalenceSummary
        {
            public List<ConstructionEquivalenceItem> Items { get; set; }
            public ConstructionEquivalenceSummary() { Items = new List<ConstructionEquivalenceItem>(); }
        }

        private class ConstructionEquivalenceItem
        {
            public string Id { get; set; }
            public string NodeType { get; set; }
            public string Phrase { get; set; }
            public bool Ok { get; set; }
            public string Message { get; set; }
            public string PhraseJson { get; set; }
            public string StepJson { get; set; }
        }

        private class AiRegressionCase
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string Phrase { get; set; }
            public bool Enabled { get; set; }
            public AiRegressionDialogue Dialogue { get; set; }
            public AiRegressionExpectation Expected { get; set; }

            public void EnsureDefaults()
            {
                Id = Id ?? "";
                Name = Name ?? "";
                Description = Description ?? "";
                Phrase = Phrase ?? "";
                if (Dialogue != null) Dialogue.EnsureDefaults();
                if (Expected == null) Expected = new AiRegressionExpectation();
                Expected.EnsureDefaults();
            }
        }

        private class AiRegressionDialogue
        {
            public bool Enabled { get; set; }
            public int ExpectedInitialBlocking { get; set; }
            public int ExpectedFinalBlocking { get; set; }
            public string ExpectedQuestionContains { get; set; }
            public JObject Answers { get; set; }

            public void EnsureDefaults()
            {
                ExpectedQuestionContains = ExpectedQuestionContains ?? "";
                if (Answers == null) Answers = new JObject();
            }
        }

        private class AiRegressionExpectation
        {
            public bool CheckValidation { get; set; }
            public bool ValidationOk { get; set; }
            public bool CheckSemanticOk { get; set; }
            public bool SemanticOk { get; set; }
            public bool CheckSemanticWarnings { get; set; }
            public bool SemanticWarningsEmpty { get; set; }
            public bool CheckSemanticErrors { get; set; }
            public bool SemanticErrorsEmpty { get; set; }
            public bool CheckNodes { get; set; }
            public bool CheckConnections { get; set; }
            public List<NodeExpectation> Nodes { get; set; }
            public List<ConnectionExpectation> Connections { get; set; }

            public AiRegressionExpectation()
            {
                CheckValidation = true;
                ValidationOk = true;
                CheckSemanticOk = true;
                SemanticOk = true;
                CheckSemanticWarnings = true;
                SemanticWarningsEmpty = true;
                CheckSemanticErrors = true;
                SemanticErrorsEmpty = true;
                CheckNodes = true;
                CheckConnections = true;
                Nodes = new List<NodeExpectation>();
                Connections = new List<ConnectionExpectation>();
            }

            public void EnsureDefaults()
            {
                if (Nodes == null) Nodes = new List<NodeExpectation>();
                if (Connections == null) Connections = new List<ConnectionExpectation>();
                foreach (var n in Nodes) n.EnsureDefaults();
                foreach (var c in Connections) c.EnsureDefaults();
            }
        }

        private class NodeExpectation
        {
            public string Type { get; set; }
            public string Label { get; set; }
            public Dictionary<string, string> Params { get; set; }

            public void EnsureDefaults()
            {
                Type = Type ?? "";
                Label = Label ?? "";
                if (Params == null) Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private class ConnectionExpectation
        {
            public string From { get; set; }
            public string To { get; set; }
            public string Condition { get; set; }

            public void EnsureDefaults()
            {
                From = From ?? "";
                To = To ?? "";
                Condition = Condition ?? "";
            }
        }

        private class AiRegressionRunResult
        {
            public AiRegressionCase Case { get; set; }
            public DateTime StartedAt { get; set; }
            public string Status { get; set; }
            public List<AiRegressionCheck> Checks { get; set; }
            public string PlanJson { get; set; }
            public List<string> NodeTypes { get; set; }
            public bool SemanticStrong { get; set; }
        }

        private class AiRegressionCheck
        {
            public bool Ok { get; set; }
            public bool Skipped { get; set; }
            public string Message { get; set; }

            public static AiRegressionCheck Pass(string message)
            {
                return new AiRegressionCheck { Ok = true, Skipped = false, Message = message };
            }

            public static AiRegressionCheck Fail(string message)
            {
                return new AiRegressionCheck { Ok = false, Skipped = false, Message = message };
            }

            public static AiRegressionCheck Skip(string message)
            {
                return new AiRegressionCheck { Ok = false, Skipped = true, Message = message };
            }
        }
    }
}
