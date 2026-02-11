using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "doc.search"
    /// - El workflow NO maneja archivos; solo referencias documentales.
    /// - Busca documentos en el DMS (vía HTTP) por criterios/índices y devuelve una lista de referencias.
    ///
    /// Parámetros:
    ///   searchUrl            (string)  URL absoluta/relativa del endpoint de búsqueda (si no viene, usa ${wf.dms.searchUrl})
    ///   criteria             (object)  criterios/índices a enviar (strings soportan ${...})
    ///   max                  (int)     máximo de ítems (opcional)
    ///   useIntranetCredentials (bool)  si true usa ctx.HttpIntranet (Windows credentials) si está disponible
    ///   viewerUrlTemplate    (string)  template para armar viewerUrl, ej: "https://dms/visor?doc={documentoId}"
    ///                              (si no viene, usa ${wf.dms.viewerUrlTemplate})
    ///   output               (string)  path destino en estado (default: "biz.doc.search")
    ///
    /// Respuesta esperada del endpoint:
    ///   - Array JSON de items, o
    ///   - Objeto con "items": [...]
    ///
    /// Cada item se normaliza a:
    ///   documentoId, carpetaId, ficheroId, tipo, indices, viewerUrl
    /// </summary>
    public class HDocSearch : IManejadorNodo
    {
        public string TipoNodo => "doc.search";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var outputPath = (GetString(p, "output") ?? "biz.doc.search").Trim();
            var useIntranet = GetBool(p, "useIntranetCredentials", true);

            // URL del endpoint (permite ${...})
            var urlTpl = GetString(p, "searchUrl");
            if (string.IsNullOrWhiteSpace(urlTpl))
                urlTpl = ctx.ExpandString("${wf.dms.searchUrl}");

            var searchUrl = ctx.ExpandString(urlTpl ?? "");
            if (string.IsNullOrWhiteSpace(searchUrl))
                throw new InvalidOperationException("doc.search: falta 'searchUrl' (o wf.dms.searchUrl en estado).");

            // criteria (objeto) - expandimos strings recursivamente
            var criteriaObj = GetObj(p, "criteria");
            JToken criteriaTok = criteriaObj == null ? new JObject() : JToken.FromObject(criteriaObj);
            ExpandStringsRecursive(ctx, criteriaTok);

            int? max = null;
            if (p.TryGetValue("max", out var vmax) && vmax != null)
            {
                if (int.TryParse(Convert.ToString(vmax, CultureInfo.InvariantCulture), out var imax) && imax > 0)
                    max = imax;
            }

            var payload = new JObject
            {
                ["criteria"] = criteriaTok
            };
            if (max.HasValue) payload["max"] = max.Value;

            var viewerTpl = GetString(p, "viewerUrlTemplate");
            if (string.IsNullOrWhiteSpace(viewerTpl))
                viewerTpl = ctx.ExpandString("${wf.dms.viewerUrlTemplate}");
            viewerTpl = ctx.ExpandString(viewerTpl ?? "");

            var client = PickClient(ctx, useIntranet);
            var started = DateTime.UtcNow;

            ctx.Log($"[doc.search] POST {searchUrl}");

            using (var req = new HttpRequestMessage(HttpMethod.Post, searchUrl))
            {
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (var resp = await client.SendAsync(req, ct))
                {
                    var txt = await resp.Content.ReadAsStringAsync();
                    var took = (int)Math.Max(0, (DateTime.UtcNow - started).TotalMilliseconds);

                    if (!resp.IsSuccessStatusCode)
                    {
                        // No cambiamos estado global del motor: dejamos trazabilidad en wf.error.*
                        ContextoEjecucion.SetPath(ctx.Estado, "wf.error", true);
                        ContextoEjecucion.SetPath(ctx.Estado, "wf.error.message", $"doc.search HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
                        ContextoEjecucion.SetPath(ctx.Estado, "wf.error.detail", txt);
                        ctx.Log($"[doc.search] ERROR HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                        return new ResultadoEjecucion { Etiqueta = "error" };
                    }

                    JToken data;
                    try
                    {
                        data = string.IsNullOrWhiteSpace(txt) ? new JArray() : JToken.Parse(txt);
                    }
                    catch
                    {
                        data = new JObject { ["raw"] = txt };
                    }

                    var items = ExtractItemsArray(data);
                    var outItems = new JArray();

                    foreach (var it in items)
                    {
                        if (it == null || it.Type == JTokenType.Null) continue;

                        var norm = NormalizeDocRef(it as JObject ?? (it.Type == JTokenType.Object ? (JObject)it : new JObject()));

                        // viewerUrl: si no viene, lo armamos con template
                        if ((norm["viewerUrl"] == null || string.IsNullOrWhiteSpace(norm.Value<string>("viewerUrl"))) && !string.IsNullOrWhiteSpace(viewerTpl))
                        {
                            var viewerUrl = ApplyViewerTemplate(viewerTpl, norm);
                            if (!string.IsNullOrWhiteSpace(viewerUrl))
                                norm["viewerUrl"] = viewerUrl;
                        }

                        outItems.Add(norm);
                    }

                    var result = new JObject
                    {
                        ["status"] = (int)resp.StatusCode,
                        ["count"] = outItems.Count,
                        ["tookMs"] = took,
                        ["items"] = outItems
                    };

                    // Guardar en estado (biz.* por default)
                    ContextoEjecucion.SetPath(ctx.Estado, outputPath, result);

                    ctx.Log($"[doc.search] OK items={outItems.Count} tookMs={took}");
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
            }
        }

        private static HttpClient PickClient(ContextoEjecucion ctx, bool useIntranetCredentials)
        {
            if (useIntranetCredentials && ctx.HttpIntranet != null)
                return ctx.HttpIntranet;
            return ctx.Http;
        }

        private static JArray ExtractItemsArray(JToken data)
        {
            if (data == null) return new JArray();

            if (data.Type == JTokenType.Array) return (JArray)data;

            if (data.Type == JTokenType.Object)
            {
                var obj = (JObject)data;
                var items = obj["items"];
                if (items != null && items.Type == JTokenType.Array) return (JArray)items;
            }

            return new JArray();
        }

        private static JObject NormalizeDocRef(JObject src)
        {
            // Aceptamos diferentes nombres provenientes del DMS, pero devolvemos el contrato estándar.
            var o = new JObject();

            o["documentoId"] = FirstNonEmpty(src, "documentoId", "docId", "idDocumento", "id");
            o["carpetaId"]   = FirstNonEmpty(src, "carpetaId", "folderId", "idCarpeta");
            o["ficheroId"]   = FirstNonEmpty(src, "ficheroId", "fileId", "idFichero", "fichero");
            o["tipo"]        = FirstNonEmpty(src, "tipo", "docTipo", "tipoDocumento");

            // índices (objeto)
            var indices = src["indices"];
            if (indices == null || (indices.Type != JTokenType.Object && indices.Type != JTokenType.Array))
            {
                // fallback: si viene "index" o "fields"
                indices = src["index"] ?? src["fields"];
            }
            if (indices != null) o["indices"] = indices.DeepClone();

            // viewerUrl opcional
            var viewerUrl = FirstNonEmpty(src, "viewerUrl", "visorUrl", "urlVisor", "viewer");
            if (!string.IsNullOrWhiteSpace(viewerUrl))
                o["viewerUrl"] = viewerUrl;

            return o;
        }

        private static string FirstNonEmpty(JObject src, params string[] keys)
        {
            if (src == null) return null;
            foreach (var k in keys)
            {
                var v = src[k];
                if (v == null) continue;
                var s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        private static string ApplyViewerTemplate(string tpl, JObject norm)
        {
            // Template simple: {documentoId} {carpetaId} {ficheroId} {tipo}
            // (sin inventar motor nuevo; es solo reemplazo directo)
            string s = tpl ?? "";
            foreach (var k in new[] { "documentoId", "carpetaId", "ficheroId", "tipo" })
            {
                var v = norm.Value<string>(k) ?? "";
                s = s.Replace("{" + k + "}", Uri.EscapeDataString(v));
            }
            return s;
        }

        private static void ExpandStringsRecursive(ContextoEjecucion ctx, JToken token)
        {
            if (token == null) return;

            if (token.Type == JTokenType.String)
            {
                var s = token.Value<string>();
                var ex = ctx.ExpandString(s);
                ((JValue)token).Value = ex;
                return;
            }

            if (token.Type == JTokenType.Object)
            {
                foreach (var p in ((JObject)token).Properties())
                    ExpandStringsRecursive(ctx, p.Value);
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var it in (JArray)token)
                    ExpandStringsRecursive(ctx, it);
            }
        }

        private static string GetString(Dictionary<string, object> p, string key)
            => p != null && p.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static object GetObj(Dictionary<string, object> p, string key)
            => p != null && p.TryGetValue(key, out var v) ? v : null;

        private static bool GetBool(Dictionary<string, object> p, string key, bool defVal)
        {
            if (p == null || !p.TryGetValue(key, out var v) || v == null) return defVal;
            if (v is bool b) return b;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant();
            if (s == "1" || s == "true" || s == "yes" || s == "si" || s == "sí" || s == "y") return true;
            if (s == "0" || s == "false" || s == "no" || s == "n") return false;
            return defVal;
        }
    }
}
