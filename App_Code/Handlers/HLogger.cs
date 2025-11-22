using Newtonsoft.Json.Linq;  // <- ÚNICA dependencia de JSON
using System;
using System.Collections;
using System.Collections.Generic;
//using System.IdentityModel.Protocols.WSTrust;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    // Handler para "util.logger": loguea interpolando ${...} con valores del estado (ej: payload.nombre)
    public class HLogger : IManejadorNodo
    {
        public string TipoNodo => "util.logger";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var levelTpl = GetString(p, "level") ?? "Info";
            var msgTpl = GetString(p, "message") ?? string.Empty;

            // Usá la misma expansión que el resto del motor (simple y consistente)
            var level = ctx.ExpandString(levelTpl);
            var msg = ctx.ExpandString(msgTpl);

            // DEBUG (quitá estas dos líneas cuando verifiques)
            ctx.Log("[Logger DEBUG] raw=" + msgTpl);
            ctx.Log("[Logger DEBUG] expanded=" + msg);

            ctx.Log($"[Logger NUEVO] [{level}] {msg}");
            

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }


        static string GetString(Dictionary<string, object> p, string key)
            => p.TryGetValue(key, out var v) ? Convert.ToString(v) : null;

        static readonly Regex Rx = new Regex(@"\$\{([^}]+)\}", RegexOptions.Compiled);

        static string Interpolate(string s, IDictionary<string, object> state)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Rx.Replace(s, m =>
            {
                var path = m.Groups[1].Value.Trim();
                return TryResolve(state, path, out var val) ? Convert.ToString(Unwrap(val)) : m.Value;
            });
        }

        // ---- Resolución de rutas tipo a.b.c, con índices: arr[0] o arr.0 ----
        static bool TryResolve(object root, string path, out object value)
        {
            value = null;
            if (root == null) return false;

            var parts = path.Split('.');
            object cursor = root;

            foreach (var part in parts)
            {
                var (name, hasIndex, index) = ParseSegment(part);

                if (!TryStep(ref cursor, name))
                    return false;

                if (hasIndex && !TryIndex(ref cursor, index))
                    return false;
            }

            value = cursor;
            return true;
        }

        static (string name, bool hasIndex, int index) ParseSegment(string seg)
        {
            var name = seg;
            var hasIndex = false;
            var index = -1;

            var brk = seg.IndexOf('[');
            if (brk >= 0 && seg.EndsWith("]"))
            {
                name = seg.Substring(0, brk);
                var num = seg.Substring(brk + 1, seg.Length - brk - 2);
                if (int.TryParse(num, out var i)) { hasIndex = true; index = i; }
            }
            else if (int.TryParse(seg, out var j)) // permite ".0" como índice
            {
                name = string.Empty; hasIndex = true; index = j;
            }

            return (name, hasIndex, index);
        }

        static bool TryStep(ref object cursor, string name)
        {
            if (cursor == null) return false;
            if (string.IsNullOrEmpty(name)) return true; // segmento era índice directo

            // IDictionary<string, object>
            if (cursor is IDictionary<string, object> dicObj)
            {
                if (dicObj.TryGetValue(name, out var next)) { cursor = next; return true; }
                foreach (var kv in dicObj)
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    { cursor = kv.Value; return true; }
                return false;
            }

            // IDictionary no genérico
            if (cursor is IDictionary dic)
            {
                foreach (DictionaryEntry de in dic)
                    if (de.Key != null && string.Equals(de.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    { cursor = de.Value; return true; }
                return false;
            }

            // Newtonsoft JObject/JToken
            if (cursor is JToken tok)
            {
                if (tok is JObject jo)
                {
                    var child = jo.GetValue(name, StringComparison.OrdinalIgnoreCase);
                    if (child != null) { cursor = child; return true; }
                    return false;
                }
                // JValue no tiene pasos siguientes
                return false;
            }

            // Reflexión (POCOs)
            var prop = cursor.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                cursor = prop.GetValue(cursor);
                return true;
            }

            return false;
        }

        static bool TryIndex(ref object cursor, int index)
        {
            if (cursor == null) return false;

            // IList / arrays
            if (cursor is IList list)
            {
                if (index >= 0 && index < list.Count) { cursor = list[index]; return true; }
                return false;
            }

            // JArray
            if (cursor is JArray ja)
            {
                if (index >= 0 && index < ja.Count) { cursor = ja[index]; return true; }
                return false;
            }

            return false;
        }

        static object Unwrap(object v)
        {
            if (v is JValue jv) return jv.Value;
            return v;
        }
    }
}
