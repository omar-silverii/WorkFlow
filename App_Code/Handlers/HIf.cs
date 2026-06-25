// App_Code/Handlers/HIf.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HIf : IManejadorNodo
    {
        public string TipoNodo => "control.if";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo?.Parameters;

            // Modo simple (para usuarios no técnicos):
            //   field: "payload.status"   (sin ${})
            //   op:    == != >= <= > < contains not_contains starts_with ends_with exists not_exists empty not_empty
            //   value: string (puede incluir ${...})
            string field = null, op = null, value = null, transform = null;

            if (p != null)
            {
                if (p.TryGetValue("field", out var vf)) field = Convert.ToString(vf);
                if (p.TryGetValue("op", out var vo)) op = Convert.ToString(vo);
                if (p.TryGetValue("value", out var vv)) value = Convert.ToString(vv);
                if (p.TryGetValue("transform", out var vt)) transform = Convert.ToString(vt);
            }

            // Modo avanzado (legacy): "expression"
            string expr = null;
            if (p != null && p.TryGetValue("expression", out var ve))
                expr = Convert.ToString(ve);

            bool ok;
            string logText;

            // fix40: modo compuesto compatible.
            // Si existen reglas, se evalúan como extensión del IF simple:
            //   rulesMode: all | any
            //   rules: [{ field, op, value, transform }]
            // El modo simple field/op/value y expression siguen funcionando igual.
            if (TryReadCompoundRules(p, out var rules))
            {
                var rulesMode = ReadStringParam(p, "rulesMode");
                ok = EvaluarCompuesto(rules, rulesMode, ctx, out logText);
                ctx.Log("[If] " + logText + " => " + (ok ? "True" : "False"));
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
            }

            // Si hay simple (field/op), preferimos SIMPLE
            if (!string.IsNullOrWhiteSpace(field) || !string.IsNullOrWhiteSpace(op))
            {
                ok = EvaluarSimple(field, op, value, transform, ctx, out logText);
                ctx.Log("[If] " + logText + " => " + (ok ? "True" : "False"));
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
            }

            ok = Evaluar(expr, ctx, out logText);
            ctx.Log("[If] " + logText + " => " + (ok ? "True" : "False"));
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
        }


        private class ReglaIf
        {
            public string Field { get; set; }
            public string Op { get; set; }
            public string Value { get; set; }
            public string Transform { get; set; }
        }

        private static string ReadStringParam(Dictionary<string, object> p, string key)
        {
            if (p == null || string.IsNullOrWhiteSpace(key)) return null;

            if (p.TryGetValue(key, out var direct))
                return Convert.ToString(direct);

            foreach (var kv in p)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToString(kv.Value);
            }

            return null;
        }

        private static bool TryReadCompoundRules(Dictionary<string, object> p, out List<ReglaIf> rules)
        {
            rules = new List<ReglaIf>();
            if (p == null) return false;

            object raw = null;
            if (!p.TryGetValue("rules", out raw))
            {
                foreach (var kv in p)
                {
                    if (string.Equals(kv.Key, "rules", StringComparison.OrdinalIgnoreCase))
                    {
                        raw = kv.Value;
                        break;
                    }
                }
            }

            if (raw == null) return false;

            if (raw is string rawText)
            {
                rawText = rawText.Trim();
                if (string.IsNullOrWhiteSpace(rawText)) return false;

                try
                {
                    raw = JArray.Parse(rawText);
                }
                catch
                {
                    return false;
                }
            }

            if (raw is JArray ja)
            {
                foreach (var item in ja)
                    AddRuleFromObject(rules, item);
            }
            else if (raw is IEnumerable en && !(raw is string))
            {
                foreach (var item in en)
                    AddRuleFromObject(rules, item);
            }

            rules.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Field));
            return rules.Count > 0;
        }

        private static void AddRuleFromObject(List<ReglaIf> rules, object item)
        {
            if (rules == null || item == null) return;

            var r = new ReglaIf();

            if (item is JObject jo)
            {
                r.Field = ReadJObjectString(jo, "field") ?? ReadJObjectString(jo, "fieldPath");
                r.Op = ReadJObjectString(jo, "op") ?? ReadJObjectString(jo, "operator");
                r.Value = ReadJObjectString(jo, "value");
                r.Transform = ReadJObjectString(jo, "transform");
            }
            else if (item is IDictionary<string, object> dic)
            {
                r.Field = ReadDictionaryString(dic, "field") ?? ReadDictionaryString(dic, "fieldPath");
                r.Op = ReadDictionaryString(dic, "op") ?? ReadDictionaryString(dic, "operator");
                r.Value = ReadDictionaryString(dic, "value");
                r.Transform = ReadDictionaryString(dic, "transform");
            }

            if (string.IsNullOrWhiteSpace(r.Op)) r.Op = "not_empty";
            if (!string.IsNullOrWhiteSpace(r.Field)) rules.Add(r);
        }

        private static string ReadJObjectString(JObject jo, string key)
        {
            if (jo == null || string.IsNullOrWhiteSpace(key)) return null;

            if (jo.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var tok))
                return tok == null || tok.Type == JTokenType.Null ? null : (tok is JValue jv ? Convert.ToString(jv.Value) : tok.ToString());

            return null;
        }

        private static string ReadDictionaryString(IDictionary<string, object> dic, string key)
        {
            if (dic == null || string.IsNullOrWhiteSpace(key)) return null;

            foreach (var kv in dic)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToString(kv.Value);
            }

            return null;
        }

        private static bool EvaluarCompuesto(List<ReglaIf> rules, string rulesMode, ContextoEjecucion ctx, out string logExpr)
        {
            var any = IsAnyMode(rulesMode);
            var final = any ? false : true;
            var partes = new List<string>();

            if (rules == null || rules.Count == 0)
            {
                logExpr = "condición compuesta sin reglas";
                return false;
            }

            foreach (var r in rules)
            {
                var ok = EvaluarSimple(r.Field, r.Op, r.Value, r.Transform, ctx, out var reglaLog);
                partes.Add(reglaLog + " => " + (ok ? "True" : "False"));

                if (any) final = final || ok;
                else final = final && ok;
            }

            logExpr = (any ? "ANY" : "ALL") + " [" + string.Join("; ", partes) + "]";
            return final;
        }

        private static bool IsAnyMode(string rulesMode)
        {
            var m = (rulesMode ?? "all").Trim().ToLowerInvariant();
            return m == "any" || m == "or" || m == "cualquiera";
        }

        internal static bool EvaluarSimple(string field, string op, string value, string transform, ContextoEjecucion ctx, out string logExpr)
        {
            logExpr = "(sin condición)";
            if (ctx == null) return false;

            field = (field ?? "").Trim();
            op = (op ?? "").Trim();

            // Normalizamos: permitir que alguien ponga ${payload.status}
            if (field.StartsWith("${") && field.EndsWith("}"))
                field = field.Substring(2, field.Length - 3).Trim();

            if (string.IsNullOrWhiteSpace(field))
                return false;

            if (string.IsNullOrWhiteSpace(op))
                op = "exists";

            var opNorm = (op ?? "").Trim().ToLowerInvariant();

            object left = ContextoEjecucion.ResolverPath(ctx.Estado, field);

            // value puede tener ${...}
            string rhsExpanded = ctx.ExpandString(value ?? "");
            object right = rhsExpanded;

            // si rhs es "${otra.ruta}" lo resolvemos como path real
            var mt = Regex.Match(rhsExpanded ?? "", @"^\s*\$\{(?<path>[^}]+)\}\s*$");
            if (mt.Success)
            {
                var path = mt.Groups["path"].Value.Trim();
                right = ContextoEjecucion.ResolverPath(ctx.Estado, path);
            }

            // Alias usados por la UI: verdadero/falso se evalúan como == true/false.
            if (opNorm == "true" || opNorm == "false")
            {
                right = opNorm == "true";
                rhsExpanded = opNorm;
                opNorm = "==";
            }

            logExpr = $"{field} {op} {(NeedsValue(opNorm) ? (rhsExpanded ?? "") : "")}".Trim();

            // Operadores sin rhs
            if (opNorm == "exists")
                return left != null;

            if (opNorm == "not_exists")
                return left == null;

            if (opNorm == "empty")
                return left == null || string.IsNullOrWhiteSpace(Convert.ToString(left));

            if (opNorm == "not_empty")
                return left != null && !string.IsNullOrWhiteSpace(Convert.ToString(left));

            // booleans (solo para == / !=; para el resto seguir evaluando números)
            if (opNorm == "==" || opNorm == "!=" || opNorm == "eq" || opNorm == "neq")
            {
                if (TryToBool(left, out var lb) && TryToBool(right, out var rb))
                {
                    if (opNorm == "==" || opNorm == "eq") return lb == rb;
                    return lb != rb; // opNorm == "!=" || "neq"
                }
            }

            // números
            if (TryToDecimal(left, out var ld) && TryToDecimal(right, out var rd))
            {
                switch (opNorm)
                {
                    case "==":
                    case "eq": return ld == rd;
                    case "!=":
                    case "neq": return ld != rd;
                    case ">": return ld > rd;
                    case "<": return ld < rd;
                    case ">=": return ld >= rd;
                    case "<=": return ld <= rd;
                }
            }

            // fechas
            if (TryToDateTime(left, out var ldt) && TryToDateTime(right, out var rdt))
            {
                switch (opNorm)
                {
                    case "==":
                    case "eq": return ldt == rdt;
                    case "!=":
                    case "neq": return ldt != rdt;
                    case ">": return ldt > rdt;
                    case "<": return ldt < rdt;
                    case ">=": return ldt >= rdt;
                    case "<=": return ldt <= rdt;
                }
            }

            // strings
            var ls = ApplyTransform(Convert.ToString(left) ?? "", transform);
            var rs = ApplyTransform(Convert.ToString(right) ?? "", transform);

            if (opNorm == "==" || opNorm == "eq")
                return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);

            if (opNorm == "!=" || opNorm == "neq")
                return !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);

            if (opNorm == "contains")
                return ls.IndexOf(rs, StringComparison.OrdinalIgnoreCase) >= 0;

            if (opNorm == "not_contains")
                return ls.IndexOf(rs, StringComparison.OrdinalIgnoreCase) < 0;

            if (opNorm == "starts_with")
                return ls.StartsWith(rs, StringComparison.OrdinalIgnoreCase);

            if (opNorm == "ends_with")
                return ls.EndsWith(rs, StringComparison.OrdinalIgnoreCase);

            // fallback: no soportado
            return false;
        }
        private static string ApplyTransform(string s, string transform)
        {
            s = s ?? "";
            var t = (transform ?? "").Trim().ToLowerInvariant();

            switch (t)
            {
                case "trim":
                    return s.Trim();

                case "lower":
                    return s.ToLowerInvariant();

                case "upper":
                    return s.ToUpperInvariant();

                case "none":
                case "":
                default:
                    return s;
            }
        }

        private static bool NeedsValue(string opNorm)
        {
            if (string.IsNullOrWhiteSpace(opNorm)) return true;
            return !(opNorm == "exists" || opNorm == "not_exists" || opNorm == "empty" || opNorm == "not_empty");
        }

        internal static bool Evaluar(string expr, ContextoEjecucion ctx, out string logExpr)
        {
            logExpr = expr ?? "(sin expresión)";
            if (ctx == null) return false;
            if (string.IsNullOrWhiteSpace(expr)) return false;

            var t = expr.Trim();

            // literales
            if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) { logExpr = "true"; return true; }
            if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) { logExpr = "false"; return false; }

            // ====== LEGACY FUNCIONES (compat)
            // contains(lower(${path}), 'x') / contains(${path}, 'x')
            var mContainsLower = Regex.Match(t,
                @"^\s*contains\s*\(\s*lower\s*\(\s*\$\{(?<path>[^}]+)\}\s*\)\s*,\s*(?<q>['""])(?<val>.*?)\k<q>\s*\)\s*$",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (mContainsLower.Success)
            {
                var path = mContainsLower.Groups["path"].Value.Trim();
                var val = mContainsLower.Groups["val"].Value ?? "";
                var left = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, path)) ?? "";
                logExpr = $"contains(lower(${{{path}}}), '{val}')";
                return left.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            var mContains = Regex.Match(t,
                @"^\s*contains\s*\(\s*\$\{(?<path>[^}]+)\}\s*,\s*(?<q>['""])(?<val>.*?)\k<q>\s*\)\s*$",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (mContains.Success)
            {
                var path = mContains.Groups["path"].Value.Trim();
                var val = mContains.Groups["val"].Value ?? "";
                var left = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, path)) ?? "";
                logExpr = $"contains(${{{path}}}, '{val}')";
                return left.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // startsWith(${path}, 'x')
            var mStarts = Regex.Match(t,
                @"^\s*startsWith\s*\(\s*\$\{(?<path>[^}]+)\}\s*,\s*(?<q>['""])(?<val>.*?)\k<q>\s*\)\s*$",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (mStarts.Success)
            {
                var path = mStarts.Groups["path"].Value.Trim();
                var val = mStarts.Groups["val"].Value ?? "";
                var left = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, path)) ?? "";
                logExpr = $"startsWith(${{{path}}}, '{val}')";
                return left.StartsWith(val, StringComparison.OrdinalIgnoreCase);
            }

            // endsWith(${path}, 'x')
            var mEnds = Regex.Match(t,
                @"^\s*endsWith\s*\(\s*\$\{(?<path>[^}]+)\}\s*,\s*(?<q>['""])(?<val>.*?)\k<q>\s*\)\s*$",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (mEnds.Success)
            {
                var path = mEnds.Groups["path"].Value.Trim();
                var val = mEnds.Groups["val"].Value ?? "";
                var left = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, path)) ?? "";
                logExpr = $"endsWith(${{{path}}}, '{val}')";
                return left.EndsWith(val, StringComparison.OrdinalIgnoreCase);
            }

            // exists(${path}) / not_exists(${path})
            var mExists = Regex.Match(t, @"^\s*exists\s*\(\s*\$\{(?<path>[^}]+)\}\s*\)\s*$", RegexOptions.IgnoreCase);
            if (mExists.Success)
            {
                var path = mExists.Groups["path"].Value.Trim();
                logExpr = $"exists(${{{path}}})";
                return ContextoEjecucion.ResolverPath(ctx.Estado, path) != null;
            }

            var mNotExists = Regex.Match(t, @"^\s*not_exists\s*\(\s*\$\{(?<path>[^}]+)\}\s*\)\s*$", RegexOptions.IgnoreCase);
            if (mNotExists.Success)
            {
                var path = mNotExists.Groups["path"].Value.Trim();
                logExpr = $"not_exists(${{{path}}})";
                return ContextoEjecucion.ResolverPath(ctx.Estado, path) == null;
            }

            // empty(${path}) / not_empty(${path})
            var mEmpty = Regex.Match(t, @"^\s*empty\s*\(\s*\$\{(?<path>[^}]+)\}\s*\)\s*$", RegexOptions.IgnoreCase);
            if (mEmpty.Success)
            {
                var path = mEmpty.Groups["path"].Value.Trim();
                var v = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                logExpr = $"empty(${{{path}}})";
                return v == null || string.IsNullOrWhiteSpace(Convert.ToString(v));
            }

            var mNotEmpty = Regex.Match(t, @"^\s*not_empty\s*\(\s*\$\{(?<path>[^}]+)\}\s*\)\s*$", RegexOptions.IgnoreCase);
            if (mNotEmpty.Success)
            {
                var path = mNotEmpty.Groups["path"].Value.Trim();
                var v = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                logExpr = $"not_empty(${{{path}}})";
                return v != null && !string.IsNullOrWhiteSpace(Convert.ToString(v));
            }

            // ====== Comparación: ${path} OP rhs
            var m = Regex.Match(t,
                @"^\s*\$\{(?<lhs>[^}]+)\}\s*(?<op>==|!=|>=|<=|>|<)\s*(?<rhs>.+?)\s*$",
                RegexOptions.Singleline);

            if (m.Success)
            {
                var lhsPath = m.Groups["lhs"].Value.Trim();
                var op = m.Groups["op"].Value.Trim();
                var rhsRaw = m.Groups["rhs"].Value.Trim();

                object left = ContextoEjecucion.ResolverPath(ctx.Estado, lhsPath);
                object right = ResolverRhs(rhsRaw, ctx);

                logExpr = "${" + lhsPath + "} " + op + " " + rhsRaw;

                // booleans (solo para == / !=; para el resto seguir evaluando números)
                if (op == "==" || op == "!=")
                {
                    if (TryToBool(left, out var lb) && TryToBool(right, out var rb))
                    {
                        if (op == "==") return lb == rb;
                        return lb != rb; // op == "!="
                    }
                }

                // números
                if (TryToDecimal(left, out var ld) && TryToDecimal(right, out var rd))
                {
                    switch (op)
                    {
                        case "==": return ld == rd;
                        case "!=": return ld != rd;
                        case ">": return ld > rd;
                        case "<": return ld < rd;
                        case ">=": return ld >= rd;
                        case "<=": return ld <= rd;
                    }
                }

                // fallback: strings
                var ls = Convert.ToString(left) ?? "";
                var rs = Convert.ToString(right) ?? "";

                switch (op)
                {
                    case "==": return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
                    case "!=": return !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
                    default: return false;
                }
            }

            // Truthy: ${path}
            var m2 = Regex.Match(t, @"^\s*\$\{(?<path>[^}]+)\}\s*$");
            if (m2.Success)
            {
                var path = m2.Groups["path"].Value.Trim();
                var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                logExpr = "${" + path + "}";
                return ContextoEjecucion.ToBool(val);
            }

            return false;
        }

        private static object ResolverRhs(string rhsRaw, ContextoEjecucion ctx)
        {
            // si rhs es ${otra.ruta}
            var mt = Regex.Match(rhsRaw, @"^\s*\$\{(?<path>[^}]+)\}\s*$");
            if (mt.Success)
            {
                var path = mt.Groups["path"].Value.Trim();
                return ContextoEjecucion.ResolverPath(ctx.Estado, path);
            }

            // si viene entre comillas
            if ((rhsRaw.StartsWith("\"") && rhsRaw.EndsWith("\"")) || (rhsRaw.StartsWith("'") && rhsRaw.EndsWith("'")))
                rhsRaw = rhsRaw.Substring(1, rhsRaw.Length - 2);

            // permite templates en rhs
            return ctx.ExpandString(rhsRaw);
        }

        private static bool TryToBool(object o, out bool b)
        {
            b = false;
            if (o == null) return false;

            if (o is bool bb) { b = bb; return true; }

            var s = Convert.ToString(o)?.Trim();
            if (string.IsNullOrWhiteSpace(s)) return false;

            if (bool.TryParse(s, out var rb)) { b = rb; return true; }
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { b = i != 0; return true; }

            if (string.Equals(s, "S", StringComparison.OrdinalIgnoreCase)) { b = true; return true; }
            if (string.Equals(s, "N", StringComparison.OrdinalIgnoreCase)) { b = false; return true; }

            return false;
        }


        private static bool TryToDateTime(object o, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (o == null) return false;

            if (o is JValue jv)
                o = jv.Value;

            if (o is DateTime dtt) { dt = dtt; return true; }

            var s = Convert.ToString(o);
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out dt))
                return true;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return true;

            return false;
        }

        private static bool TryToDecimal(object o, out decimal d)
        {
            d = 0m;
            if (o == null) return false;

            // Desenvuelve JValue (Newtonsoft) si viene del JSON
            if (o is JValue jv)
                o = jv.Value;

            if (o is decimal dd) { d = dd; return true; }
            if (o is int i) { d = i; return true; }
            if (o is long l) { d = l; return true; }
            if (o is double dbl) { d = (decimal)dbl; return true; }
            if (o is float fl) { d = (decimal)fl; return true; }

            var s = Convert.ToString(o);
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim();

            // limpia símbolos (ej: "$ 154.000,00")
            s = Regex.Replace(s, @"[^\d\.,\-]", "");

            // 1) es-AR (154.000,00)
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out d))
                return true;

            // 2) invariant (154000.00)
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return true;

            // 3) último recurso: quito puntos, cambio coma por punto
            var s2 = s.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(s2, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return true;

            return false;
        }


    }
}
