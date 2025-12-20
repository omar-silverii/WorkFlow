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
                var path = m.Groups[1].Value.Trim();
                var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                return val == null ? "" : System.Convert.ToString(val);
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
