// App_Code/Handlers/HAiCall.cs
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// ai.call
    /// Cliente HTTP genérico para “IA enchufable” (gateway interno / OpenAI / Azure / on-prem),
    /// sin acoplar el motor a un proveedor específico.
    ///
    /// Parameters:
    ///   - url              (string, obligatorio)  Endpoint del proveedor
    ///   - client           (string, opcional)     "external" (default) | "intranet"
    ///   - method           (string, opcional)     Default: POST
    ///   - headers          (object, opcional)     { "Authorization": "Bearer ${secrets.ai.token}", ... } (templatable)
    ///   - prompt           (string, obligatorio)  Prompt final (templatable)
    ///   - system           (string, opcional)     Contexto/sistema (templatable)
    ///   - responseFormat   (string, opcional)     "text" (default) | "json"
    ///   - timeoutMs        (int, opcional)        Default: 30000
    ///   - output           (string, opcional)     Default: "ai"
    ///
    /// Output (en ctx.Estado, con prefijo = output):
    ///   - {output}.status  (int)
    ///   - {output}.raw     (string)  body crudo (solo para debug; NO contiene secrets)
    ///   - {output}.text    (string)
    ///   - {output}.json    (JToken)  si se pudo parsear
    ///   - {output}.usage.* (si viene en respuesta)
    ///
    /// Etiquetas:
    ///   - always
    ///   - error  (http>=400, timeout, excepción, json inválido cuando responseFormat=json)
    /// </summary>
    public class HAiCall : IManejadorNodo
    {
        public string TipoNodo { get { return "ai.call"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            var url = TemplateUtil.Expand(ctx, GetString(p, "url"));
            var client = (TemplateUtil.Expand(ctx, GetString(p, "client")) ?? "").Trim().ToLowerInvariant();
            var method = (TemplateUtil.Expand(ctx, GetString(p, "method")) ?? "POST").Trim().ToUpperInvariant();
            var prompt = TemplateUtil.Expand(ctx, GetString(p, "prompt"));
            var system = TemplateUtil.Expand(ctx, GetString(p, "system"));
            var responseFormat = (TemplateUtil.Expand(ctx, GetString(p, "responseFormat")) ?? "text").Trim().ToLowerInvariant();
            var timeoutMs = GetInt(p, "timeoutMs", 30000);
            var output = (TemplateUtil.Expand(ctx, GetString(p, "output")) ?? "ai").Trim();
            if (string.IsNullOrWhiteSpace(output)) output = "ai";

            var headers = GetDict(p, "headers"); // values templatable

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("ai.call: falta 'url'");

            if (string.IsNullOrWhiteSpace(prompt))
            {
                ctx.Log("[ai.call/error] Falta 'prompt'.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // Elegir cliente HTTP (coherente con IIS Express + Windows Auth)
            // - si client=intranet => ctx.HttpIntranet
            // - si url es relativa (/Api/...) o localhost => intranet por defecto
            var http = ctx.Http;
            bool useIntranet =
                client == "intranet" ||
                (string.IsNullOrWhiteSpace(client) && (IsRelativeUrl(url) || IsLocalhostUrl(url)));

            if (useIntranet)
            {
                // Si no existe HttpIntranet, cae a Http (no rompe)
                var intranet = GetHttpIntranetOrNull(ctx);
                if (intranet != null) http = intranet;
            }

            // Request estable (contrato del motor)
            var reqObj = new
            {
                prompt = prompt,
                system = string.IsNullOrWhiteSpace(system) ? null : system,
                responseFormat = responseFormat
            };

            var json = JsonConvert.SerializeObject(reqObj, Formatting.None);

            var req = new HttpRequestMessage(new HttpMethod(method), url);
            req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // Headers (templatable)
            foreach (var kv in headers)
            {
                var hk = kv.Key ?? "";
                var hv = TemplateUtil.Expand(ctx, Convert.ToString(kv.Value));
                if (string.IsNullOrWhiteSpace(hk)) continue;

                // Content-Type se maneja por Content
                if (hk.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;

                req.Headers.TryAddWithoutValidation(hk, hv);
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (timeoutMs > 0) linked.CancelAfter(timeoutMs);

                try
                {
                    var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, linked.Token);
                    var status = (int)resp.StatusCode;
                    var body = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";

                    ctx.Estado[output + ".status"] = status;
                    ctx.Estado[output + ".raw"] = body;

                    if (status >= 400)
                    {
                        ctx.Log("[ai.call/error] HTTP " + status + " url=" + url);
                        return new ResultadoEjecucion { Etiqueta = "error" };
                    }

                    string outText = null;
                    JToken outJson = null;

                    JToken tok = null;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try { tok = JToken.Parse(body); } catch { tok = null; }
                    }

                    if (tok != null)
                    {
                        outText = tok["text"]?.ToString();

                        var j = tok["json"];
                        if (j != null && j.Type != JTokenType.Null) outJson = j;

                        if (outText == null)
                            outText = tok["message"]?.ToString() ?? tok["content"]?.ToString();

                        if (outText == null)
                            outText = tok.SelectToken("choices[0].message.content")?.ToString();

                        if (outText == null)
                            outText = tok["output_text"]?.ToString();

                        var usage = tok["usage"];
                        if (usage != null && usage.Type == JTokenType.Object)
                        {
                            TrySetInt(ctx, output + ".usage.promptTokens", usage["prompt_tokens"]);
                            TrySetInt(ctx, output + ".usage.completionTokens", usage["completion_tokens"]);
                            TrySetInt(ctx, output + ".usage.totalTokens", usage["total_tokens"]);
                            TrySetInt(ctx, output + ".usage.inputTokens", usage["input_tokens"]);
                            TrySetInt(ctx, output + ".usage.outputTokens", usage["output_tokens"]);
                        }
                    }
                    else
                    {
                        outText = body;
                    }

                    if (outJson == null && string.Equals(responseFormat, "json", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(outText))
                        {
                            try { outJson = JToken.Parse(outText); }
                            catch
                            {
                                ctx.Log("[ai.call/error] responseFormat=json pero la respuesta no es JSON válido.");
                                ctx.Estado[output + ".text"] = outText ?? "";
                                return new ResultadoEjecucion { Etiqueta = "error" };
                            }
                        }
                    }

                    if (outText != null) ctx.Estado[output + ".text"] = outText;
                    if (outJson != null) ctx.Estado[output + ".json"] = outJson;
                    if (outJson != null) FlattenJsonToEstado(ctx, output + ".json", outJson);

                    ctx.Log("[ai.call] OK url=" + url + " status=" + status + " format=" + responseFormat + " output=" + output);
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
                catch (TaskCanceledException)
                {
                    ctx.Estado[output + ".status"] = 408;
                    ctx.Estado[output + ".raw"] = "timeout";
                    ctx.Log("[ai.call/error] timeout (" + timeoutMs + "ms) url=" + url);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
                catch (Exception ex)
                {
                    ctx.Estado[output + ".status"] = 500;
                    ctx.Estado[output + ".raw"] = ex.Message;
                    ctx.Log("[ai.call/error] " + ex.Message);
                    ctx.Log("[ai.call/error] url=" + url);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }
        }

        // ===== Helpers =====

        private static HttpClient GetHttpIntranetOrNull(ContextoEjecucion ctx)
        {
            try
            {
                // Propiedad existente en tu motor
                return ctx.HttpIntranet;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsRelativeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.StartsWith("/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalhostUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.IndexOf("://localhost", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("://127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : null;
        }

        private static int GetInt(Dictionary<string, object> d, string k, int def = 0)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return def;
            int i;
            return int.TryParse(Convert.ToString(v), out i) ? i : def;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string k)
        {
            var res = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (d == null) return res;

            object v;
            if (!d.TryGetValue(k, out v) || v == null) return res;

            if (v is JObject jo)
                return jo.ToObject<Dictionary<string, object>>();

            if (v is Dictionary<string, object> dd)
                return new Dictionary<string, object>(dd, StringComparer.OrdinalIgnoreCase);

            return res;
        }

        private static void TrySetInt(ContextoEjecucion ctx, string key, JToken tok)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(key) || tok == null) return;

            try
            {
                if (tok.Type == JTokenType.Integer)
                {
                    ctx.Estado[key] = tok.Value<int>();
                    return;
                }

                var s = tok.ToString();
                if (int.TryParse(s, out var i))
                    ctx.Estado[key] = i;
            }
            catch { }
        }

        private static void FlattenJsonToEstado(ContextoEjecucion ctx, string prefix, JToken tok, int maxDepth = 6)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(prefix) || tok == null) return;
            FlattenInner(ctx, prefix, tok, 0, maxDepth);

            void FlattenInner(ContextoEjecucion c, string pfx, JToken t, int depth, int md)
            {
                if (t == null) return;
                if (depth > md) return;

                if (t.Type == JTokenType.Object)
                {
                    var obj = (JObject)t;
                    foreach (var prop in obj.Properties())
                    {
                        var key = pfx + "." + prop.Name;
                        var val = prop.Value;

                        if (val == null || val.Type == JTokenType.Null)
                        {
                            c.Estado[key] = null;
                            continue;
                        }

                        if (val.Type == JTokenType.Object || val.Type == JTokenType.Array)
                        {
                            // guardo el token crudo también (por si el UI quiere mostrarlo)
                            c.Estado[key] = val;
                            FlattenInner(c, key, val, depth + 1, md);
                        }
                        else
                        {
                            c.Estado[key] = ((JValue)val).Value;
                        }
                    }
                    return;
                }

                if (t.Type == JTokenType.Array)
                {
                    var arr = (JArray)t;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var key = pfx + "[" + i + "]";
                        var val = arr[i];

                        if (val == null || val.Type == JTokenType.Null)
                        {
                            c.Estado[key] = null;
                            continue;
                        }

                        if (val.Type == JTokenType.Object || val.Type == JTokenType.Array)
                        {
                            c.Estado[key] = val;
                            FlattenInner(c, key, val, depth + 1, md);
                        }
                        else
                        {
                            c.Estado[key] = ((JValue)val).Value;
                        }
                    }
                }
            }
        }

    }
}
