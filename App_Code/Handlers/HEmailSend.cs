using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "email.send".
    /// Envía un correo usando SMTP o, en modo "simulado", solo loguea.
    /// Parámetros esperados (node.Parameters):
    ///   - from: string
    ///   - to: string[] o CSV
    ///   - cc: string[] o CSV (opcional)
    ///   - subject: string
    ///   - body: string
    ///   - isHtml: bool
    ///   - modo: "simulado" | "real"
    ///   - host: string (SMTP)
    ///   - port: int (por defecto 25)
    ///   - user: string (opcional)
    ///   - password: string (opcional)
    ///   - enableSsl: bool
    /// 
    /// En éxito ⇒ Etiqueta = "always"
    /// En error  ⇒ Etiqueta = "error"
    /// </summary>
    public class HEmailSend : IManejadorNodo
    {
        public string TipoNodo => "email.send";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string modo = GetString(p, "modo") ?? "simulado";
            string rawFrom = GetString(p, "from");
            string rawSubject = GetString(p, "subject");
            string rawBody = GetString(p, "body");
            bool isHtml = GetBool(p, "isHtml", defaultValue: true);

            // Host / puerto / credenciales
            string host = GetString(p, "host");
            int port = GetInt(p, "port", 25);
            string user = GetString(p, "user");
            string password = GetString(p, "password");
            bool enableSsl = GetBool(p, "enableSsl");

            // Expandimos con variables del contexto (${...})
            string from = ctx.ExpandString(rawFrom ?? string.Empty);
            string subject = ctx.ExpandString(rawSubject ?? string.Empty);
            string body = ctx.ExpandString(rawBody ?? string.Empty);

            var toList = GetStringList(p, "to");
            var ccList = GetStringList(p, "cc");

            // Log de parámetros
            ctx.Log($"[email.send] modo={modo}, host={host}, port={port}, to=[{string.Join(";", toList)}]");

            // Validaciones mínimas
            if (toList.Count == 0)
            {
                ctx.Log("[email.send/error] Parámetro 'to' vacío.");
                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            if (string.Equals(modo, "simulado", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(host))
            {
                // Modo simulación: no se conecta a SMTP
                ctx.Log("[email.send/simulado] No se envía correo real. " +
                        $"From={from}, To={string.Join(",", toList)}, Subject={subject}");
                ctx.Log("[email.send/simulado] Body:");
                ctx.Log(body);

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }

            try
            {
                using (var msg = new MailMessage())
                {
                    if (!string.IsNullOrWhiteSpace(from))
                        msg.From = new MailAddress(from);

                    foreach (var addr in toList)
                    {
                        msg.To.Add(addr);
                    }

                    foreach (var addr in ccList)
                    {
                        msg.CC.Add(addr);
                    }

                    msg.Subject = subject ?? string.Empty;
                    msg.Body = body ?? string.Empty;
                    msg.IsBodyHtml = isHtml;

                    using (var client = new SmtpClient(host, port))
                    {
                        // Si hay usuario, usamos NetworkCredential
                        if (!string.IsNullOrWhiteSpace(user))
                        {
                            client.UseDefaultCredentials = false;
                            client.Credentials = new NetworkCredential(user, password ?? string.Empty);
                        }
                        else
                        {
                            // En muchos intranets se usa auth integrada
                            client.UseDefaultCredentials = true;
                        }

                        client.EnableSsl = enableSsl;

                        client.Send(msg);
                    }
                }

                ctx.Log("[email.send] Correo enviado correctamente.");

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }
            catch (Exception ex)
            {
                ctx.Log("[email.send/error] " + ex.GetType().Name + ": " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }
        }

        // ===== Helpers =====

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool GetBool(IDictionary<string, object> p, string key, bool defaultValue = false)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(Convert.ToString(v), out var b2)) return b2;
                if (int.TryParse(Convert.ToString(v), out var i)) return i != 0;
            }
            return defaultValue;
        }

        private static int GetInt(IDictionary<string, object> p, string key, int defaultValue)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (int.TryParse(Convert.ToString(v), out var i)) return i;
            }
            return defaultValue;
        }

        private static List<string> GetStringList(IDictionary<string, object> p, string key)
        {
            var list = new List<string>();

            if (p == null) return list;
            if (!p.TryGetValue(key, out var v) || v == null) return list;

            if (v is IEnumerable<object> objEnum && !(v is string))
            {
                foreach (var item in objEnum)
                {
                    var s = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s.Trim());
                }
            }
            else
            {
                var csv = Convert.ToString(v);
                if (!string.IsNullOrWhiteSpace(csv))
                {
                    foreach (var s in csv.Split(','))
                    {
                        var t = s.Trim();
                        if (!string.IsNullOrWhiteSpace(t))
                            list.Add(t);
                    }
                }
            }

            return list;
        }
    }
}
