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

        /// <summary>
        /// Intenta convertir string a bool/int/long/decimal/datetime; si no, deja string.
        /// </summary>
        public static object Coerce(string s)
        {
            if (s == null) return null;

            bool b;
            if (bool.TryParse(s, out b)) return b;

            long l;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;

            decimal d;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;

            DateTime dt;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;

            return s;
        }
    }

    // ===== Motor =====
    public class MotorFlujo
    {
        private readonly IDictionary<string, IManejadorNodo> _handlers;

        public MotorFlujo(IEnumerable<IManejadorNodo> handlers)
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
            string actual = wf.StartNodeId;
            int guard = 0;

            // Publico estado inicial (por si algún nodo consulta WF_CTX_ESTADO)
            PublishEstado(ctx, null, null);

            while (!string.IsNullOrEmpty(actual) && guard++ < 2000)
            {
                if (!wf.Nodes.TryGetValue(actual, out var nodo))
                    throw new InvalidOperationException("Nodo no encontrado: " + actual);

                if (!_handlers.TryGetValue(nodo.Type, out var manejador))
                    throw new InvalidOperationException("No hay handler para: " + nodo.Type);

                var res = await manejador.EjecutarAsync(ctx, nodo, ct);

                // Publicar estado luego de ejecutar cada nodo
                PublishEstado(ctx, nodo.Id, nodo.Type);

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

                var salientes = wf.Edges
                    .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (salientes.Count == 0) break; // fin del flujo si no hay salida

                var siguiente =
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, etiqueta, StringComparison.OrdinalIgnoreCase)) ??
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                    salientes[0];

                actual = siguiente.To;

                if (string.Equals(nodo.Type, "util.end", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            // Publico el estado final por las dudas
            PublishEstado(ctx, null, null);
        }

        private static void PublishEstado(ContextoEjecucion ctx, string nodoId, string nodoTipo)
        {
            try
            {
                var items = System.Web.HttpContext.Current?.Items;
                if (items == null || ctx == null || ctx.Estado == null)
                    return;

                if (!string.IsNullOrEmpty(nodoId))
                    ctx.Estado["wf.currentNodeId"] = nodoId;

                if (!string.IsNullOrEmpty(nodoTipo))
                    ctx.Estado["wf.currentNodeType"] = nodoTipo;

                items["WF_CTX_ESTADO"] = ctx.Estado;
            }
            catch
            {
                // nunca romper el motor por HttpContext
            }
        }
    }

    // ===== Helper de demo =====
    public static class MotorDemo
    {
        public static WorkflowDef FromJson(string json)
        {
            return JsonConvert.DeserializeObject<WorkflowDef>(json);
        }

        // Devuelve List<IManejadorNodo>
        public static List<IManejadorNodo> CrearHandlersPorDefecto()
        {
            var list = new List<IManejadorNodo>
            {
                // básicos
                new HStart(),
                new HEnd(),
                new HLogger(),
                new HIf(),
                new HDocEntrada(),
                new HHttpRequest(),

                // notify / chat / queue
                new HNotify(),
                new HChatNotify(),
                new HQueuePublish(),
                new HError(),

                // tareas humanas
                new HHumanTask(),

                // control
                new ManejadorSwitch(),
                new ManejadorDelay(),
                new ManejadorLoop(),

                // email / file
                new ManejadorEmailSend(),
                new HFileWrite() // <-- IMPORTANTE: usá tu handler nuevo (content + fallback origen)
            };

            return list;
        }

        /// <summary>
        /// Ejecuta con los handlers por defecto.
        /// </summary>
        public static async Task EjecutarAsync(
            WorkflowDef wf,
            Action<string> log,
            CancellationToken ct = default(CancellationToken))
        {
            var handlers = CrearHandlersPorDefecto();
            var motor = new MotorFlujo(handlers);
            await motor.EjecutarAsync(wf, log ?? (_ => { }), ct);
        }

        /// <summary>
        /// Igual que el anterior pero permitiendo agregar handlers extra (por ejemplo data.sql runtime).
        /// </summary>
        public static async Task EjecutarAsync(
            WorkflowDef wf,
            Action<string> log,
            IEnumerable<IManejadorNodo> handlersExtra,
            CancellationToken ct = default(CancellationToken))
        {
            var handlers = CrearHandlersPorDefecto();
            if (handlersExtra != null) handlers.AddRange(handlersExtra);

            var motor = new MotorFlujo(handlers);
            await motor.EjecutarAsync(wf, log ?? (_ => { }), ct);
        }
    }
}
