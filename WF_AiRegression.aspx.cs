using System;
using System.Collections.Generic;
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

            RenderResults(new List<AiRegressionRunResult> { RunCase(item) });
        }

        protected void btnRunAll_Click(object sender, EventArgs e)
        {
            pnlIntro.Visible = false;

            var cases = LoadCases();
            var results = new List<AiRegressionRunResult>();

            foreach (var item in cases)
                results.Add(RunCase(item));

            RenderResults(results);
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

                JObject plan = model.Plan;
                run.PlanJson = plan.ToString(Formatting.Indented);
                run.NodeTypes = ExtractNodeTypes(plan, item);

                var validation = new WfAiPlanValidator().Validate(plan, catalog);
                EvaluateValidation(run, item, validation);
                EvaluateSemantic(run, item, plan);
                EvaluateNodes(run, item, plan);
                EvaluateConnections(run, item, plan);

                run.Status = run.Checks.Any(x => !x.Ok && !x.Skipped) ? "FALLA" : "OK";
            }
            catch (Exception ex)
            {
                run.Checks.Add(AiRegressionCheck.Fail("Excepción ejecutando el caso: " + ex.Message));
                run.Status = "FALLA";
            }

            return run;
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

        private void RenderResults(List<AiRegressionRunResult> results)
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
            litSummary.Text = sb.ToString();

            litDetails.Text = RenderDetails(results);
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

        private class AiRegressionCase
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string Phrase { get; set; }
            public bool Enabled { get; set; }
            public AiRegressionExpectation Expected { get; set; }

            public void EnsureDefaults()
            {
                Id = Id ?? "";
                Name = Name ?? "";
                Description = Description ?? "";
                Phrase = Phrase ?? "";
                if (Expected == null) Expected = new AiRegressionExpectation();
                Expected.EnsureDefaults();
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
