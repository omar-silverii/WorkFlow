// App_Code/Handlers/HIf.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
            string field = null, op = null, value = null;

            if (p != null)
            {
                if (p.TryGetValue("field", out var vf)) field = Convert.ToString(vf);
                if (p.TryGetValue("op", out var vo)) op = Convert.ToString(vo);
                if (p.TryGetValue("value", out var vv)) value = Convert.ToString(vv);
            }

            // Modo avanzado (legacy): "expression"
            string expr = null;
            if (p != null && p.TryGetValue("expression", out var ve))
                expr = Convert.ToString(ve);

            bool ok;
            string logText;

            // Si hay simple (field/op), preferimos SIMPLE
            if (!string.IsNullOrWhiteSpace(field) || !string.IsNullOrWhiteSpace(op))
            {
                ok = EvaluarSimple(field, op, value, ctx, out logText);
                ctx.Log("[If] " + logText + " => " + (ok ? "True" : "False"));
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
            }

            ok = Evaluar(expr, ctx, out logText);
            ctx.Log("[If] " + logText + " => " + (ok ? "True" : "False"));
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
        }

        internal static bool EvaluarSimple(string field, string op, string value, ContextoEjecucion ctx, out string logExpr)
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

            // booleans
            if (TryToBool(left, out var lb) && TryToBool(right, out var rb))
            {
                if (opNorm == "==" || opNorm == "eq") return lb == rb;
                if (opNorm == "!=" || opNorm == "neq") return lb != rb;
                return false;
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

            // strings
            var ls = Convert.ToString(left) ?? "";
            var rs = Convert.ToString(right) ?? "";

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

                // booleans
                if (TryToBool(left, out var lb) && TryToBool(right, out var rb))
                {
                    if (op == "==") return lb == rb;
                    if (op == "!=") return lb != rb;
                    return false;
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

        private static bool TryToDecimal(object o, out decimal d)
        {
            d = 0m;
            if (o == null) return false;

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
