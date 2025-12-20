using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HNotify : IManejadorNodo
    {
        public string TipoNodo { get { return "util.notify"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var tipo = (p.ContainsKey("tipo") ? Convert.ToString(p["tipo"]) : "email").ToLowerInvariant();

            if (tipo == "email")
            {
                try
                {
                    var destino = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("destino") ? p["destino"] : ""));
                    var asunto = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("asunto") ? p["asunto"] : "Notificación"));
                    var mensaje = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("mensaje") ? p["mensaje"] : ""));

                    if (string.IsNullOrWhiteSpace(destino))
                    {
                        ctx.Log("[Email] destino vacío; no se envía.");
                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    var smtpSection = System.Configuration.ConfigurationManager
                        .GetSection("system.net/mailSettings/smtp") as System.Net.Configuration.SmtpSection;
                    var fromAddr = (smtpSection != null && !string.IsNullOrWhiteSpace(smtpSection.From))
                                    ? smtpSection.From : "no-reply@localhost";

                    using (var msg = new System.Net.Mail.MailMessage(fromAddr, destino, asunto ?? "Notificación", mensaje ?? string.Empty))
                    {
                        msg.IsBodyHtml = (mensaje ?? "").IndexOf("<", StringComparison.Ordinal) >= 0;
                        using (var smtp = new System.Net.Mail.SmtpClient())
                        {
                            await smtp.SendMailAsync(msg);
                        }
                    }
                    ctx.Log("[Email] enviado a " + destino);
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
                catch (Exception ex)
                {
                    ctx.Log("[Email] error: " + ex.Message);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }

            // Otros tipos (sms/webhook) no implementados en este handler mínimo
            ctx.Log("[Notify] tipo '" + tipo + "' no implementado; se ignora.");
            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }
}