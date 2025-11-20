using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HEmail : IManejadorNodo
    {
        public string TipoNodo { get { return "email.send"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            var from = GetString(p, "from");          // opcional (si mailSettings define "from", puede omitirse)
            var to = GetList(p, "to");                // obligatorio (string o array)
            var subject = GetString(p, "subject") ?? "(sin asunto)";
            var html = GetString(p, "html") ?? "";

            if (to.Count == 0)
                throw new InvalidOperationException("email.send: falta 'to'");

            var host = GetString(p, "smtp");          // si no se especifica, se usa web.config
            var port = GetInt(p, "port", 25);
            var ssl = GetBool(p, "ssl", false);
            var user = GetString(p, "user");
            var pass = GetString(p, "pass");

            using (var mail = new MailMessage())
            {
                if (!string.IsNullOrWhiteSpace(from)) mail.From = new MailAddress(from);
                foreach (var addr in to) mail.To.Add(addr);

                mail.Subject = subject;
                mail.Body = html;
                mail.IsBodyHtml = true;

                using (var smtp = string.IsNullOrWhiteSpace(host) ? new SmtpClient() : new SmtpClient(host, port))
                {
                    smtp.EnableSsl = ssl;
                    if (!string.IsNullOrWhiteSpace(user))
                        smtp.Credentials = new System.Net.NetworkCredential(user, pass ?? "");

                    // Nota: si no hay host/user/pass, usará <system.net><mailSettings> del web.config
                    await smtp.SendMailAsync(mail);
                }
            }

            ctx.Log("[Email] enviado a " + string.Join(",", to));
            return new ResultadoEjecucion { Etiqueta = "always" };
        }

        // ==== Helpers ====
        static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : null;
        }
        static bool GetBool(Dictionary<string, object> d, string k, bool def = false)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            bool b; if (bool.TryParse(Convert.ToString(v), out b)) return b;
            int i; if (int.TryParse(Convert.ToString(v), out i)) return i != 0;
            return def;
        }
        static int GetInt(Dictionary<string, object> d, string k, int def = 0)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            int i; return int.TryParse(Convert.ToString(v), out i) ? i : def;
        }
        static List<string> GetList(Dictionary<string, object> d, string k)
        {
            var res = new List<string>();
            if (d == null) return res;
            object v; if (!d.TryGetValue(k, out v) || v == null) return res;

            var arr = v as IEnumerable<object>;
            if (arr != null) { foreach (var x in arr) if (x != null) res.Add(Convert.ToString(x)); }
            else res.Add(Convert.ToString(v));

            return res;
        }
    }
}
