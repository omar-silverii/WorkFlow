// App_Code/MotorFlujoMinimo.cs
// Motor mínimo para ejecutar el workflow desde JSON.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

// <<< NUEVO para SQL >>>
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.WebForms
{
    // ===== Modelos =====
    public class NodeDef
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class EdgeDef
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Condition { get; set; }
    }

    public class WorkflowDef
    {
        public string StartNodeId { get; set; }
        public Dictionary<string, NodeDef> Nodes { get; set; }
        public List<EdgeDef> Edges { get; set; }
    }

    public class ResultadoEjecucion
    {
        public string Etiqueta { get; set; }
    }

    public interface IManejadorNodo
    {
        string TipoNodo { get; }
        Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct);
    }

    // ===== Contexto =====
    public class ContextoEjecucion
    {
        public Dictionary<string, object> Estado { get; private set; }
        public Action<string> Log { get; set; }
        public HttpClient Http { get; private set; }

        public ContextoEjecucion(Action<string> log = null)
        {
            Estado = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Log = log ?? (_ => { });
            Http = new HttpClient();

            // base para URLs relativas
            try
            {
                var req = System.Web.HttpContext.Current?.Request;
                if (req != null && req.Url != null)
                {
                    var app = req.ApplicationPath ?? "/";
                    if (!app.EndsWith("/")) app += "/";
                    var baseUri = new Uri(req.Url.Scheme + "://" + req.Url.Authority + app);
                    Http.BaseAddress = baseUri;
                }
            }
            catch { }
        }

        public static object ResolverPath(object root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path)) return null;

            var dict = root as IDictionary<string, object>;
            if (dict != null && dict.TryGetValue(path, out var direct)) return direct;

            var parts = path.Split('.');
            object curr = root;

            foreach (var p in parts)
            {
                if (curr == null) return null;

                if (curr is IDictionary<string, object> d)
                {
                    d.TryGetValue(p, out curr);
                }
                else if (curr is Newtonsoft.Json.Linq.JObject jobj)
                {
                    curr = jobj.TryGetValue(p, StringComparison.OrdinalIgnoreCase, out var tok) ? tok : null;
                    if (curr is Newtonsoft.Json.Linq.JValue jv) curr = jv.Value;
                }
                else
                {
                    var prop = curr.GetType().GetProperty(p);
                    curr = prop != null ? prop.GetValue(curr, null) : null;
                }
            }
            return curr;
        }

        public static bool ToBool(object o)
        {
            if (o is bool b) return b;
            if (o is string s)
            {
                if (bool.TryParse(s, out var bs)) return bs;
                if (int.TryParse(s, out var isv)) return isv != 0;
            }
            if (o is int i) return i != 0;
            if (o is long l) return l != 0L;
            if (o is decimal d) return d != 0m;
            return false;
        }
    }

    // ===== Motor =====
    public class MotorFlujo
    {
        readonly IDictionary<string, IManejadorNodo> _handlers;

        public MotorFlujo(IEnumerable<IManejadorNodo> handlers)
        {
            _handlers = handlers.ToDictionary(h => h.TipoNodo, StringComparer.OrdinalIgnoreCase);
        }

        public static void Validar(WorkflowDef wf)
        {
            if (wf == null) throw new ArgumentNullException("wf");
            if (wf.Nodes == null || wf.Nodes.Count == 0) throw new InvalidOperationException("El flujo no contiene nodos");
            if (string.IsNullOrWhiteSpace(wf.StartNodeId) || !wf.Nodes.ContainsKey(wf.StartNodeId))
                throw new InvalidOperationException("Nodo de inicio inválido o inexistente");
        }

        public async Task EjecutarAsync(WorkflowDef wf, Action<string> log, CancellationToken ct = default(CancellationToken))
        {
            Validar(wf);
            var ctx = new ContextoEjecucion(log);
            string actual = wf.StartNodeId;
            int guard = 0;

            while (!string.IsNullOrEmpty(actual) && guard++ < 2000)
            {
                if (!wf.Nodes.TryGetValue(actual, out var nodo))
                    throw new InvalidOperationException("Nodo no encontrado: " + actual);

                if (!_handlers.TryGetValue(nodo.Type, out var manejador))
                    throw new InvalidOperationException("No hay handler para: " + nodo.Type);

                var res = await manejador.EjecutarAsync(ctx, nodo, ct);
                var etiqueta = (res != null && !string.IsNullOrEmpty(res.Etiqueta)) ? res.Etiqueta : "always";

                var salientes = wf.Edges.Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase)).ToList();
                if (salientes.Count == 0) break;

                var siguiente = salientes.FirstOrDefault(e => string.Equals(e.Condition, etiqueta, StringComparison.OrdinalIgnoreCase))
                                ?? salientes.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase))
                                ?? salientes[0];

                actual = siguiente.To;

                if (string.Equals(nodo.Type, "util.end", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
    }

    // ===== Handlers básicos =====

    public class HStart : IManejadorNodo
    {
        public string TipoNodo => "util.start";
        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            ctx.Log("[Start]");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }

    public class HEnd : IManejadorNodo
    {
        public string TipoNodo => "util.end";
        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            ctx.Log("[End]");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }

    public class HLogger : IManejadorNodo
    {
        public string TipoNodo => "util.logger";
        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            object lvl, msg;
            var level = (nodo.Parameters != null && nodo.Parameters.TryGetValue("level", out lvl)) ? Convert.ToString(lvl) : "Info";
            var text = (nodo.Parameters != null && nodo.Parameters.TryGetValue("message", out msg)) ? Convert.ToString(msg) : "";
            ctx.Log("[Logger] [" + level + "] " + text);
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }

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

        static bool Evaluar(string expr, ContextoEjecucion ctx)
        {
            if (string.IsNullOrWhiteSpace(expr)) return false;
            var t = expr.Trim();

            if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;

            var m = Regex.Match(t, @"^\s*\$\{(?<path>[^}]+)\}\s*==\s*(?<rhs>.+?)\s*$");
            if (m.Success)
            {
                var path = m.Groups["path"].Value.Trim();
                var rhs = m.Groups["rhs"].Value.Trim();

                if ((rhs.StartsWith("\"") && rhs.EndsWith("\"")) || (rhs.StartsWith("'") && rhs.EndsWith("'")))
                    rhs = rhs.Substring(1, rhs.Length - 2);

                var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                if (rhs.Equals("true", StringComparison.OrdinalIgnoreCase) || rhs.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return ContextoEjecucion.ToBool(val) == rhs.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (decimal.TryParse(rhs, out var num))
                {
                    decimal left;
                    if (val is decimal d) left = d;
                    else if (val is int i) left = i;
                    else if (val is long l) left = l;
                    else if (val is string s && decimal.TryParse(s, out var ds)) left = ds;
                    else return false;
                    return left == num;
                }

                return string.Equals(Convert.ToString(val) ?? "", rhs, StringComparison.OrdinalIgnoreCase);
            }

            var m2 = Regex.Match(t, @"^\s*\$\{(?<path>[^}]+)\}\s*$");
            if (m2.Success)
            {
                var path = m2.Groups["path"].Value.Trim();
                var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                return ContextoEjecucion.ToBool(val);
            }

            return false;
        }
    }

    // ====== NUEVO: Handler SQL ======
    public class ManejadorSql : IManejadorNodo
    {
        // esto tiene que coincidir con el Type que te da el editor
        public string TipoNodo => "data.sql";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // 1) cadena
            // a) forma "editor":  { "connection": "Server=OMARD\\SQLEXPRESS;Database=Workflow;Trusted_Connection=True;" }
            // b) forma "config":  { "connectionStringName": "DefaultConnection" }
            string cnnString = null;

            if (p.TryGetValue("connectionStringName", out var cnnNameObj))
            {
                var cnnName = Convert.ToString(cnnNameObj);
                cnnString = ConfigurationManager.ConnectionStrings[cnnName].ConnectionString;
            }
            else if (p.TryGetValue("connection", out var cnnDirectObj))
            {
                cnnString = Convert.ToString(cnnDirectObj);
            }
            else
            {
                // por defecto usamos la tuya
                cnnString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            }

            // 2) comando
            string sql = null;
            if (p.TryGetValue("commandText", out var cmdObj))
                sql = Convert.ToString(cmdObj);
            else if (p.TryGetValue("query", out var qObj))
                sql = Convert.ToString(qObj);

            if (string.IsNullOrWhiteSpace(sql))
            {
                ctx.Log("[SQL] sin query/commandText");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }

            // 3) parámetros opcionales
            Dictionary<string, object> sqlParams = null;
            if (p.TryGetValue("parameters", out var parObj) && parObj is Newtonsoft.Json.Linq.JObject jobj)
            {
                sqlParams = jobj.ToObject<Dictionary<string, object>>();
            }

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                if (sqlParams != null)
                {
                    foreach (var kv in sqlParams)
                    {
                        cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
                    }
                }

                await cn.OpenAsync(ct);
                var rows = await cmd.ExecuteNonQueryAsync(ct);

                // guardamos en el contexto por si después un IF quiere mirar
                ctx.Estado["sql.rows"] = rows;
                ctx.Log("[SQL] ejecutado. filas=" + rows);
            }

            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }

    // ===== Helper de demo =====
    public static class MotorDemo
    {
        public static WorkflowDef FromJson(string json)
        {
            return JsonConvert.DeserializeObject<WorkflowDef>(json);
        }

        // AHORA devuelve List<IManejadorNodo>
        public static List<IManejadorNodo> CrearHandlersPorDefecto()
        {
            var list = new List<IManejadorNodo>
        {
            new HStart(),
            new HEnd(),
            new HLogger(),
            new HIf(),
            new HDocEntrada(),
            new HHttpRequest(),
        };

            // si TENÉS el handler SQL en Intranet.WorkflowStudio.Runtime
            // lo agregás acá y listo:
            // list.Add(new Intranet.WorkflowStudio.Runtime.ManejadorSql());

            return list;
        }

        public static async Task EjecutarAsync(
            WorkflowDef wf,
            Action<string> log,
            CancellationToken ct = default(CancellationToken))
        {
            var handlers = CrearHandlersPorDefecto();   // ahora es List<...>
            var motor = new MotorFlujo(handlers);       // MotorFlujo acepta IEnumerable
            await motor.EjecutarAsync(wf, log ?? (_ => { }), ct);
        }
    }
}
