using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Intranet.WorkflowStudio.WebForms;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WF_AiAssistant : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;

            try
            {
                if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context, new { ok = false, error = "WF_AiAssistant acepta solo POST." });
                    return;
                }

                string body;
                using (var sr = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                    body = sr.ReadToEnd();

                var req = JsonConvert.DeserializeObject<WfAiAssistantRequest>(body ?? "{}") ?? new WfAiAssistantRequest();
                req.UserText = (req.UserText ?? "").Trim();
                req.Mode = (req.Mode ?? "interpret").Trim();
                req.SourceKind = (req.SourceKind ?? "phrase").Trim();

                var catalog = new WfAiCatalogProvider().Build();

                // FIX84C1/FIX84C2A/FIX84C2Ab/FIX84C2B: el Constructor paso a paso envía su plan candidato a este mismo
                // endpoint para que util.logger, queue.consume, queue.publish y human.task pasen por la misma capa C#
                // de resolución contractual que usa la ruta por frase.
                if (string.Equals(req.Mode, "normalize_plan", StringComparison.OrdinalIgnoreCase))
                {
                    if (req.Plan == null)
                    {
                        WriteJson(context, new { ok = false, error = "normalize_plan requiere un plan candidato." });
                        return;
                    }

                    var common = new WfAiResolvedNodeBuilder(catalog).ResolvePlan(req.Plan, req.UserText, req.SourceKind);
                    var normalizedValidation = new WfAiPlanValidator().Validate(common.Plan, catalog) ?? new WfAiValidationResult();
                    foreach (string error in common.Errors)
                    {
                        normalizedValidation.Ok = false;
                        normalizedValidation.Errors.Add(error);
                    }
                    foreach (string warning in common.Warnings)
                        normalizedValidation.Warnings.Add(warning);

                    WriteJson(context, new
                    {
                        ok = true,
                        provider = "contract-common",
                        model = "FIX84C2Bf nodo resuelto común",
                        plan = common.Plan,
                        validation = new
                        {
                            ok = normalizedValidation.Ok,
                            errors = normalizedValidation.Errors,
                            warnings = normalizedValidation.Warnings
                        },
                        fix84c1 = common,
                        fix84c2a = common,
                        fix84c2ab = common,
                        fix84c2b = common,
                        catalogWarnings = catalog.Warnings,
                        messageToUser = "Plan normalizado por la capa común FIX84C2Bf."
                    });
                    return;
                }

                if (req.UserText.Length == 0)
                {
                    WriteJson(context, new { ok = false, error = "Escribí una intención para el Asistente IA." });
                    return;
                }
                WfAiNaturalPhraseContext naturalContext = WfAiNaturalPhraseContext.Analyze(req.UserText);
                var model = new WfAiMlnetProvider().Interpret(req.UserText, catalog, req.WorkflowJson);

                if (!model.Ok)
                {
                    WriteJson(context, new
                    {
                        ok = false,
                        provider = model.Provider,
                        model = model.Model,
                        error = model.ErrorMessage,
                        catalogWarnings = catalog.Warnings,
                        messageToUser = "No pude interpretar la intención con el proveedor IA configurado. Revisá el error técnico."
                    });
                    return;
                }

                // FIX84B mantiene al proveedor legacy como generador del plan base y aplica las
                // respuestas estructuradas encima de una copia. Cada POST reconstruye todo desde cero.
                var builder = new WfAiInterpretationDraftBuilder();
                var commonBuilder = new WfAiResolvedNodeBuilder(catalog);
                JObject providerPlan = model.Plan == null ? new JObject() : model.Plan;
                WfAiResolvedPlanResult baseCommon = commonBuilder.ResolvePlan(providerPlan, req.UserText, "phrase");
                JObject basePlan = baseCommon.Plan;
                WfAiInterpretationDraft baseDraft = builder.Build(req.UserText, basePlan, catalog);

                bool hasAnswers = req.ClarificationAnswers != null && req.ClarificationAnswers.HasValues;
                bool stale = hasAnswers
                    && (string.IsNullOrWhiteSpace(req.InterpretationFingerprint)
                        || !string.Equals(req.InterpretationFingerprint, baseDraft.Fingerprint, StringComparison.OrdinalIgnoreCase));

                var resolution = new WfAiClarificationResolutionResult
                {
                    Plan = (JObject)basePlan.DeepClone()
                };

                if (!stale && hasAnswers)
                    resolution = new WfAiClarificationResolver().Resolve(basePlan, baseDraft, req.ClarificationAnswers, catalog);

                JObject resolvedCandidatePlan = resolution.Plan ?? (JObject)basePlan.DeepClone();
                WfAiResolvedPlanResult effectiveCommon = commonBuilder.ResolvePlan(resolvedCandidatePlan, req.UserText, "phrase_resolved");
                JObject effectivePlan = effectiveCommon.Plan;
                WfAiInterpretationDraft interpretationDraft = builder.Build(
                    req.UserText,
                    effectivePlan,
                    catalog,
                    resolution.AcceptedAnswerIds,
                    baseDraft.Fingerprint);

                var validation = new WfAiPlanValidator().Validate(effectivePlan, catalog);
                if (validation == null) validation = new WfAiValidationResult();
                if (stale)
                {
                    validation.Ok = false;
                    validation.Errors.Add("La propuesta cambió desde la última aclaración. Volvé a verificar la frase antes de continuar.");
                }
                foreach (string error in resolution.Errors)
                {
                    validation.Ok = false;
                    validation.Errors.Add(error);
                }
                foreach (string error in effectiveCommon.Errors)
                {
                    validation.Ok = false;
                    validation.Errors.Add(error);
                }
                foreach (string warning in effectiveCommon.Warnings)
                    validation.Warnings.Add(warning);

                bool dialogueReady = !stale
                    && resolution.Errors.Count == 0
                    && effectiveCommon.Errors.Count == 0
                    && interpretationDraft.RegistryErrors.Count == 0
                    && interpretationDraft.BlockingClarificationCount == 0;

                WriteJson(context, new
                {
                    ok = true,
                    provider = model.Provider,
                    model = model.Model,
                    plan = effectivePlan,
                    validation = new
                    {
                        ok = validation.Ok,
                        errors = validation.Errors,
                        warnings = validation.Warnings
                    },
                    // Se conserva fix84a por compatibilidad diagnóstica con lo ya validado.
                    fix84a = new
                    {
                        active = true,
                        contractVersion = WfAiConstructionContractRegistry.Version,
                        coveredNodeTypes = WfAiConstructionContractRegistry.CoveredNodeTypes(),
                        interpretationDraft = interpretationDraft,
                        error = ""
                    },
                    fix84b = new
                    {
                        active = true,
                        version = "fix84b2-dialog-v2",
                        fingerprint = baseDraft.Fingerprint,
                        stale = stale,
                        ready = dialogueReady,
                        interpretationDraft = interpretationDraft,
                        acceptedAnswerIds = resolution.AcceptedAnswerIds,
                        appliedAnswers = resolution.AppliedAnswers,
                        rejectedAnswers = resolution.RejectedAnswers,
                        naturalContext = naturalContext,
                        errors = resolution.Errors,
                        warnings = resolution.Warnings
                    },
                    fix84c1 = new
                    {
                        active = true,
                        version = effectiveCommon.Version,
                        sourceKind = effectiveCommon.SourceKind,
                        ok = effectiveCommon.Ok,
                        nodes = effectiveCommon.Nodes,
                        errors = effectiveCommon.Errors,
                        warnings = effectiveCommon.Warnings
                    },
                    fix84c2a = new
                    {
                        active = true,
                        version = effectiveCommon.Version,
                        sourceKind = effectiveCommon.SourceKind,
                        ok = effectiveCommon.Ok,
                        nodes = effectiveCommon.Nodes,
                        errors = effectiveCommon.Errors,
                        warnings = effectiveCommon.Warnings
                    },
                    fix84c2ab = new
                    {
                        active = true,
                        version = effectiveCommon.Version,
                        sourceKind = effectiveCommon.SourceKind,
                        ok = effectiveCommon.Ok,
                        nodes = effectiveCommon.Nodes,
                        errors = effectiveCommon.Errors,
                        warnings = effectiveCommon.Warnings
                    },
                    fix84c2b = new
                    {
                        active = true,
                        version = effectiveCommon.Version,
                        sourceKind = effectiveCommon.SourceKind,
                        ok = effectiveCommon.Ok,
                        nodes = effectiveCommon.Nodes,
                        errors = effectiveCommon.Errors,
                        warnings = effectiveCommon.Warnings
                    },
                    catalogWarnings = catalog.Warnings,
                    messageToUser = Convert.ToString(effectivePlan["messageToUser"] ?? "Propuesta recibida del modelo local.")
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                WriteJson(context, new { ok = false, error = ex.Message });
            }
        }

        private static void WriteJson(HttpContext context, object payload)
        {
            context.Response.Write(JsonConvert.SerializeObject(payload, Formatting.None));
        }
    }
}
