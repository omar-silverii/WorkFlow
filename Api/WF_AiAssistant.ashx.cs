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

                if (req.UserText.Length == 0)
                {
                    WriteJson(context, new { ok = false, error = "Escribí una intención para el Asistente IA." });
                    return;
                }

                var catalog = new WfAiCatalogProvider().Build();
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
                JObject basePlan = model.Plan == null ? new JObject() : model.Plan;
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

                JObject effectivePlan = resolution.Plan ?? (JObject)basePlan.DeepClone();
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

                bool dialogueReady = !stale
                    && resolution.Errors.Count == 0
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
                        version = "fix84b-dialog-v1",
                        fingerprint = baseDraft.Fingerprint,
                        stale = stale,
                        ready = dialogueReady,
                        interpretationDraft = interpretationDraft,
                        acceptedAnswerIds = resolution.AcceptedAnswerIds,
                        appliedAnswers = resolution.AppliedAnswers,
                        errors = resolution.Errors,
                        warnings = resolution.Warnings
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
