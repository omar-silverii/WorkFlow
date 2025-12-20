// App_Code/HIf.cs
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
            string expr = null;
            if (nodo.Parameters != null && nodo.Parameters.TryGetValue("expression", out var e))
                expr = Convert.ToString(e);

            bool ok = Evaluar(expr, ctx);
            ctx.Log("[If] " + (expr ?? "(sin expresión)") + " => " + (ok ? "True" : "False"));
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = ok ? "true" : "false" });
        }

        internal static bool Evaluar(string expr, ContextoEjecucion ctx)
        {
            if (ctx == null) return false;
            if (string.IsNullOrWhiteSpace(expr)) return false;

            var t = expr.Trim();

            // literales
            if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;

            // 1) comparación: ${path} OP rhs
            // OP: == != >= <= > <
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

                // booleans
                if (TryToBool(left, out var lb) && TryToBool(right, out var rb))
                {
                    if (op == "==") return lb == rb;
                    if (op == "!=") return lb != rb;
                    return false; // no tiene sentido > < con bool
                }

                // números (ARG / Invariant)
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
                    default:
                        // Para strings no hacemos >/< (evita sorpresas)
                        return false;
                }
            }

            // 2) truthy: ${path}
            var m2 = Regex.Match(t, @"^\s*\$\{(?<path>[^}]+)\}\s*$");
            if (m2.Success)
            {
                var path = m2.Groups["path"].Value.Trim();
                var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
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

            return rhsRaw;
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

            // 3) último recurso: si trae miles con punto y decimal con coma
            //    reemplazo simple: quito puntos, cambio coma por punto
            var s2 = s.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(s2, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return true;

            return false;
        }
    }
}
