// App_Code/MotorFlujoMinimo.cs
// Motor mínimo para ejecutar el workflow desde JSON.

using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>
        /// Si es true, el motor se detiene en el nodo actual y NO busca arista de salida.
        /// Útil para nodos asincrónicos como human.task.
        /// </summary>
        public bool Detener { get; set; }
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

            // Importar seed si lo dejó el server
            try
            {
                var items = System.Web.HttpContext.Current?.Items;
                if (items != null && items["WF_SEED"] is IDictionary<string, object> dic)
                {
                    foreach (var kv in dic)
                        Estado[kv.Key] = kv.Value;
                }
            }
            catch { }

            // Base para URLs relativas
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

        private static readonly Regex _tplRegex = new Regex(@"\$\{(?<path>[^}]+)\}", RegexOptions.Compiled);

        public string ExpandString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            return _tplRegex.Replace(s, m =>
            {
                var path = m.Groups["path"].Value.Trim();
                var val = ResolverPath(Estado, path);
                return val == null ? "" : Convert.ToString(val, CultureInfo.InvariantCulture);
            });
        }

        /// <summary>
        /// Setea en Estado una ruta con puntos: ejemplo "payload.polizaId" = 123.
        /// Crea diccionarios intermedios si no existen.
        /// </summary>
        public static void SetPath(IDictionary<string, object> root, string path, object value)
        {
            if (root == null || string.IsNullOrWhiteSpace(path)) return;

            var parts = path.Split('.');
            IDictionary<string, object> curr = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var key = parts[i];
                object next;

                if (!curr.TryGetValue(key, out next) || !(next is IDictionary<string, object>))
                {
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    curr[key] = dict;
                    curr = dict;
                }
                else
                {
                    curr = (IDictionary<string, object>)next;
                }
            }

            curr[parts[parts.Length - 1]] = value;
        }
    }

    // ===== Motor =====
    public class MotorFlujo
    {
        private readonly IDictionary<string, IManejadorNodo> _handlers;
        private readonly IEstadoPublisher _estadoPublisher;

        public MotorFlujo(IEnumerable<IManejadorNodo> handlers, IEstadoPublisher estadoPublisher = null)
        {
            var dict = new Dictionary<string, IManejadorNodo>(StringComparer.OrdinalIgnoreCase);

            if (handlers != null)
            {
                foreach (var h in handlers)
                {
                    if (h == null) continue;

                    var key = h.TipoNodo;
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    // Si ya había un handler con esa clave, lo reemplazamos por este
                    dict[key] = h;
                }
            }

            _handlers = dict;
            _estadoPublisher = estadoPublisher; // puede ser null
        }

        public static void Validar(WorkflowDef wf)
        {
            if (wf == null) throw new ArgumentNullException("wf");
            if (wf.Nodes == null || wf.Nodes.Count == 0)
                throw new InvalidOperationException("El flujo no contiene nodos");

            if (string.IsNullOrWhiteSpace(wf.StartNodeId) || !wf.Nodes.ContainsKey(wf.StartNodeId))
                throw new InvalidOperationException("Nodo de inicio inválido o inexistente");
        }

        public async Task EjecutarAsync(
    WorkflowDef wf,
    Action<string> log,
    CancellationToken ct = default(CancellationToken))
        {
            Validar(wf);

            var ctx = new ContextoEjecucion(log);

            // ===================== start override (resume) =====================
            string actual = null;
            bool resumeMode = false;

            if (ctx.Estado != null &&
                ctx.Estado.TryGetValue("wf.startNodeIdOverride", out var ov) &&
                ov != null)
            {
                var ovId = Convert.ToString(ov);
                if (!string.IsNullOrWhiteSpace(ovId) && wf.Nodes.ContainsKey(ovId))
                {
                    actual = ovId;
                    resumeMode = true;

                    // Log solo si es una reanudación real (tenemos el nodo de tarea)
                    if (ctx.Estado.TryGetValue("wf.resume.taskNodeId", out var tnode) && tnode != null)
                    {
                        // Tomamos el tareaId desde el estado (ya lo estás guardando como tarea.id / wf.tarea.id)
                        object tidObj = null;

                        if (!ctx.Estado.TryGetValue("wf.tarea.id", out tidObj) || tidObj == null)
                            ctx.Estado.TryGetValue("tarea.id", out tidObj);

                        var tidStr = (tidObj == null) ? "?" : Convert.ToString(tidObj);

                        ctx.Log($"[Motor] reanudando desde tareaId={tidStr}, nodo={Convert.ToString(tnode)} → arrancando en nodo {actual}");
                        ctx.Log($"[Motor] start override: arrancando en nodo {actual}");
                    }

                }
            }

            if (string.IsNullOrWhiteSpace(actual))
                actual = wf.StartNodeId;

            // ✅ NUEVO: si estamos reanudando, nunca ejecutar util.start
            // (por seguridad, aunque alguien lo haya puesto como override)
            if (resumeMode &&
                wf.Nodes.TryGetValue(actual, out var n0) &&
                n0 != null &&
                n0.Type != null &&
                n0.Type.Equals("util.start", StringComparison.OrdinalIgnoreCase))
            {
                // buscamos la salida "always" desde start y saltamos
                var salStart = (wf.Edges ?? new List<EdgeDef>())
                    .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var next =
                    salStart.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                    salStart.FirstOrDefault();

                if (next != null && !string.IsNullOrWhiteSpace(next.To) && wf.Nodes.ContainsKey(next.To))
                {
                    ctx.Log($"[Motor] resume: se omite util.start y se continúa en {next.To}");
                    actual = next.To;
                }
            }
            // ================================================================

            int guard = 0;

            // Publico estado inicial (por si algún nodo consulta WF_CTX_ESTADO)
            _estadoPublisher?.Publish(ctx.Estado, null, null);

            while (!string.IsNullOrEmpty(actual) && guard++ < 2000)
            {
                if (!wf.Nodes.TryGetValue(actual, out var nodo))
                    throw new InvalidOperationException("Nodo no encontrado: " + actual);

                if (!_handlers.TryGetValue(nodo.Type, out var manejador))
                    throw new InvalidOperationException("No hay handler para: " + nodo.Type);

                var res = await manejador.EjecutarAsync(ctx, nodo, ct);

                // Publicar estado luego de ejecutar cada nodo
                _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);

                // Si algún nodo pidió detener (wf.detener = true), corto
                if (ctx.Estado != null &&
                    ctx.Estado.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                {
                    ctx.Log($"[Motor] ejecución detenida en nodo {nodo.Id} ({nodo.Type})");
                    break;
                }

                var etiqueta = (res != null && !string.IsNullOrEmpty(res.Etiqueta))
                    ? res.Etiqueta
                    : "always";

                var salientes = (wf.Edges ?? new List<EdgeDef>())
                    .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (salientes.Count == 0) break;

                var siguiente =
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, etiqueta, StringComparison.OrdinalIgnoreCase)) ??
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                    salientes[0];

                actual = siguiente.To;

                if (string.Equals(nodo.Type, "util.end", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            _estadoPublisher?.Publish(ctx.Estado, null, null);
        }


    }


}
