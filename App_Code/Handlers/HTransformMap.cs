using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// transform.map
    /// Construye un objeto destino en base a un "map" declarativo.
    ///
    /// Parameters:
    ///  - output: string (default "payload")  // dónde guardar el objeto resultante (path)
    ///  - map: object { "destKey": valueOrTemplate, ... }
    ///       - si value es string, se expande con ctx.ExpandString (soporta ${...})
    ///       - soporta keys con punto para crear paths anidados (ej: "sql.params.Numero")
    ///  - overwrite: bool (default true) // si false y ya existe output, merge superficial (solo top-level)
    ///
    /// Salidas:
    ///  - always
    ///  - error (si map inválido)
    /// </summary>
    public class HTransformMap : IManejadorNodo
    {
        public string TipoNodo => "transform.map";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var output = LeerString(p, "output");
            if (string.IsNullOrWhiteSpace(output)) output = "payload";

            var overwrite = LeerBool(p, "overwrite", defaultValue: true);

            var mapObj = LeerObj(p, "map");
            var map = CoerceToDict(mapObj);
            if (map.Count == 0)
            {
                ctx.Log("[transform.map] map vacío o inválido.");
                // Lo tratamos como error funcional para que puedas rutearlo al util.error si querés
                ContextoEjecucion.SetPath(ctx.Estado, "wf.error", true);
                ContextoEjecucion.SetPath(ctx.Estado, "wf.error.message", "transform.map: map vacío o inválido");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            // Construimos objeto destino (Dictionary anidado via SetPath)
            IDictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in map)
            {
                ct.ThrowIfCancellationRequested();

                var destKey = (kv.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(destKey)) continue;

                object value = NormalizeValue(kv.Value);

                if (value is string s)
                    value = ctx.ExpandString(s);

                ContextoEjecucion.SetPath(result, destKey, value);
            }

            // Persistimos en output
            if (overwrite)
            {
                ContextoEjecucion.SetPath(ctx.Estado, output, result);
                ctx.Log($"[transform.map] OK output='{output}' keys={map.Count} overwrite=true");
            }
            else
            {
                // merge superficial: si ya hay dict en output, lo mezclamos top-level
                if (TryGetDictAtPath(ctx.Estado, output, out var existing))
                {
                    foreach (var kv in result)
                        existing[kv.Key] = kv.Value;

                    ContextoEjecucion.SetPath(ctx.Estado, output, existing);
                    ctx.Log($"[transform.map] OK output='{output}' keys={map.Count} overwrite=false (merge)");
                }
                else
                {
                    ContextoEjecucion.SetPath(ctx.Estado, output, result);
                    ctx.Log($"[transform.map] OK output='{output}' keys={map.Count} overwrite=false (new)");
                }
            }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        // ---------------- helpers ----------------

        private static string LeerString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool LeerBool(Dictionary<string, object> p, string key, bool defaultValue)
        {
            if (p == null) return defaultValue;
            if (!p.TryGetValue(key, out var v) || v == null) return defaultValue;

            if (v is bool b) return b;

            var s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (bool.TryParse(s, out var bb)) return bb;

            if (int.TryParse(s, out var i)) return i != 0;

            return defaultValue;
        }

        private static object LeerObj(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            return p.TryGetValue(key, out var v) ? v : null;
        }

        private static Dictionary<string, object> CoerceToDict(object v)
        {
            var r = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (v == null) return r;

            if (v is JObject jo)
            {
                foreach (var prop in jo.Properties())
                    r[prop.Name] = prop.Value;
                return r;
            }

            if (v is IDictionary<string, object> dso)
            {
                foreach (var kv in dso)
                    r[kv.Key] = kv.Value;
                return r;
            }

            if (v is string s && !string.IsNullOrWhiteSpace(s))
            {
                s = s.Trim();
                if (s.StartsWith("{") && s.EndsWith("}"))
                {
                    try
                    {
                        var j = JObject.Parse(s);
                        foreach (var prop in j.Properties())
                            r[prop.Name] = prop.Value;
                    }
                    catch { }
                }
                return r;
            }

            return r;
        }

        private static object NormalizeValue(object v)
        {
            if (v == null) return null;

            if (v is JValue jv) return jv.Value;
            if (v is JObject || v is JArray) return v;

            return v;
        }

        private static bool TryGetDictAtPath(IDictionary<string, object> root, string path, out IDictionary<string, object> dict)
        {
            dict = null;
            if (root == null || string.IsNullOrWhiteSpace(path)) return false;

            // Solo soportamos path simple o con puntos donde los nodos intermedios sean dict
            var parts = path.Split('.');
            object cur = root;

            foreach (var partRaw in parts)
            {
                var part = (partRaw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(part)) return false;

                if (!(cur is IDictionary<string, object> d)) return false;

                if (!d.TryGetValue(part, out cur) || cur == null)
                    return false;
            }

            if (cur is IDictionary<string, object> dd)
            {
                dict = dd;
                return true;
            }
            return false;
        }
    }
}
