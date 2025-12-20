using Intranet.WorkflowStudio.WebForms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.Runtime
{
    // ====== NUEVO: Handler EMAIL SEND ======
    public class ManejadorEmailSend : IManejadorNodo
    {
        public string TipoNodo => "email.send";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            string from = GetString(p, "from");
            string subject = GetString(p, "subject");
            string html = GetString(p, "html");
            string text = GetString(p, "text");

            var toList = GetStrings(p, "to");
            var ccList = GetStrings(p, "cc");
            var bccList = GetStrings(p, "bcc");
            var attachments = GetStrings(p, "attachments");

            // Overrides opcionales
            string host = GetString(p, "host");
            int port = GetInt(p, "port", 25);
            bool enableSsl = GetBool(p, "enableSsl", false);
            string user = GetString(p, "user");
            string password = GetString(p, "password");

            try
            {
                using (var msg = new MailMessage())
                {
                    if (!string.IsNullOrWhiteSpace(from)) msg.From = new MailAddress(from);
                    foreach (var t in toList) msg.To.Add(t);
                    foreach (var c in ccList) msg.CC.Add(c);
                    foreach (var b in bccList) msg.Bcc.Add(b);

                    msg.Subject = subject ?? "(sin asunto)";
                    if (!string.IsNullOrEmpty(html))
                    {
                        msg.Body = html;
                        msg.IsBodyHtml = true;
                    }
                    else
                    {
                        msg.Body = text ?? "";
                        msg.IsBodyHtml = false;
                    }

                    foreach (var path in attachments)
                    {
                        try
                        {
                            if (File.Exists(path)) msg.Attachments.Add(new Attachment(path));
                            else ctx.Log($"[Email] adjunto no encontrado: {path}");
                        }
                        catch (Exception exAtt)
                        {
                            ctx.Log($"[Email] adjunto error '{path}': {exAtt.Message}");
                        }
                    }

                    using (var smtp = string.IsNullOrWhiteSpace(host) ? new SmtpClient() : new SmtpClient(host, port))
                    {
                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            smtp.EnableSsl = enableSsl;
                            if (!string.IsNullOrWhiteSpace(user))
                                smtp.Credentials = new System.Net.NetworkCredential(user, password ?? "");
                        }

                        smtp.Send(msg);
                    }
                }

                ctx.Log("[Email] enviado");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                ctx.Estado["email.lastError"] = ex.Message;
                ctx.Log("[Email] error: " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }

        private static string GetString(Dictionary<string, object> p, string key)
            => p.TryGetValue(key, out var v) ? Convert.ToString(v) : null;

        private static int GetInt(Dictionary<string, object> p, string key, int def = 0)
        {
            if (p.TryGetValue(key, out var v) && int.TryParse(Convert.ToString(v), out var i)) return i;
            return def;
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool def = false)
        {
            if (!p.TryGetValue(key, out var v)) return def;
            var s = Convert.ToString(v);
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }

        private static List<string> GetStrings(Dictionary<string, object> p, string key)
        {
            var res = new List<string>();
            if (!p.TryGetValue(key, out var v) || v == null) return res;

            if (v is Newtonsoft.Json.Linq.JArray ja) return ja.Select(x => x.ToString()).ToList();
            if (v is IEnumerable enumerable) return enumerable.Cast<object>().Select(o => Convert.ToString(o)).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var sraw = Convert.ToString(v);
            if (string.IsNullOrWhiteSpace(sraw)) return res;
            return sraw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
        }
    }
}