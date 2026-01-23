using System;
using System.Collections.Concurrent;
using System.Net;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class RetryTest : IHttpHandler
    {
        // Estado simple en memoria: "failOnce" falla 1 vez por clave y luego OK
        private static readonly ConcurrentDictionary<string, int> _hits = new ConcurrentDictionary<string, int>();

        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            var mode = (context.Request["mode"] ?? "").Trim();
            if (string.IsNullOrEmpty(mode)) mode = "ok";

            // Clave por "mode + usuario" (podés cambiarla por SessionID si querés)
            var user = (context.User?.Identity?.Name ?? "anon").ToLowerInvariant();
            var key = mode + "|" + user;

            if (mode.Equals("failOnce", StringComparison.OrdinalIgnoreCase))
            {
                int n = _hits.AddOrUpdate(key, 1, (_, old) => old + 1);

                if (n == 1)
                {
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json";
                    context.Response.Write("{\"ok\":false,\"mode\":\"failOnce\",\"hit\":1,\"msg\":\"falla intencional\"}");
                    return;
                }

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.Write("{\"ok\":true,\"mode\":\"failOnce\",\"hit\":" + n + "}");
                return;
            }

            // default OK
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.Write("{\"ok\":true,\"mode\":\"" + mode + "\"}");
        }
    }
}
