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
    /// Nota:
    /// - Para keys con puntos (ej: "biz.oc.numero") usa ContextoEjecucion.SetPath(...) y arma diccionarios anidados.
    /// - No tira error duro: si set/remove vienen vacíos, continúa.
    /// </summary>
    public class HStateVars : IManejadorNodo
    {
        public string TipoNodo => "state.vars";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // ---- SET ----
            var setObj = GetObject(p, "set");
            var setDict = CoerceToDict(setObj);

            if (setDict.Count > 0)
            {
                foreach (var kv in setDict)
                {
                    ct.ThrowIfCancellationRequested();

                    var key = (kv.Key ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    object value = NormalizeValue(kv.Value);

                    // Expand string templates ${...}
                    if (value is string s)
                        value = ctx.ExpandString(s);

                    // Set path (soporta "a.b.c")
                    ContextoEjecucion.SetPath(ctx.Estado, key, value);
                }

                ctx.Log($"[state.vars] SET {setDict.Count} variable(s).");
            }
            else
            {
                ctx.Log("[state.vars] SET vacío.");
            }

            // ---- REMOVE ----
            var remObj = GetObject(p, "remove");
            var removeList = CoerceToStringList(remObj);

            if (removeList.Count > 0)
            {
                int removed = 0;

                foreach (var k in removeList)
                {
                    ct.ThrowIfCancellationRequested();

                    var key = (k ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    // remover solo key exacta (no borra sub-árbol completo de paths)
                    if (ctx.Estado.Remove(key))
                        removed++;
                }

                ctx.Log($"[state.vars] REMOVE {removed}/{removeList.Count} key(s).");
            }
            else
            {
                ctx.Log("[state.vars] REMOVE vacío.");
            }

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

            // JObject
            if (v is JObject jo)
            {
                foreach (var prop in jo.Properties())
                    r[prop.Name] = prop.Value;
                return r;
            }

            // IDictionary<string, object>
            if (v is IDictionary<string, object> dso)
            {
                foreach (var kv in dso)
                    r[kv.Key] = kv.Value;
                return r;
            }

            // string JSON
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

            // fallback single
            var single = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(single)) list.Add(single.Trim());
            return list;
        }

        private static object NormalizeValue(object v)
        {
            if (v == null) return null;

            // JValue unwrap
            if (v is JValue jv) return jv.Value;

            // JObject/JArray los dejamos como JToken (persistencia ya lo serializa)
            if (v is JObject || v is JArray) return v;

            return v;
        }
    }
}
