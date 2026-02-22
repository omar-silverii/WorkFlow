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

        // Cliente general (externo)
        public HttpClient Http { get; private set; }

        // ✅ NUEVO: Cliente intranet con credenciales Windows
        public HttpClient HttpIntranet { get; private set; }

        public ContextoEjecucion(Action<string> log = null)
        {
            Estado = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Log = log ?? (_ => { });

            Http = new HttpClient();

            // Cliente SOLO intranet con credenciales Windows
            try
            {
                var handler = new HttpClientHandler { UseDefaultCredentials = true };
                HttpIntranet = new HttpClient(handler);
            }
            catch
            {
                HttpIntranet = null;
            }

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

            // Base para URLs relativas (aplica a ambos clientes)
            try
            {
                var req = System.Web.HttpContext.Current?.Request;
                if (req != null && req.Url != null)
                {
                    var app = req.ApplicationPath ?? "/";
                    if (!app.EndsWith("/")) app += "/";
                    var baseUri = new Uri(req.Url.Scheme + "://" + req.Url.Authority + app);

                    Http.BaseAddress = baseUri;
                    if (HttpIntranet != null) HttpIntranet.BaseAddress = baseUri;
                }
            }
            catch { }
        }

        public static object ResolverPath(object root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path))
                return null;

            var parts = path.Split('.');
            object curr = root;

            foreach (var raw in parts)
            {
                if (curr == null)
                    return null;

                // Soporte para índices tipo items[0]
                string propName = raw;
                int? index = null;

                int bracketStart = raw.IndexOf('[');
                if (bracketStart >= 0)
                {
                    int bracketEnd = raw.IndexOf(']', bracketStart + 1);
                    if (bracketEnd > bracketStart)
                    {
                        propName = raw.Substring(0, bracketStart);
                        var inside = raw.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                        if (int.TryParse(inside, out var ix))
                            index = ix;
                    }
                }

                // Resolver propiedad
                if (curr is IDictionary<string, object> dict)
                {
                    dict.TryGetValue(propName, out curr);
                }
                else if (curr is Newtonsoft.Json.Linq.JObject jobj)
                {
                    curr = jobj.TryGetValue(propName, StringComparison.OrdinalIgnoreCase, out var tok) ? tok : null;
                    if (curr is Newtonsoft.Json.Linq.JValue jv)
                        curr = jv.Value;
                }
                else
                {
                    var prop = curr.GetType().GetProperty(propName);
                    curr = prop != null ? prop.GetValue(curr, null) : null;
                }

                // Aplicar índice si existe
                if (index.HasValue)
                {
                    int ix = index.Value;

                    if (curr is System.Collections.IList list)
                    {
                        curr = (ix >= 0 && ix < list.Count) ? list[ix] : null;
                    }
                    else if (curr is Newtonsoft.Json.Linq.JArray jarr)
                    {
                        curr = (ix >= 0 && ix < jarr.Count) ? jarr[ix] : null;
                        if (curr is Newtonsoft.Json.Linq.JValue jv)
                            curr = jv.Value;
                    }
                    else
                    {
                        curr = null;
                    }
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

        private static readonly System.Text.RegularExpressions.Regex _tplRegex =
            new System.Text.RegularExpressions.Regex(@"\$\{(?<path>[^}]+)\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

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
            // ✅ para que handlers como control.parallel puedan ejecutar ramas con el mismo motor/contexto
            //ctx.Estado["wf.def"] = wf;
            //ctx.Estado["wf.motor"] = this;

            var items = System.Web.HttpContext.Current?.Items ?? Intranet.WorkflowStudio.Runtime.WorkflowAmbient.Items.Value;
            if (items != null)
            {
                items["wf.def"] = wf;        // ✅ misma clave, pero en Items (no se persiste)
                items["wf.motor"] = this;    // ✅ misma clave, pero en Items (no se persiste)

                Intranet.WorkflowStudio.Runtime.WorkflowAmbient.Items.Value = items;
            }

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

                ResultadoEjecucion res = null;

                // ================================================================
                // control.retry: el motor reintenta internamente el nodo siguiente
                // - reintenta si:
                //     a) el nodo siguiente devuelve Etiqueta == "error"
                //     b) el handler del nodo siguiente lanza excepción
                // ================================================================
                if (string.Equals(nodo.Type, "control.retry", StringComparison.OrdinalIgnoreCase))
                {
                    // 1) ejecutar handler del retry (solo log/config)
                    try
                    {
                        _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);
                        await manejador.EjecutarAsync(ctx, nodo, ct);
                    }
                    catch (Exception exRetry)
                    {
                        // si el retry en sí falla, lo tratamos como error del flujo
                        ctx.Log($"[Retry] ERROR en control.retry: {exRetry.Message}");
                    }

                    // 2) resolver destino (arista always o primera)
                    var salRetry = (wf.Edges ?? new List<EdgeDef>())
                        .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (salRetry.Count == 0)
                        throw new InvalidOperationException("control.retry sin aristas salientes: " + nodo.Id);

                    var edgeToTry =
                        salRetry.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                        salRetry.FirstOrDefault();

                    if (edgeToTry == null || string.IsNullOrWhiteSpace(edgeToTry.To))
                        throw new InvalidOperationException("control.retry sin destino válido: " + nodo.Id);

                    if (!wf.Nodes.TryGetValue(edgeToTry.To, out var nodoTarget))
                        throw new InvalidOperationException("Nodo destino de retry no encontrado: " + edgeToTry.To);

                    if (!_handlers.TryGetValue(nodoTarget.Type, out var handlerTarget))
                        throw new InvalidOperationException("No hay handler para nodo destino de retry: " + nodoTarget.Type);

                    // 3) leer params retry
                    int reintentos = GetIntParam(nodo, "reintentos", 3);
                    int backoffMs = GetIntParam(nodo, "backoffMs", 500);
                    if (reintentos < 0) reintentos = 0;
                    if (reintentos > 50) reintentos = 50;
                    if (backoffMs < 0) backoffMs = 0;
                    if (backoffMs > 600000) backoffMs = 600000;

                    int maxIntentos = reintentos + 1;

                    ResultadoEjecucion resTarget = null;
                    Exception ultimaEx = null;

                    for (int intento = 1; intento <= maxIntentos; intento++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            _estadoPublisher?.Publish(ctx.Estado, nodoTarget.Id, nodoTarget.Type);
                            ctx.Log($"[Retry] intento {intento}/{maxIntentos} -> {nodoTarget.Id} ({nodoTarget.Type})");
                            resTarget = await handlerTarget.EjecutarAsync(ctx, nodoTarget, ct);
                            ultimaEx = null;
                        }
                        catch (Exception ex)
                        {
                            ultimaEx = ex;
                            ctx.Log($"[Retry] excepción en intento {intento}/{maxIntentos}: {ex.Message}");

                            // forzamos etiqueta error para el criterio de retry (tu ResultadoEjecucion no tiene 'Mensaje')
                            resTarget = new ResultadoEjecucion { Etiqueta = "error" };
                        }


                        // publicar estado luego de ejecutar el nodo target
                        _estadoPublisher?.Publish(ctx.Estado, nodoTarget.Id, nodoTarget.Type);

                        // criterio de “fallo” => retry
                        var et = (resTarget != null && !string.IsNullOrEmpty(resTarget.Etiqueta)) ? resTarget.Etiqueta : "always";
                        bool esError = string.Equals(et, "error", StringComparison.OrdinalIgnoreCase);

                        if (!esError)
                        {
                            // éxito: salimos del retry loop
                            break;
                        }

                        // si falló y quedan intentos, backoff
                        if (intento < maxIntentos && backoffMs > 0)
                        {
                            ctx.Log($"[Retry] backoff {backoffMs}ms (sigue retry)");
                            await Task.Delay(backoffMs, ct);
                        }
                    }

                    // si terminó con excepción persistente, log extra (sin romper compatibilidad)
                    if (ultimaEx != null)
                        ctx.Log($"[Retry] agotados intentos. última excepción: {ultimaEx.Message}");

                    // 4) aplicar lógica de transición EN BASE AL NODO TARGET (no al retry)
                    var etiquetaTarget = (resTarget != null && !string.IsNullOrEmpty(resTarget.Etiqueta))
                        ? resTarget.Etiqueta
                        : "always";

                    var salTarget = (wf.Edges ?? new List<EdgeDef>())
                        .Where(e => string.Equals(e.From, nodoTarget.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (salTarget.Count == 0)
                        break;

                    var nextAfterTarget =
                        salTarget.FirstOrDefault(e => string.Equals(e.Condition, etiquetaTarget, StringComparison.OrdinalIgnoreCase)) ??
                        salTarget.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                        salTarget[0];

                    actual = nextAfterTarget.To;

                    // aplicar mismas reglas post-ejecución (detener)
                    if (ctx.Estado != null &&
                        ctx.Estado.TryGetValue("wf.detener", out var detValRetry) &&
                        ContextoEjecucion.ToBool(detValRetry))
                    {
                        ctx.Log($"[Motor] ejecución detenida (desde retry) en nodo {nodoTarget.Id} ({nodoTarget.Type})");
                        break;
                    }

                    // saltamos el flujo normal porque ya avanzamos "actual" manualmente
                    continue;
                }
                // ================================================================

                // ejecución normal
                _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);
                res = await manejador.EjecutarAsync(ctx, nodo, ct);


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

        public async Task EjecutarDesdeNodoAsync(
    WorkflowDef wf,
    ContextoEjecucion ctx,
    string startNodeId,
    string stopNodeId,
    CancellationToken ct = default(CancellationToken))
        {
            if (wf == null) throw new ArgumentNullException(nameof(wf));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (string.IsNullOrWhiteSpace(startNodeId)) return;

            string actual = startNodeId;
            int guard = 0;

            while (!string.IsNullOrEmpty(actual) && guard++ < 2000)
            {
                ct.ThrowIfCancellationRequested();

                // STOP: no ejecutar el stop node (join)
                if (!string.IsNullOrWhiteSpace(stopNodeId) &&
                    string.Equals(actual, stopNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!wf.Nodes.TryGetValue(actual, out var nodo))
                    throw new InvalidOperationException("Nodo no encontrado: " + actual);

                if (!_handlers.TryGetValue(nodo.Type, out var manejador))
                    throw new InvalidOperationException("No hay handler para: " + nodo.Type);

                ResultadoEjecucion res = null;

                // ====== COPIA control.retry (misma lógica que el motor principal) ======
                if (string.Equals(nodo.Type, "control.retry", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);
                        await manejador.EjecutarAsync(ctx, nodo, ct);
                    }
                    catch (Exception exRetry)
                    {
                        ctx.Log($"[Retry] ERROR en control.retry: {exRetry.Message}");
                    }

                    var salRetry = (wf.Edges ?? new List<EdgeDef>())
                        .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (salRetry.Count == 0)
                        throw new InvalidOperationException("control.retry sin aristas salientes: " + nodo.Id);

                    var edgeToTry =
                        salRetry.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                        salRetry.FirstOrDefault();

                    if (edgeToTry == null || string.IsNullOrWhiteSpace(edgeToTry.To))
                        throw new InvalidOperationException("control.retry sin destino válido: " + nodo.Id);

                    if (!wf.Nodes.TryGetValue(edgeToTry.To, out var nodoTarget))
                        throw new InvalidOperationException("Nodo destino de retry no encontrado: " + edgeToTry.To);

                    if (!_handlers.TryGetValue(nodoTarget.Type, out var handlerTarget))
                        throw new InvalidOperationException("No hay handler para nodo destino de retry: " + nodoTarget.Type);

                    int reintentos = GetIntParam(nodo, "reintentos", 3);
                    int backoffMs = GetIntParam(nodo, "backoffMs", 500);
                    if (reintentos < 0) reintentos = 0;
                    if (reintentos > 50) reintentos = 50;
                    if (backoffMs < 0) backoffMs = 0;
                    if (backoffMs > 600000) backoffMs = 600000;

                    int maxIntentos = reintentos + 1;

                    ResultadoEjecucion resTarget = null;
                    Exception ultimaEx = null;

                    for (int intento = 1; intento <= maxIntentos; intento++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            _estadoPublisher?.Publish(ctx.Estado, nodoTarget.Id, nodoTarget.Type);
                            ctx.Log($"[Retry] intento {intento}/{maxIntentos} -> {nodoTarget.Id} ({nodoTarget.Type})");
                            resTarget = await handlerTarget.EjecutarAsync(ctx, nodoTarget, ct);
                            ultimaEx = null;
                        }
                        catch (Exception ex)
                        {
                            ultimaEx = ex;
                            ctx.Log($"[Retry] excepción en intento {intento}/{maxIntentos}: {ex.Message}");
                            resTarget = new ResultadoEjecucion { Etiqueta = "error" };
                        }

                        _estadoPublisher?.Publish(ctx.Estado, nodoTarget.Id, nodoTarget.Type);

                        var et = (resTarget != null && !string.IsNullOrEmpty(resTarget.Etiqueta)) ? resTarget.Etiqueta : "always";
                        bool esError = string.Equals(et, "error", StringComparison.OrdinalIgnoreCase);

                        if (!esError) break;

                        if (intento < maxIntentos && backoffMs > 0)
                        {
                            ctx.Log($"[Retry] backoff {backoffMs}ms (sigue retry)");
                            await Task.Delay(backoffMs, ct);
                        }
                    }

                    if (ultimaEx != null)
                        ctx.Log($"[Retry] agotados intentos. última excepción: {ultimaEx.Message}");

                    var etiquetaTarget = (resTarget != null && !string.IsNullOrEmpty(resTarget.Etiqueta))
                        ? resTarget.Etiqueta
                        : "always";

                    var salTarget = (wf.Edges ?? new List<EdgeDef>())
                        .Where(e => string.Equals(e.From, nodoTarget.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (salTarget.Count == 0)
                        return;

                    var nextAfterTarget =
                        salTarget.FirstOrDefault(e => string.Equals(e.Condition, etiquetaTarget, StringComparison.OrdinalIgnoreCase)) ??
                        salTarget.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                        salTarget[0];

                    actual = nextAfterTarget.To;

                    if (ctx.Estado != null &&
                        ctx.Estado.TryGetValue("wf.detener", out var detValRetry) &&
                        ContextoEjecucion.ToBool(detValRetry))
                    {
                        ctx.Log($"[Motor] ejecución detenida (desde retry) en nodo {nodoTarget.Id} ({nodoTarget.Type})");
                        return;
                    }

                    continue;
                }
                // =====================================================================

                _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);
                res = await manejador.EjecutarAsync(ctx, nodo, ct);
                _estadoPublisher?.Publish(ctx.Estado, nodo.Id, nodo.Type);

                if (ctx.Estado != null &&
                    ctx.Estado.TryGetValue("wf.detener", out var detVal) &&
                    ContextoEjecucion.ToBool(detVal))
                {
                    ctx.Log($"[Motor] ejecución detenida en nodo {nodo.Id} ({nodo.Type})");
                    return;
                }

                var etiqueta = (res != null && !string.IsNullOrEmpty(res.Etiqueta)) ? res.Etiqueta : "always";

                var salientes = (wf.Edges ?? new List<EdgeDef>())
                    .Where(e => string.Equals(e.From, actual, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (salientes.Count == 0) return;

                var siguiente =
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, etiqueta, StringComparison.OrdinalIgnoreCase)) ??
                    salientes.FirstOrDefault(e => string.Equals(e.Condition, "always", StringComparison.OrdinalIgnoreCase)) ??
                    salientes[0];

                actual = siguiente.To;

                if (string.Equals(nodo.Type, "util.end", StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }



        private static int GetIntParam(NodeDef nodo, string key, int def)
        {
            try
            {
                if (nodo?.Parameters == null) return def;
                if (!nodo.Parameters.TryGetValue(key, out var v) || v == null) return def;

                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (v is decimal d) return (int)d;
                if (v is double db) return (int)db;

                var s = v.ToString().Trim();
                if (string.IsNullOrEmpty(s)) return def;

                s = s.Replace(",", ".");
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                    return x;

                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dd))
                    return (int)dd;

                return def;
            }
            catch { return def; }
        }

       


    }


}
