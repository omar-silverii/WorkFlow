using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    /// <summary>
    /// AiMock.ashx
    /// Mock local para probar ai.call sin Internet.
    ///
    /// QueryString:
    ///   - mode=ok|error|failOnce
    ///   - delayMs=0.. (simula latencia)
    ///
    /// Request JSON (opcional):
    ///   { "prompt": "...", "system": "...", "responseFormat": "text|json" }
    ///
    /// Response JSON (normalizado):
    ///   { ok, text, json, usage }
    /// </summary>
    public class AiMock : IHttpHandler
    {
        private static readonly ConcurrentDictionary<string, int> _hits = new ConcurrentDictionary<string, int>();

        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            var mode = (context.Request["mode"] ?? "ok").Trim();
            var delayMsStr = (context.Request["delayMs"] ?? "").Trim();
            int delayMs = 0;
            int.TryParse(delayMsStr, out delayMs);

            if (delayMs > 0)
                Thread.Sleep(Math.Min(delayMs, 60000));

            // Leer body (si viene)
            string body = null;
            try
            {
                context.Request.InputStream.Position = 0;
                using (var sr = new StreamReader(context.Request.InputStream))
                    body = sr.ReadToEnd();
            }
            catch { body = null; }

            string prompt = null;
            string system = null;
            string responseFormat = "text";

            JObject req = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try { req = JObject.Parse(body); } catch { req = null; }
            }

            if (req != null)
            {
                prompt = req["prompt"]?.ToString();
                system = req["system"]?.ToString();
                responseFormat = (req["responseFormat"]?.ToString() ?? "text").Trim().ToLowerInvariant();
            }

            // Clave por usuario+mode para failOnce
            var user = (context.User?.Identity?.Name ?? "anon").ToLowerInvariant();
            var key = mode + "|" + user;

            if (mode.Equals("failOnce", StringComparison.OrdinalIgnoreCase))
            {
                int n = _hits.AddOrUpdate(key, 1, (_, old) => old + 1);
                if (n == 1)
                {
                    WriteError(context, 500, "falla intencional (failOnce) hit=1");
                    return;
                }

                WriteOk(context, prompt, system, responseFormat, note: "failOnce hit=" + n);
                return;
            }

            if (mode.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                // simulamos rate limit
                WriteError(context, 429, "rate limited (mock)");
                return;
            }

            // default OK
            WriteOk(context, prompt, system, responseFormat, note: "ok");
        }

        private static void WriteError(HttpContext context, int statusCode, string msg)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var payload = new
            {
                ok = false,
                error = new { message = msg },
                usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
            };

            context.Response.Write(JsonConvert.SerializeObject(payload, Formatting.None));
        }

        private static void WriteOk(HttpContext context, string prompt, string system, string responseFormat, string note)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";

            // respuesta “humana” (texto)
            var text = "OK (AiMock): " + (string.IsNullOrWhiteSpace(note) ? "" : note);

            // json “extraído”
            JToken json = null;
            if (string.Equals(responseFormat, "json", StringComparison.OrdinalIgnoreCase))
            {
                json = new JObject
                {
                    ["numero"] = "OC-10025",
                    ["proveedor"] = "ACME S.A.",
                    ["importe"] = 125000,
                    ["note"] = note
                };
            }

            // usage fake (aprox por longitud)
            int pt = string.IsNullOrWhiteSpace(prompt) ? 0 : Math.Max(1, prompt.Length / 4);
            int ct = string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, text.Length / 4);

            var payload = new JObject
            {
                ["ok"] = true,
                ["text"] = text,
                ["json"] = json,
                ["usage"] = new JObject
                {
                    ["prompt_tokens"] = pt,
                    ["completion_tokens"] = ct,
                    ["total_tokens"] = (pt + ct)
                },
                ["debug"] = new JObject
                {
                    ["system"] = system ?? "",
                    ["responseFormat"] = responseFormat ?? "text"
                }
            };

            context.Response.Write(payload.ToString(Formatting.None));
        }
    }
}
