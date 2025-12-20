using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;


namespace Intranet.WorkflowStudio.WebForms
{
    public class HChatNotify : IManejadorNodo
    {
        public string TipoNodo { get { return "chat.notify"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var canal = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("canal") ? p["canal"] : ""));
            var mensaje = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("mensaje") ? p["mensaje"] : ""));
            var webhook = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("webhookUrl") ? p["webhookUrl"] : ""));

            if (!string.IsNullOrWhiteSpace(webhook))
            {
                try
                {
                    var payload = new { text = string.IsNullOrEmpty(mensaje) ? "(sin mensaje)" : mensaje };
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                    using (var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                    {
                        var resp = await ctx.Http.PostAsync(webhook, content, ct);
                        ctx.Log("[Chat] POST " + webhook + " => " + (int)resp.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    ctx.Log("[Chat] error webhook: " + ex.Message);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }
            else
            {
                ctx.Log("[Chat] (" + (canal ?? "canal") + "): " + (mensaje ?? ""));
            }

            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }
}