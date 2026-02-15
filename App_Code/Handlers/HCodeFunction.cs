// App_Code/Handlers/HCodeFunction.cs
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// code.function
    /// Ejecuta una función registrada (whitelist) del lado servidor.
    ///
    /// Parameters:
    ///   - name   : string   (ej: "string.concat", "math.sum", "json.pick")
    ///   - args   : object   (se expanden strings con ${...})
    ///   - output : string   (default: "biz.code.function")
    /// </summary>
    public class HCodeFunction : IManejadorNodo
    {
        public string TipoNodo => "code.function";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string name = (ctx.ExpandString(GetString(p, "name")) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("code.function: falta parámetro 'name'.");

            string output = (ctx.ExpandString(GetString(p, "output")) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(output)) output = "biz.code.function";

            JObject args = GetArgsAsJObject(p, "args", ctx);

            if (!CodeFunctionRegistry.TryInvoke(name, ctx, args, out JToken result, out string error))
                throw new InvalidOperationException("code.function: " + error);

            // Guardar en Estado usando TU SetPath (static)
            object clr = ToClr(result);
            ContextoEjecucion.SetPath(ctx.Estado, output, clr);

            ctx.Log($"[code.function] OK name='{name}' output='{output}'");

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "ok" });
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static JObject GetArgsAsJObject(IDictionary<string, object> p, string key, ContextoEjecucion ctx)
        {
            if (p == null) return new JObject();
            if (!p.TryGetValue(key, out var v) || v == null) return new JObject();

            try
            {
                JObject jo;

                if (v is JObject j1) jo = (JObject)j1.DeepClone();
                else if (v is IDictionary<string, object> dict) jo = JObject.FromObject(dict);
                else if (v is string s)
                {
                    s = ctx.ExpandString(s);
                    var tok = JToken.Parse(s);
                    jo = tok as JObject ?? new JObject { ["value"] = tok };
                }
                else
                {
                    jo = JObject.FromObject(v);
                }

                ExpandAllStrings(jo, ctx);
                return jo;
            }
            catch
            {
                return new JObject();
            }
        }

        private static void ExpandAllStrings(JToken tok, ContextoEjecucion ctx)
        {
            if (tok == null) return;

            if (tok is JValue jv && jv.Type == JTokenType.String)
            {
                jv.Value = ctx.ExpandString(Convert.ToString(jv.Value));
                return;
            }

            if (tok is JObject jo)
            {
                foreach (var prop in jo.Properties())
                    ExpandAllStrings(prop.Value, ctx);
            }
            else if (tok is JArray arr)
            {
                foreach (var it in arr)
                    ExpandAllStrings(it, ctx);
            }
        }

        private static object ToClr(JToken tok)
        {
            if (tok == null) return null;

            if (tok is JValue v) return v.Value;

            // Para objetos/arrays guardamos como objeto CLR (Dictionary/List)
            try { return tok.ToObject<object>(); }
            catch { return tok.ToString(Newtonsoft.Json.Formatting.None); }
        }
    }
}
