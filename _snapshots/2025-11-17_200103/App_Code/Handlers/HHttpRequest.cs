using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers; // <— para ContentType
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HHttpRequest : IManejadorNodo
    {
        public string TipoNodo { get { return "http.request"; } }

        //Omar

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var method = (GetString(p, "method") ?? "GET").Trim().ToUpperInvariant();
            var url = GetString(p, "url");
            var query = GetDict(p, "query");
            var headers = GetDict(p, "headers");
            var contentType = GetString(p, "contentType"); // puede ser sobrescrito por header "Content-Type"
            var body = GetObj(p, "body");
            var timeoutMs = GetInt(p, "timeoutMs", 10000);

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("http.request: falta 'url'");

            var uri = BuildUri(ctx.Http.BaseAddress, url, query);

            // Armamos el request
            var req = new HttpRequestMessage(new HttpMethod(method), uri);

            // Separamos headers normales de headers de contenido (Content-*)
            var pendingContentHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in headers)
            {
                var key = kv.Key ?? "";
                var val = Convert.ToString(kv.Value);

                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    // Si vino por headers, tiene prioridad sobre param contentType
                    contentType = val;
                    continue;
                }

                if (key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    pendingContentHeaders[key] = val;
                    continue;
                }

                // Headers "normales"
                req.Headers.TryAddWithoutValidation(key, val);
            }

            // Adjuntar contenido SOLO si el verbo lo soporta
            var allowsBody = method == "POST" || method == "PUT" || method == "PATCH" || method == "DELETE" || method == "OPTIONS";
            if (allowsBody && body != null)
            {
                if (body is string s)
                {
                    req.Content = new StringContent(
                        s,
                        System.Text.Encoding.UTF8,
                        string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType
                    );
                }
                else
                {
                    var json = JsonConvert.SerializeObject(body);
                    req.Content = new StringContent(
                        json,
                        System.Text.Encoding.UTF8,
                        string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType
                    );
                }

                // Aplico el resto de headers de contenido
                foreach (var ch in pendingContentHeaders)
                {
                    // Content-Type ya se manejó arriba
                    if (!ch.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        req.Content.Headers.TryAddWithoutValidation(ch.Key, ch.Value);
                }
            }

            // Timeout enlazado
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeoutMs);

            try
            {
                var resp = await ctx.Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, linked.Token);
                var status = (int)resp.StatusCode;
                var text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";

                // Estado para IF / nodos siguientes
                ctx.Estado["payload.status"] = status;
                ctx.Estado["payload.body"] = text;

                // Intento parsear JSON para conveniencia (no obligatorio)
                try
                {
                    if (!string.IsNullOrWhiteSpace(text) && resp.Content.Headers.ContentType != null &&
                        resp.Content.Headers.ContentType.MediaType != null &&
                        resp.Content.Headers.ContentType.MediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ctx.Estado["payload.json"] = JsonConvert.DeserializeObject<object>(text);
                    }
                }
                catch { /* ignorar parse fallido */ }

                ctx.Log("[HTTP] " + method + " " + uri + " => " + status);
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (TaskCanceledException)
            {
                ctx.Estado["payload.status"] = 408;
                ctx.Estado["payload.body"] = "timeout";
                ctx.Log("[HTTP] timeout: " + method + " " + uri);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
            catch (Exception ex)
            {
                ctx.Estado["payload.status"] = 500;
                ctx.Estado["payload.body"] = ex.Message;
                ctx.Log("[HTTP] error: " + ex.Message);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        // ===== Helpers =====
        static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : null;
        }
        static int GetInt(Dictionary<string, object> d, string k, int def = 0)
        {
            if (d == null) return def;
            object v; if (!d.TryGetValue(k, out v) || v == null) return def;
            int i; return int.TryParse(Convert.ToString(v), out i) ? i : def;
        }
        static object GetObj(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v; return d.TryGetValue(k, out v) ? v : null;
        }
        static Dictionary<string, object> GetDict(Dictionary<string, object> d, string k)
        {
            var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (d == null) return res;
            object v; if (!d.TryGetValue(k, out v) || v == null) return res;

            if (v is Newtonsoft.Json.Linq.JObject jo)
                return jo.ToObject<Dictionary<string, object>>();

            if (v is Dictionary<string, object> dd) return new Dictionary<string, object>(dd, StringComparer.OrdinalIgnoreCase);
            return res;
        }

        static Uri BuildUri(Uri baseAddress, string url, Dictionary<string, object> query)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u))
            {
                // Base: ctx.Http.BaseAddress o bien HttpContext actual
                var baseUri = baseAddress;
                if (baseUri == null)
                {
                    var http = HttpContext.Current;
                    if (http != null && http.Request != null)
                    {
                        var left = http.Request.Url.GetLeftPart(UriPartial.Authority);   // https://localhost:44350
                        var app = http.Request.ApplicationPath ?? "/";
                        if (!app.EndsWith("/")) app += "/";
                        baseUri = new Uri(new Uri(left), app);
                    }
                    else
                    {
                        throw new InvalidOperationException("http.request: URL relativa sin BaseAddress y sin HttpContext actual");
                    }
                }

                var relative = url.TrimStart('/');
                if (!Uri.TryCreate(baseUri, relative, out u))
                    throw new InvalidOperationException("http.request: no se pudo armar la URL con BaseAddress");
            }

            if (query != null && query.Count > 0)
            {
                var ub = new UriBuilder(u);
                var nv = HttpUtility.ParseQueryString(ub.Query);
                foreach (var kv in query) nv[kv.Key] = Convert.ToString(kv.Value);
                ub.Query = nv.ToString();
                u = ub.Uri;
            }
            return u;
        }
    }
}
