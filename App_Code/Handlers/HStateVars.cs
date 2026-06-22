using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// state.vars
    /// Setea y/o elimina variables en ctx.Estado (incluye biz.* si lo seteás con ese prefijo).
    ///
    /// Parameters:
    ///   - set:    object { "key": value, ... }  (value puede ser string con ${...})
    ///   - remove: array|string csv              (ej: ["a","b"] o "a,b")
    ///
    /// fix30:
    /// - mantiene compatibilidad con set/remove existentes.
    /// - evita logs "SET vacío" / "REMOVE vacío" cuando no corresponde.
    /// - remove soporta rutas anidadas creadas con SetPath, por ejemplo biz.aprobacion.monto.
    /// - deja un resumen técnico en state.last.* para ver qué hizo el nodo en DatosContexto.
    ///
    /// fix30c:
    /// - remove también soporta rutas anidadas dentro de objetos JSON guardados como JObject,
    ///   por ejemplo biz.compra.importe cuando biz.compra fue guardado desde JSON avanzado.
    /// </summary>
    public class HStateVars : IManejadorNodo
    {
        public string TipoNodo => "state.vars";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var setObj = GetObject(p, "set");
            var setDict = CoerceToDict(setObj);
            var setKeys = new List<string>();

            foreach (var kv in setDict)
            {
                ct.ThrowIfCancellationRequested();

                var key = (kv.Key ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                object value = NormalizeValue(kv.Value);

                // Expand string templates ${...}
                if (value is string s)
                    value = ctx.ExpandString(s);

                ContextoEjecucion.SetPath(ctx.Estado, key, value);
                setKeys.Add(key);
            }

            var remObj = GetObject(p, "remove");
            var removeList = CoerceToStringList(remObj);
            var removeKeys = new List<string>();
            int removed = 0;

            foreach (var k in removeList)
            {
                ct.ThrowIfCancellationRequested();

                var key = (k ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (RemovePath(ctx.Estado, key))
                    removed++;

                removeKeys.Add(key);
            }

            if (setKeys.Count > 0)
                ctx.Log($"[state.vars] SET {setKeys.Count} variable(s): {string.Join(", ", setKeys)}");

            if (removeKeys.Count > 0)
                ctx.Log($"[state.vars] REMOVE {removed}/{removeKeys.Count} variable(s): {string.Join(", ", removeKeys)}");

            if (setKeys.Count == 0 && removeKeys.Count == 0)
                ctx.Log("[state.vars] Sin cambios.");

            ContextoEjecucion.SetPath(ctx.Estado, "state.last.nodeId", nodo.Id ?? string.Empty);
            ContextoEjecucion.SetPath(ctx.Estado, "state.last.setCount", setKeys.Count);
            ContextoEjecucion.SetPath(ctx.Estado, "state.last.removeCount", removeKeys.Count);
            ContextoEjecucion.SetPath(ctx.Estado, "state.last.removedCount", removed);
            ContextoEjecucion.SetPath(ctx.Estado, "state.last.setKeys", setKeys.ToArray());
            ContextoEjecucion.SetPath(ctx.Estado, "state.last.removeKeys", removeKeys.ToArray());

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        // ----------------- helpers -----------------

        private static object GetObject(Dictionary<string, object> p, string key)
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
                    catch { /* ignore */ }
                }
                return r;
            }

            return r;
        }

        private static List<string> CoerceToStringList(object v)
        {
            var list = new List<string>();
            if (v == null) return list;

            if (v is JArray ja)
            {
                foreach (var x in ja)
                {
                    var s = x?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                return list;
            }

            if (v is object[] arr)
            {
                foreach (var x in arr)
                {
                    var s = x == null ? null : Convert.ToString(x, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                return list;
            }

            if (v is List<object> lo)
            {
                foreach (var x in lo)
                {
                    var s = x == null ? null : Convert.ToString(x, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                return list;
            }

            if (v is string csv && !string.IsNullOrWhiteSpace(csv))
            {
                foreach (var part in csv.Split(','))
                {
                    var s = (part ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
                return list;
            }

            var single = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(single)) list.Add(single.Trim());
            return list;
        }

        private static object NormalizeValue(object v)
        {
            if (v == null) return null;
            if (v is JValue jv) return jv.Value;
            if (v is JObject || v is JArray) return v;
            return v;
        }

        private static bool RemovePath(IDictionary<string, object> root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path)) return false;

            path = path.Trim();

            // Compatibilidad: si existiera como key plana exacta, remover primero.
            if (root.Remove(path))
                return true;

            var parts = path.Split('.');
            if (parts.Length == 0) return false;

            object curr = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var key = (parts[i] ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key)) return false;

                curr = GetChildForRemove(curr, key);
                if (curr == null) return false;
            }

            var last = (parts[parts.Length - 1] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(last)) return false;

            return RemoveChild(curr, last);
        }

        private static object GetChildForRemove(object parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key)) return null;

            if (parent is IDictionary<string, object> dict)
            {
                object value;
                return dict.TryGetValue(key, out value) ? value : null;
            }

            var jo = parent as JObject;
            if (jo != null)
            {
                JToken token;
                return jo.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out token) ? token : null;
            }

            return null;
        }

        private static bool RemoveChild(object parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key)) return false;

            if (parent is IDictionary<string, object> dict)
                return dict.Remove(key);

            var jo = parent as JObject;
            if (jo != null)
            {
                JProperty prop = null;
                foreach (var p in jo.Properties())
                {
                    if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        prop = p;
                        break;
                    }
                }

                if (prop == null) return false;

                prop.Remove();
                return true;
            }

            return false;
        }
    }
}
