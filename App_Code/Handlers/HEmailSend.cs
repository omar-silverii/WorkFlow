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
    /// 
    /// Parámetros esperados (node.Parameters):
    ///   - from: string
    ///   - to: string[] o CSV
    ///   - cc: string[] o CSV (opcional)
    ///   - subject: string
    ///   - body: string (compatibilidad)
    ///   - html: string (usado por el inspector nuevo)
    ///   - isHtml: bool (por defecto true)
    ///   - modo: "simulado" | "real"
    ///   - host: string (SMTP)           // nombre viejo
    ///   - smtp: string (SMTP)           // nombre nuevo (inspector)
    ///   - port: int (por defecto 25)
    ///   - user: string (opcional)
    ///   - password: string (opcional)
    ///   - pass: string (alias de password)
    ///   - enableSsl: bool (nombre viejo)
    ///   - ssl: bool (nombre nuevo)
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

            // ==== Parámetros básicos ====
            string modo = GetString(p, "modo") ?? "simulado";
            string rawFrom = GetString(p, "from");
            string rawSubject = GetString(p, "subject");

            // body/html: primero "html" (inspector nuevo), si no, "body" (compatibilidad)
            string rawBody = GetString(p, "html");
            if (string.IsNullOrEmpty(rawBody))
                rawBody = GetString(p, "body");

            bool isHtml = GetBool(p, "isHtml", defaultValue: true);

            // Host / puerto / credenciales
            string host = GetString(p, "host");    // nombre viejo
            string smtp = GetString(p, "smtp");    // nombre del inspector nuevo
            if (string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(smtp))
                host = smtp;

            int port = GetInt(p, "port", 25);
            string user = GetString(p, "user");

            // password / pass (alias)
            string password = GetString(p, "password");
            if (string.IsNullOrWhiteSpace(password))
                password = GetString(p, "pass");

            // enableSsl / ssl (alias)
            bool enableSsl = GetBool(p, "enableSsl", defaultValue: false);
            bool sslNew = GetBool(p, "ssl", defaultValue: enableSsl);
            enableSsl = sslNew;

            // Expandimos con variables del contexto (${...})
            string from = ctx.ExpandString(rawFrom ?? string.Empty);
            string subject = ctx.ExpandString(rawSubject ?? string.Empty);
            string body = ctx.ExpandString(rawBody ?? string.Empty);

            var toList = GetStringList(p, "to");
            var ccList = GetStringList(p, "cc");

            // Log de parámetros
            string hostInfo = string.IsNullOrWhiteSpace(host) ? "(web.config)" : host;
            ctx.Log($"[email.send] modo={modo}, host={hostInfo}, port={port}, to=[{string.Join(";", toList)}]");

            // Validaciones mínimas
            if (toList.Count == 0)
            {
                ctx.Log("[email.send/error] Parámetro 'to' vacío.");
                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            bool modoSimulado = string.Equals(modo, "simulado", StringComparison.OrdinalIgnoreCase);

            // ===== MODO SIMULADO =====
            if (modoSimulado)
            {
                ctx.Log("[email.send/simulado] No se envía correo real. " +
                        $"From={from}, To={string.Join(",", toList)}, Subject={subject}");

                ctx.Log("[email.send/simulado] Body:");
                ctx.Log(body ?? string.Empty);

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }

            // ===== MODO REAL =====
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

                    bool useWebConfig = GetBool(p, "useWebConfig", defaultValue: false);
                    using (var client = CreateSmtpClient(host, port, user, password, enableSsl, useWebConfig))
                    {
                        ct.ThrowIfCancellationRequested();
                        client.Send(msg);   // sync, .NET 4.0 safe

                        ctx.Log("[email.send] Correo enviado correctamente.");
                    }
                }

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

        // ===== Helpers SMTP =====

        private static SmtpClient CreateSmtpClient(string host, int port, string user, string password, bool enableSsl, bool useWebConfig)
        {
            // ✅ Si se pide web.config, NO pisamos nada: mailSettings manda.
            if (useWebConfig || string.IsNullOrWhiteSpace(host))
            {
                return new SmtpClient(); // usa <system.net><mailSettings> del web.config
            }

            var client = new SmtpClient(host, port);
            client.EnableSsl = enableSsl;

            if (!string.IsNullOrWhiteSpace(user))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(user, password ?? string.Empty);
            }
            else
            {
                client.UseDefaultCredentials = true;
            }

            return client;
        }


        // ===== Helpers de lectura de parámetros =====

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

            // Si viene como array de objetos (JSON), lo recorremos
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
                // Si viene como CSV
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
