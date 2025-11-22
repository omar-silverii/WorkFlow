// App_Code/MotorFlujoMinimo.cs 
// Motor mínimo para ejecutar el workflow desde JSON.

using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Globalization;
// <<< NUEVO para SQL / IO >>>
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;

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

            // NUEVO: importar seed si lo dejó el server
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

            bool b; if (bool.TryParse(s, out b)) return b;

            long l; if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;

            decimal d; if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;

            DateTime dt; if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;

            return s;
        }

    }

    // ===== Motor =====
    public class MotorFlujo
    {
        private readonly IDictionary<string, IManejadorNodo> _handlers;

        public MotorFlujo(IEnumerable<IManejadorNodo> handlers)
        {
            _handlers = handlers.ToDictionary(h => h.TipoNodo, StringComparer.OrdinalIgnoreCase);
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

            while (!string.IsNullOrEmpty(actual) && guard++ < 2000)
            {
                if (!wf.Nodes.TryGetValue(actual, out var nodo))
                    throw new InvalidOperationException("Nodo no encontrado: " + actual);

                if (!_handlers.TryGetValue(nodo.Type, out var manejador))
                    throw new InvalidOperationException("No hay handler para: " + nodo.Type);

                // Guardamos nodo actual en el Estado (útil para runtime/bandejas)
                ctx.Estado["wf.currentNodeId"] = nodo.Id;
                ctx.Estado["wf.currentNodeType"] = nodo.Type;

                var res = await manejador.EjecutarAsync(ctx, nodo, ct);
                var etiqueta = (res != null && !string.IsNullOrEmpty(res.Etiqueta)) ? res.Etiqueta : "always";

                // NUEVO: si el handler pide detener, no seguimos con las aristas
                if (res != null && res.Detener)
                {
                    ctx.Log("[Motor] ejecución detenida en nodo " + (nodo.Id ?? "?") + " (" + nodo.Type + ")");
                    break;
                }

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

    // ====== NUEVO: Handler SWITCH ======
    public class ManejadorSwitch : IManejadorNodo
    {
        public string TipoNodo => "control.switch";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string elegido = null;

            if (p.TryGetValue("casos", out var casosObj) && casosObj is Newtonsoft.Json.Linq.JObject jobj)
            {
                foreach (var prop in jobj.Properties())
                {
                    var label = prop.Name;
                    var expr = prop.Value?.ToString();
                    bool ok = HIf.Evaluar(expr, ctx);
                    ctx.Log($"[Switch] caso '{label}' => {(ok ? "True" : "False")}");
                    if (ok) { elegido = label; break; }
                }
            }

            if (elegido == null)
            {
                if (p.TryGetValue("default", out var defObj))
                    elegido = Convert.ToString(defObj);
                if (string.IsNullOrWhiteSpace(elegido)) elegido = "always";
                ctx.Log($"[Switch] default => '{elegido}'");
            }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = elegido });
        }
    }

    // ====== NUEVO: Handler DELAY ======
    public class ManejadorDelay : IManejadorNodo
    {
        public string TipoNodo => "control.delay";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            int ms = 1000;
            if (p.TryGetValue("ms", out var msObj))
            {
                int.TryParse(Convert.ToString(msObj), out ms);
                if (ms < 0) ms = 0;
            }
            ctx.Log($"[Delay] {ms} ms");
            await Task.Delay(ms, ct);
            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }

    // ====== NUEVO: Handler LOOP (foreach) ======
    public class ManejadorLoop : IManejadorNodo
    {
        public string TipoNodo => "control.loop";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string id = nodo.Id ?? "loop";
            string baseKey = $"loop.{id}";
            string itemsKey = $"{baseKey}.items";
            string indexKey = $"{baseKey}.index";

            // Cargar o inicializar items
            if (!ctx.Estado.TryGetValue(itemsKey, out var itemsObj))
            {
                IList<object> items = ObtenerItemsIniciales(ctx, p);
                ctx.Estado[itemsKey] = items;
                ctx.Estado[indexKey] = 0;
            }

            var list = NormalizarALista(ctx.Estado[itemsKey]);
            int idx = Convert.ToInt32(ctx.Estado[indexKey]);

            // Límite opcional
            int max = int.MaxValue;
            if (p.TryGetValue("max", out var maxObj))
            {
                if (int.TryParse(Convert.ToString(maxObj), out var m) && m > 0) max = m;
            }

            if (list == null || list.Count == 0 || idx >= list.Count || idx >= max)
            {
                ctx.Log($"[Loop] fin (count={(list?.Count ?? 0)}, idx={idx})");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "false" });
            }

            // Exponer item actual
            string itemVar = "item";
            if (p.TryGetValue("itemVar", out var itemVarObj) && !string.IsNullOrWhiteSpace(Convert.ToString(itemVarObj)))
                itemVar = Convert.ToString(itemVarObj);

            var actual = list[idx];
            ctx.Estado[itemVar] = actual;
            ctx.Estado[indexKey] = idx + 1;

            ctx.Log($"[Loop] idx={idx} → var '{itemVar}' seteada");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "true" });
        }

        private static IList<object> ObtenerItemsIniciales(ContextoEjecucion ctx, Dictionary<string, object> p)
        {
            if (p.TryGetValue("forEach", out var fe))
            {
                // "${algo.ruta}" o arreglo
                if (fe is string s)
                {
                    var trimmed = s.Trim();
                    if (trimmed.StartsWith("${") && trimmed.EndsWith("}"))
                    {
                        var path = trimmed.Substring(2, trimmed.Length - 3);
                        var resolved = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                        return NormalizarALista(resolved);
                    }
                    // string literal → un solo item
                    return new List<object> { s };
                }
                if (fe is Newtonsoft.Json.Linq.JArray ja) return ja.ToObject<List<object>>();
                if (fe is IEnumerable enumerable) return enumerable.Cast<object>().ToList();
            }
            // default: lista vacía
            return new List<object>();
        }

        private static IList<object> NormalizarALista(object obj)
        {
            if (obj == null) return new List<object>();
            if (obj is IList<object> lo) return lo;
            if (obj is Newtonsoft.Json.Linq.JArray ja) return ja.ToObject<List<object>>();
            if (obj is IEnumerable enumerable) return enumerable.Cast<object>().ToList();
            return new List<object> { obj };
        }
    }

    // ====== NUEVO: Handler EMAIL SEND ======
    public class ManejadorEmailSend : IManejadorNodo
    {
        public string TipoNodo => "email.send";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            string from = GetString(p, "from");
            string subject = GetString(p, "subject");
            string html = GetString(p, "html");
            string text = GetString(p, "text");

            var toList = GetStrings(p, "to");
            var ccList = GetStrings(p, "cc");
            var bccList = GetStrings(p, "bcc");
            var attachments = GetStrings(p, "attachments");

            // Overrides opcionales
            string host = GetString(p, "host");
            int port = GetInt(p, "port", 25);
            bool enableSsl = GetBool(p, "enableSsl", false);
            string user = GetString(p, "user");
            string password = GetString(p, "password");

            try
            {
                using (var msg = new MailMessage())
                {
                    if (!string.IsNullOrWhiteSpace(from)) msg.From = new MailAddress(from);
                    foreach (var t in toList) msg.To.Add(t);
                    foreach (var c in ccList) msg.CC.Add(c);
                    foreach (var b in bccList) msg.Bcc.Add(b);

                    msg.Subject = subject ?? "(sin asunto)";
                    if (!string.IsNullOrEmpty(html))
                    {
                        msg.Body = html;
                        msg.IsBodyHtml = true;
                    }
                    else
                    {
                        msg.Body = text ?? "";
                        msg.IsBodyHtml = false;
                    }

                    foreach (var path in attachments)
                    {
                        try
                        {
                            if (File.Exists(path)) msg.Attachments.Add(new Attachment(path));
                            else ctx.Log($"[Email] adjunto no encontrado: {path}");
                        }
                        catch (Exception exAtt)
                        {
                            ctx.Log($"[Email] adjunto error '{path}': {exAtt.Message}");
                        }
                    }

                    using (var smtp = string.IsNullOrWhiteSpace(host) ? new SmtpClient() : new SmtpClient(host, port))
                    {
                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            smtp.EnableSsl = enableSsl;
                            if (!string.IsNullOrWhiteSpace(user))
                                smtp.Credentials = new System.Net.NetworkCredential(user, password ?? "");
                        }

                        smtp.Send(msg);
                    }
                }

                ctx.Log("[Email] enviado");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                ctx.Estado["email.lastError"] = ex.Message;
                ctx.Log("[Email] error: " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }

        private static string GetString(Dictionary<string, object> p, string key)
            => p.TryGetValue(key, out var v) ? Convert.ToString(v) : null;

        private static int GetInt(Dictionary<string, object> p, string key, int def = 0)
        {
            if (p.TryGetValue(key, out var v) && int.TryParse(Convert.ToString(v), out var i)) return i;
            return def;
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool def = false)
        {
            if (!p.TryGetValue(key, out var v)) return def;
            var s = Convert.ToString(v);
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }

        private static List<string> GetStrings(Dictionary<string, object> p, string key)
        {
            var res = new List<string>();
            if (!p.TryGetValue(key, out var v) || v == null) return res;

            if (v is Newtonsoft.Json.Linq.JArray ja) return ja.Select(x => x.ToString()).ToList();
            if (v is IEnumerable enumerable) return enumerable.Cast<object>().Select(o => Convert.ToString(o)).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var sraw = Convert.ToString(v);
            if (string.IsNullOrWhiteSpace(sraw)) return res;
            return sraw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
        }
    }

    // ====== NUEVO: Handler FILE READ ======
    public class ManejadorFileRead : IManejadorNodo
    {
        public string TipoNodo => "file.read";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string path = p.TryGetValue("path", out var v) ? Convert.ToString(v) : null;
            string encodingName = p.TryGetValue("encoding", out var encObj) ? Convert.ToString(encObj) : "utf-8";
            string salida = p.TryGetValue("salida", out var sObj) ? Convert.ToString(sObj) : "file.content";

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    ctx.Log("[File.Read] path vacío");
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }

                Encoding enc = Encoding.UTF8;
                try { enc = Encoding.GetEncoding(encodingName); } catch { }

                string content = File.ReadAllText(path, enc);
                ctx.Estado[salida] = content;
                ctx.Estado["file.path"] = path;
                ctx.Log($"[File.Read] ok ({path}) → Estado['{salida}']");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                ctx.Estado["file.lastError"] = ex.Message;
                ctx.Log("[File.Read] error: " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }
    }

    // ====== NUEVO: Handler FILE WRITE ======
    public class ManejadorFileWrite : IManejadorNodo
    {
        public string TipoNodo => "file.write";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string path = p.TryGetValue("path", out var v) ? Convert.ToString(v) : null;
            string encodingName = p.TryGetValue("encoding", out var encObj) ? Convert.ToString(encObj) : "utf-8";
            bool overwrite = p.TryGetValue("overwrite", out var owObj) ? ContextoEjecucion.ToBool(owObj) : true;

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    ctx.Log("[File.Write] path vacío");
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }

                // Determinar contenido
                string content = null;

                if (p.TryGetValue("content", out var contObj) && contObj != null)
                {
                    content = Convert.ToString(contObj);
                }
                else if (p.TryGetValue("from", out var fromObj) && fromObj != null)
                {
                    var s = Convert.ToString(fromObj).Trim();
                    if (s.StartsWith("${") && s.EndsWith("}"))
                    {
                        var pathVar = s.Substring(2, s.Length - 3);
                        var val = ContextoEjecucion.ResolverPath(ctx.Estado, pathVar);
                        content = val?.ToString() ?? "";
                    }
                    else
                    {
                        content = s;
                    }
                }
                else
                {
                    content = "";
                }

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path) && !overwrite)
                {
                    ctx.Log($"[File.Write] ya existe y overwrite=false: {path}");
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }

                Encoding enc = Encoding.UTF8;
                try { enc = Encoding.GetEncoding(encodingName); } catch { }

                File.WriteAllText(path, content, enc);
                ctx.Log($"[File.Write] ok ({path})");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                ctx.Estado["file.lastError"] = ex.Message;
                ctx.Log("[File.Write] error: " + ex.Message);
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }
    }

    // ====== Handler SQL (existente) ======

    // ====== Handler SQL con templating y modos de resultado ======
    // ====== NUEVO: Handler SQL con templating y SELECT ======
    // ====== NUEVO: Handler SQL con templating ======
    public class ManejadorSql : IManejadorNodo
    {
        public string TipoNodo => "data.sql";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // 1) Conexión (connectionStringName > connection > DefaultConnection)
            string cnnString = null;
            object cnnNameObj;
            object cnnDirectObj;

            if (p.TryGetValue("connectionStringName", out cnnNameObj))
            {
                var cnnName = Convert.ToString(cnnNameObj);
                cnnString = ConfigurationManager.ConnectionStrings[cnnName].ConnectionString;
            }
            else if (p.TryGetValue("connection", out cnnDirectObj))
            {
                cnnString = Convert.ToString(cnnDirectObj);
            }
            else
            {
                cnnString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            }

            // 2) Comando (query|commandText) con templating
            string sql = null;
            object cmdObj, qObj;
            if (p.TryGetValue("commandText", out cmdObj))
                sql = Convert.ToString(cmdObj);
            else if (p.TryGetValue("query", out qObj))
                sql = Convert.ToString(qObj);

            if (string.IsNullOrWhiteSpace(sql))
            {
                ctx.Log("[SQL] sin query/commandText");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            sql = ctx.ExpandString(sql);

            // 3) Parámetros (K→V) con templating y tipado
            Dictionary<string, object> sqlParams = null;
            object parObj;
            if (p.TryGetValue("parameters", out parObj))
            {
                if (parObj is Newtonsoft.Json.Linq.JObject jobj)
                    sqlParams = jobj.ToObject<Dictionary<string, object>>();
                else if (parObj is Dictionary<string, object> d0)
                    sqlParams = new Dictionary<string, object>(d0, StringComparer.OrdinalIgnoreCase);
            }
            if (sqlParams == null) sqlParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var finalParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sqlParams)
            {
                object val = kv.Value;
                if (val is string s)
                {
                    var expanded = ctx.ExpandString(s ?? "");
                    val = ContextoEjecucion.Coerce(expanded);
                }
                finalParams[kv.Key] = val;
            }

            // 4) Result mode + outputKey (opcionales)
            //    resultMode: "nonquery" (default) | "scalar" | "datatable"
            string resultMode = null;
            object rmObj;
            if (p.TryGetValue("resultMode", out rmObj))
                resultMode = Convert.ToString(rmObj);
            if (string.IsNullOrWhiteSpace(resultMode)) resultMode = "nonquery";

            string outputKey = null; // ejemplo: "payload.polizaId" / "payload.sql"
            object okObj;
            if (p.TryGetValue("outputKey", out okObj))
                outputKey = Convert.ToString(okObj);

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                foreach (var kv in finalParams)
                {
                    cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
                }

                await cn.OpenAsync(ct);

                if (string.Equals(resultMode, "scalar", StringComparison.OrdinalIgnoreCase))
                {
                    var scalar = await cmd.ExecuteScalarAsync(ct);
                    ctx.Estado["sql.scalar"] = scalar;
                    if (!string.IsNullOrWhiteSpace(outputKey))
                        ContextoEjecucion.SetPath(ctx.Estado, outputKey, scalar);

                    ctx.Log("[SQL] scalar OK");
                }
                else if (string.Equals(resultMode, "datatable", StringComparison.OrdinalIgnoreCase))
                {
                    using (var rdr = await cmd.ExecuteReaderAsync(ct))
                    {
                        var rows = new List<Dictionary<string, object>>();
                        while (await rdr.ReadAsync(ct))
                        {
                            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < rdr.FieldCount; i++)
                            {
                                row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
                            }
                            rows.Add(row);
                        }
                        ctx.Estado["sql.rowsData"] = rows;
                        if (!string.IsNullOrWhiteSpace(outputKey))
                            ContextoEjecucion.SetPath(ctx.Estado, outputKey, rows);

                        ctx.Log("[SQL] datatable OK. filas=" + rows.Count);
                    }
                }
                else
                {
                    // nonquery
                    var rows = await cmd.ExecuteNonQueryAsync(ct);
                    ctx.Estado["sql.rows"] = rows;
                    if (!string.IsNullOrWhiteSpace(outputKey))
                        ContextoEjecucion.SetPath(ctx.Estado, outputKey, rows);

                    ctx.Log("[SQL] ejecutado. filas=" + rows);
                }
            }

            return new ResultadoEjecucion { Etiqueta = "always" };
        }


        // Reemplaza ${ruta.en.ctx} por el valor en ctx.Estado
        private static string Expand(string text, Dictionary<string, object> estado)
        {
            if (string.IsNullOrEmpty(text) || estado == null) return text;
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\$\{([^}]+)\}",
                m =>
                {
                    var path = m.Groups[1].Value.Trim();
                    var val = ContextoEjecucion.ResolverPath(estado, path);
                    return val == null ? "" : Convert.ToString(val);
                }
            );
        }
    }

    // ==== Utilidad de templating (expandir ${path} contra ctx.Estado) ====
    internal static class TemplateUtil
    {
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

    public class HNotify : IManejadorNodo
    {
        public string TipoNodo { get { return "util.notify"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var tipo = (p.ContainsKey("tipo") ? Convert.ToString(p["tipo"]) : "email").ToLowerInvariant();

            if (tipo == "email")
            {
                try
                {
                    var destino = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("destino") ? p["destino"] : ""));
                    var asunto = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("asunto") ? p["asunto"] : "Notificación"));
                    var mensaje = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("mensaje") ? p["mensaje"] : ""));

                    if (string.IsNullOrWhiteSpace(destino))
                    {
                        ctx.Log("[Email] destino vacío; no se envía.");
                        return new ResultadoEjecucion { Etiqueta = "always" };
                    }

                    var smtpSection = System.Configuration.ConfigurationManager
                        .GetSection("system.net/mailSettings/smtp") as System.Net.Configuration.SmtpSection;
                    var fromAddr = (smtpSection != null && !string.IsNullOrWhiteSpace(smtpSection.From))
                                    ? smtpSection.From : "no-reply@localhost";

                    using (var msg = new System.Net.Mail.MailMessage(fromAddr, destino, asunto ?? "Notificación", mensaje ?? string.Empty))
                    {
                        msg.IsBodyHtml = (mensaje ?? "").IndexOf("<", StringComparison.Ordinal) >= 0;
                        using (var smtp = new System.Net.Mail.SmtpClient())
                        {
                            await smtp.SendMailAsync(msg);
                        }
                    }
                    ctx.Log("[Email] enviado a " + destino);
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
                catch (Exception ex)
                {
                    ctx.Log("[Email] error: " + ex.Message);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }

            // Otros tipos (sms/webhook) no implementados en este handler mínimo
            ctx.Log("[Notify] tipo '" + tipo + "' no implementado; se ignora.");
            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }

    public class HChatNotify : IManejadorNodo
    {
        public string TipoNodo { get { return "chat.notify"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var canal = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("canal") ? p["canal"] : ""));
            var mensaje = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("mensaje") ? p["mensaje"] : ""));
            var webhook = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("webhookUrl") ? p["webhookUrl"] : ""));

            if (!string.IsNullOrWhiteSpace(webhook))
            {
                try
                {
                    var payload = new { text = string.IsNullOrEmpty(mensaje) ? "(sin mensaje)" : mensaje };
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                    using (var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"))
                    {
                        var resp = await ctx.Http.PostAsync(webhook, content, ct);
                        ctx.Log("[Chat] POST " + webhook + " => " + (int)resp.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    ctx.Log("[Chat] error webhook: " + ex.Message);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }
            else
            {
                ctx.Log("[Chat] (" + (canal ?? "canal") + "): " + (mensaje ?? ""));
            }

            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }

    public class HQueuePublish : IManejadorNodo
    {
        public string TipoNodo { get { return "queue.publish"; } }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            var broker = (p.ContainsKey("broker") ? Convert.ToString(p["broker"]) : "sql").ToLowerInvariant();
            var queue = TemplateUtil.Expand(ctx, Convert.ToString(p.ContainsKey("queue") ? p["queue"] : "default"));
            object payloadObj;
            p.TryGetValue("payload", out payloadObj);

            // Expandir strings de 1er nivel dentro del payload si viene como JObject
            var tok = payloadObj as Newtonsoft.Json.Linq.JToken;
            if (tok != null && tok.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                foreach (var prop in ((Newtonsoft.Json.Linq.JObject)tok).Properties())
                {
                    if (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                    {
                        prop.Value = TemplateUtil.Expand(ctx, prop.Value.ToString());
                    }
                }
            }
            var payloadJson = tok != null
                ? tok.ToString(Newtonsoft.Json.Formatting.None)
                : (payloadObj != null ? Newtonsoft.Json.JsonConvert.SerializeObject(payloadObj) : "{}");

            if (broker == "sql")
            {
                try
                {
                    var cs = System.Configuration.ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                    using (var cn = new System.Data.SqlClient.SqlConnection(cs))
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
IF OBJECT_ID('dbo.WF_Queue','U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_Queue(
      Id INT IDENTITY(1,1) PRIMARY KEY,
      Queue NVARCHAR(100) NOT NULL,
      Payload NVARCHAR(MAX) NOT NULL,
      CreatedAt DATETIME NOT NULL DEFAULT(GETDATE()),
      Processed BIT NOT NULL DEFAULT(0)
    );
END;
INSERT INTO dbo.WF_Queue(Queue, Payload) VALUES(@q, @p);";
                        cmd.Parameters.Add("@q", System.Data.SqlDbType.NVarChar, 100).Value = queue ?? "default";
                        cmd.Parameters.Add("@p", System.Data.SqlDbType.NVarChar).Value = (object)payloadJson ?? "{}";
                        await cn.OpenAsync(ct);
                        await cmd.ExecuteNonQueryAsync(ct);
                    }
                    ctx.Log("[Queue] Enviado a '" + queue + "' vía SQL.");
                    ctx.Estado["queue.last"] = new Dictionary<string, object>
                {
                    { "queue", queue },
                    { "payload", payloadJson }
                };
                    return new ResultadoEjecucion { Etiqueta = "always" };
                }
                catch (Exception ex)
                {
                    ctx.Log("[Queue] error SQL: " + ex.Message);
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }
            }

            if (broker == "rabbitmq")
            {
                ctx.Log("[Queue] RabbitMQ no implementado en este motor mínimo (usa broker='sql' para demo).");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // Fallback: solo log
            ctx.Log("[Queue] (" + broker + ") -> " + queue + " payload=" + payloadJson);
            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }

    public class HHumanTask : IManejadorNodo
    {
        public string TipoNodo => "human.task";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Plantillas (se expanden con ctx.ExpandString, igual que logger/sql)
            var tituloTpl = GetString(p, "titulo") ?? GetString(p, "title") ?? "(sin título)";
            var descripcionTpl = GetString(p, "descripcion") ?? GetString(p, "description");
            var rolTpl = GetString(p, "rol");
            var usuarioTpl = GetString(p, "usuario");
            var slaMin = GetInt(p, "deadlineMinutes", 0);   // SLA en minutos (0 = sin vencimiento)

            var titulo = ctx.ExpandString(tituloTpl);
            var descripcion = ctx.ExpandString(descripcionTpl);
            var rol = ctx.ExpandString(rolTpl);
            var usuario = ctx.ExpandString(usuarioTpl);

            // Metadata opcional (objeto libre -> JSON)
            p.TryGetValue("metadata", out var metaObj);
            string metaJson = null;

            if (metaObj != null)
            {
                if (metaObj is string sMeta)
                {
                    // permitimos también plantillas dentro de metadata string
                    metaJson = ctx.ExpandString(sMeta);
                }
                else if (metaObj is Newtonsoft.Json.Linq.JToken tok)
                {
                    // expandimos strings de primer nivel
                    var clone = tok.DeepClone();
                    if (clone is Newtonsoft.Json.Linq.JObject jo)
                    {
                        foreach (var prop in jo.Properties())
                        {
                            if (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String)
                            {
                                prop.Value = ctx.ExpandString(prop.Value.ToString());
                            }
                        }
                    }
                    metaJson = JsonConvert.SerializeObject(clone);
                }
                else
                {
                    metaJson = JsonConvert.SerializeObject(metaObj);
                }
            }

            // Instancia actual (el Runtime la pasa via HttpContext.Current.Items["WF_SEED"]["wf.instanceId"])
            var instanciaId = TryGetLong(ctx.Estado, "wf.instanceId");

            if (!instanciaId.HasValue)
            {
                // Modo demo (ejecución desde WorkflowUI.aspx "Probar motor"):
                // no tenemos instancia, así que no grabamos en BD NI detenemos el flujo.
                ctx.Log("[HumanTask] wf.instanceId no encontrado en Estado; modo demo (no se graba WF_Tarea).");
                return new ResultadoEjecucion
                {
                    Etiqueta = "always",
                    Detener = false
                };
            }

            var ahora = DateTime.Now;
            DateTime? fechaVenc = null;
            if (slaMin > 0)
                fechaVenc = ahora.AddMinutes(slaMin);

            long tareaId;
            var cnnString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO WF_Tarea
    (WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion,
     RolDestino, UsuarioAsignado, Estado, FechaCreacion, FechaVencimiento,
     Resultado, Datos)
VALUES
    (@WF_InstanciaId, @NodoId, @NodoTipo, @Titulo, @Descripcion,
     @RolDestino, @UsuarioAsignado, @Estado, @FechaCreacion, @FechaVencimiento,
     @Resultado, @Datos);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

                cmd.Parameters.Add("@WF_InstanciaId", SqlDbType.BigInt).Value = instanciaId.Value;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = (object)(nodo.Id ?? "") ?? DBNull.Value;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = (object)(nodo.Type ?? "") ?? DBNull.Value;
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = (object)(titulo ?? "") ?? DBNull.Value;

                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, -1).Value =
                    string.IsNullOrEmpty(descripcion) ? (object)DBNull.Value : descripcion;

                cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value =
                    string.IsNullOrEmpty(rol) ? (object)DBNull.Value : rol;

                cmd.Parameters.Add("@UsuarioAsignado", SqlDbType.NVarChar, 100).Value =
                    string.IsNullOrEmpty(usuario) ? (object)DBNull.Value : usuario;

                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = "Pendiente";
                cmd.Parameters.Add("@FechaCreacion", SqlDbType.DateTime).Value = ahora;
                cmd.Parameters.Add("@FechaVencimiento", SqlDbType.DateTime).Value =
                    (object)fechaVenc ?? DBNull.Value;

                cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = DBNull.Value;

                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar, -1).Value =
                    string.IsNullOrEmpty(metaJson) ? (object)DBNull.Value : metaJson;

                await cn.OpenAsync(ct);
                var obj = await cmd.ExecuteScalarAsync(ct);
                tareaId = Convert.ToInt64(obj);
            }

            // Dejamos rastro en el contexto (útil para debug / otros nodos)
            ctx.Estado["task.lastId"] = tareaId;
            if (!string.IsNullOrEmpty(nodo.Id))
            {
                ContextoEjecucion.SetPath(ctx.Estado, $"task.{nodo.Id}.id", tareaId);
            }

            ctx.Log($"[HumanTask] WF_InstanciaId={instanciaId} Nodo={nodo.Id} TareaId={tareaId} Titulo='{titulo}'");

            // En runtime real: detenemos el motor hasta que alguien resuelva la tarea
            return new ResultadoEjecucion
            {
                Etiqueta = "wait",
                Detener = true
            };
        }

        // ---- helpers internos ----

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            return p.TryGetValue(key, out var v) && v != null ? Convert.ToString(v) : null;
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def = 0)
        {
            if (p == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;
            return int.TryParse(Convert.ToString(v), out var i) ? i : def;
        }

        private static long? TryGetLong(IDictionary<string, object> estado, string key)
        {
            if (estado == null) return null;
            if (!estado.TryGetValue(key, out var v) || v == null) return null;

            if (v is long l) return l;
            if (v is int i) return i;
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ls)) return ls;

            try { return Convert.ToInt64(v); }
            catch { return null; }
        }
    }


    public class HError : IManejadorNodo
    {
        public string TipoNodo { get { return "util.error"; } }

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var capturar = p.ContainsKey("capturar") && ContextoEjecucion.ToBool(p["capturar"]);
            var notificar = p.ContainsKey("notificar") && ContextoEjecucion.ToBool(p["notificar"]);
            var reintentar = p.ContainsKey("volverAIntentar") && ContextoEjecucion.ToBool(p["volverAIntentar"]);

            ctx.Log("[Error] capturar=" + capturar + " notificar=" + notificar + " retry=" + reintentar);
            // En este motor mínimo no forzamos reintentos globales; dejamos rastro en log/estado para futuras mejoras.
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
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
                new HNotify(),
                new HChatNotify(),
                new HQueuePublish(),
                new HError(),
                // ===== Tareas humanas =====
                new HHumanTask(),
                // ===== Registro Lote A =====
                new ManejadorSwitch(),
                new ManejadorDelay(),
                new ManejadorLoop(),
                new ManejadorEmailSend(),
                new ManejadorFileRead(),
                new ManejadorFileWrite(),
            };

            // Handler SQL se puede inyectar como extra (ya se hace en btnProbarMotor_Click)
            // list.Add(new Intranet.WorkflowStudio.Runtime.ManejadorSql());

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
            var handlers = CrearHandlersPorDefecto();            // List<IManejadorNodo>
            var motor = new MotorFlujo(handlers);                // MotorFlujo recibe IEnumerable
            await motor.EjecutarAsync(wf, log ?? (_ => { }), ct);
        }

        /// <summary>
        /// Igual que el anterior pero permitiendo agregar handlers extra (por ejemplo ManejadorSql).
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
