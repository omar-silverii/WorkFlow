using System;
using System.IO;
using System.Text;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    // Endpoint local para simular Slack/Webhook
    // POST /Api/ChatWebhookMock.ashx
    // Devuelve 200 OK y loguea el body recibido.
    public class ChatWebhookMock : IHttpHandler
    {
        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405; // Method Not Allowed
                context.Response.Write("{\"ok\":false,\"error\":\"Use POST\"}");
                return;
            }

            string body;
            using (var sr = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = sr.ReadToEnd();
            }

            // Log a Output/Debug (lo ves en VS Output)
            System.Diagnostics.Debug.WriteLine("[ChatWebhookMock] Body: " + body);

            // También lo devolvemos para ver rápido que llegó
            context.Response.StatusCode = 200;
            context.Response.Write("{\"ok\":true,\"echo\":" + ToJsonString(body) + "}");
        }

        public bool IsReusable { get { return false; } }

        private static string ToJsonString(string s)
        {
            if (s == null) return "null";
            return "\"" + s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") + "\"";
        }
    }
}
