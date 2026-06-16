using System;
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

                var validation = new WfAiPlanValidator().Validate(model.Plan, catalog);

                WriteJson(context, new
                {
                    ok = true,
                    provider = model.Provider,
                    model = model.Model,
                    plan = model.Plan,
                    validation = new
                    {
                        ok = validation.Ok,
                        errors = validation.Errors,
                        warnings = validation.Warnings
                    },
                    catalogWarnings = catalog.Warnings,
                    messageToUser = Convert.ToString(model.Plan["messageToUser"] ?? "Propuesta recibida del modelo local.")
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
