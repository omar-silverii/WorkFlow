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

                var res = await manejador.EjecutarAsync(ctx, nodo, ct);
                var etiqueta = (res != null && !string.IsNullOrEmpty(res.Etiqueta)) ? res.Etiqueta : "always";

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
    public class ManejadorSql : IManejadorNodo
    {
        public string TipoNodo => "data.sql";

        // Reemplaza ${path.al.valor} con el valor del contexto (ctx.Estado)
        private static string ResolveTemplates(string input, ContextoEjecucion ctx)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return System.Text.RegularExpressions.Regex.Replace(
                input,
                @"\$\{([^}]+)\}",
                m => {
                    var path = m.Groups[1].Value.Trim();
                    var val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                    return val == null ? "" : Convert.ToString(val);
                });
        }

        // Intenta tipar (int, long, decimal, bool); si no, deja string
        private static object Coerce(string s)
        {
            if (s == null) return DBNull.Value;
            if (int.TryParse(s, out var i)) return i;
            if (long.TryParse(s, out var l)) return l;
            if (decimal.TryParse(s, out var d)) return d;
            if (bool.TryParse(s, out var b)) return b;
            return s;
        }

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // 1) connection
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
                cnnString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            }

            // 2) SQL + modo
            string sql = null;
            if (p.TryGetValue("commandText", out var cmdObj)) sql = Convert.ToString(cmdObj);
            else if (p.TryGetValue("query", out var qObj)) sql = Convert.ToString(qObj);
            if (string.IsNullOrWhiteSpace(sql)) { ctx.Log("[SQL] sin query/commandText"); return new ResultadoEjecucion { Etiqueta = "always" }; }

            // templating en el SQL (por si querés usar ${...} dentro del texto)
            sql = ResolveTemplates(sql, ctx);

            var resultMode = (p.TryGetValue("resultMode", out var rmObj) ? Convert.ToString(rmObj) : null) ?? "NonQuery";
            // NonQuery | Scalar | DataTable

            // 3) parámetros opcionales (diccionario k→v)
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (p.TryGetValue("parameters", out var parObj))
            {
                if (parObj is Newtonsoft.Json.Linq.JObject jobj)
                    dict = jobj.ToObject<Dictionary<string, object>>(Newtonsoft.Json.JsonSerializer.CreateDefault());
                else if (parObj is Dictionary<string, object> d2)
                    dict = new Dictionary<string, object>(d2, StringComparer.OrdinalIgnoreCase);
            }

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                // templating y tipado en cada valor
                foreach (var kv in dict)
                {
                    var raw = kv.Value == null ? "" : Convert.ToString(kv.Value);
                    var resolved = ResolveTemplates(raw, ctx);
                    var coerced = Coerce(resolved);
                    cmd.Parameters.AddWithValue("@" + kv.Key, coerced ?? DBNull.Value);
                }

                await cn.OpenAsync(ct);

                if (string.Equals(resultMode, "Scalar", StringComparison.OrdinalIgnoreCase))
                {
                    var scalar = await cmd.ExecuteScalarAsync(ct);
                    ctx.Estado["sql.scalar"] = scalar;
                    ctx.Log("[SQL] scalar = " + (scalar == null ? "null" : scalar.ToString()));
                }
                else if (string.Equals(resultMode, "DataTable", StringComparison.OrdinalIgnoreCase))
                {
                    using (var reader = await cmd.ExecuteReaderAsync(ct))
                    {
                        var dt = new DataTable();
                        dt.Load(reader);

                        // Guarda el primer registro también como diccionario simple para IF: ${sql.first.Campo}
                        var first = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        if (dt.Rows.Count > 0)
                        {
                            var r = dt.Rows[0];
                            foreach (DataColumn c in dt.Columns) first[c.ColumnName] = r[c];
                        }
                        ctx.Estado["sql.table"] = dt;      // objeto runtime
                        ctx.Estado["sql.first"] = first;   // diccionario
                        ctx.Log($"[SQL] {dt.Rows.Count} filas (DataTable)");
                    }
                }
                else
                {
                    var rows = await cmd.ExecuteNonQueryAsync(ct);
                    ctx.Estado["sql.rows"] = rows;
                    ctx.Log("[SQL] ejecutado. filas=" + rows);
                }
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
