using Newtonsoft.Json;
using Newtonsoft.Json.Linq;   // <<< NUEVO para trabajar con JToken/JObject
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

                // <<< NUEVO: intentar parsear JSON y exponerlo bien en ctx.Estado >>>
                try
                {
                    var mediaType = resp.Content?.Headers?.ContentType?.MediaType;
                    if (!string.IsNullOrWhiteSpace(text) &&
                        mediaType != null &&
                        mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Parseo como JSON
                        var tok = JToken.Parse(text);

                        // Raíz JSON accesible como "payload"
                        ctx.Estado["payload"] = tok;

                        // Compatibilidad con lo que ya tenías
                        ctx.Estado["payload.json"] = tok;

                        // Aplanar: payload.ok, payload.status, payload.nombre, payload.documento, etc.
                        FlattenJson("payload", tok, ctx.Estado);
                    }
                }
                catch
                {
                    // ignorar parse fallido; dejamos solo payload.body/payload.status
                }

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

        // <<< NUEVO: helper para aplanar el JSON en ctx.Estado >>>
        private static void FlattenJson(string prefix, JToken token, IDictionary<string, object> state)
        {
            if (token == null) return;

            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        var key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
                        var val = prop.Value;

                        if (val is JValue jv)
                        {
                            state[key] = jv.Value;        // ej: payload.nombre = "María García"
                        }
                        else
                        {
                            state[key] = val;             // guarda subárbol
                            FlattenJson(key, val, state); // y además lo aplana recursivamente
                        }
                    }
                    break;

                case JTokenType.Array:
                    var arr = (JArray)token;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var item = arr[i];
                        var key = $"{prefix}[{i}]";       // ej: payload.productos[0]
                        if (item is JValue jv2)
                        {
                            state[key] = jv2.Value;
                        }
                        else
                        {
                            state[key] = item;
                            FlattenJson(key, item, state);
                        }
                    }
                    break;

                default:
                    // Primitivos ya manejados arriba
                    break;
            }
        }
    }
}
