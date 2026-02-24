using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms.App_Code.Handlers
{
    public class TemplateUtil
    {
        // ==== Utilidad de templating(expandir ${ path} contra ctx.Estado) ====

        private static readonly System.Text.RegularExpressions.Regex _rx =
                    new System.Text.RegularExpressions.Regex(@"\$\{([^}]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string Expand(ContextoEjecucion ctx, string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            return _rx.Replace(s, m =>
            {
                var path = (m.Groups[1].Value ?? "").Trim();
                if (path.Length == 0) return "";

                object val = null;

                // ✅ 1) Primero: búsqueda directa por KEY EXACTA en el Estado.
                // Esto soporta claves planas como: "wf.docTipoCodigo" o "biz.np2.itemsCount"
                // (que es exactamente como doc.load las guarda).
                if (ctx != null && ctx.Estado != null)
                {
                    if (ctx.Estado.TryGetValue(path, out val))
                        return val == null ? "" : Convert.ToString(val);
                }

                // ✅ 2) Fallback: resolver por path (para payload.*, input.*, etc.)
                val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                return val == null ? "" : Convert.ToString(val);
            });
        }

        public static Dictionary<string, object> ExpandDictionary(ContextoEjecucion ctx, IDictionary<string, object> src)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (src == null) return dict;
            foreach (var kv in src)
            {
                var v = kv.Value;
                if (v is string) dict[kv.Key] = Expand(ctx, (string)v);
                else if (v is Newtonsoft.Json.Linq.JValue) dict[kv.Key] = Expand(ctx, System.Convert.ToString(((Newtonsoft.Json.Linq.JValue)v).Value));
                else dict[kv.Key] = v;
            }
            return dict;
        }
    }
}
