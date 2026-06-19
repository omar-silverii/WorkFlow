using System;
using System.Linq;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using Intranet.WorkflowStudio.WebForms;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WF_AiCatalog : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;

            try
            {
                if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context, new { ok = false, error = "WF_AiCatalog acepta solo GET." });
                    return;
                }

                var catalog = new WfAiCatalogProvider().Build();

                WriteJson(context, new
                {
                    ok = true,
                    nodes = catalog.Nodes.Select(n => new
                    {
                        type = n.Type,
                        label = n.Label,
                        @params = n.Params
                    }).ToList(),
                    docTypes = catalog.DocTypes.Select(d => new
                    {
                        codigo = d.Codigo,
                        nombre = d.Nombre,
                        contextPrefix = d.ContextPrefix,
                        motorExtraccion = d.MotorExtraccion
                    }).ToList(),
                    roles = catalog.Roles.Select(r => new
                    {
                        RolKey = r,
                        Nombre = r
                    }).ToList(),
                    users = catalog.Users.Select(u => new
                    {
                        userKey = u.UserKey,
                        displayName = u.DisplayName
                    }).ToList(),
                    fields = catalog.Fields.Select(f => new
                    {
                        path = f.Path,
                        label = f.Label,
                        docTipo = f.DocTipo
                    }).ToList(),
                    warnings = catalog.Warnings
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
