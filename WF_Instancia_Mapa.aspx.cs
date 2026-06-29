using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Instancia_Mapa : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "INSTANCIAS" };

        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Ejecuciones"; } catch { }

            if (!IsPostBack)
            {
                long instanciaId;
                if (!long.TryParse(Convert.ToString(Request.QueryString["id"]), out instanciaId) || instanciaId <= 0)
                {
                    MostrarError("Falta el parámetro id de instancia. Abrí el mapa desde el listado de instancias.");
                    return;
                }

                lnkVolver.NavigateUrl = "WF_Instancias.aspx?inst=" + instanciaId;

                CargarMapa(instanciaId);
            }
        }

        private void CargarMapa(long instanciaId)
        {
            try
            {
                var info = CargarInstancia(instanciaId);
                if (info == null)
                {
                    MostrarError("No se encontró la instancia " + H(instanciaId) + ".");
                    return;
                }

                var logs = CargarLogs(instanciaId);
                var tareas = CargarTareas(instanciaId);
                var nodos = LeerNodos(info.JsonDef);

                pnlContenido.Visible = true;
                pnlError.Visible = false;

                litEstadoChip.Text = RenderEstadoChip(info.Estado);
                litResumen.Text = RenderResumen(info, logs, tareas, nodos);
                litMapa.Text = RenderCaminoEjecutado(nodos, logs, tareas);
                litTareas.Text = RenderTareas(tareas);
                litNoEjecutados.Text = RenderNoEjecutados(nodos, logs, tareas);
                litLogs.Text = RenderLogs(logs);
            }
            catch (Exception ex)
            {
                MostrarError("Error al generar el mapa de instancia: " + H(ex.Message));
            }
        }

        private InstanciaInfo CargarInstancia(long instanciaId)
        {
            const string sql = @"
SELECT TOP (1)
    i.Id,
    i.WF_DefinicionId,
    i.Estado,
    i.FechaInicio,
    i.FechaFin,
    i.DatosContexto,
    d.[Key] AS DefKey,
    d.Codigo,
    d.Nombre,
    d.JsonDef
FROM dbo.WF_Instancia i
INNER JOIN dbo.WF_Definicion d ON d.Id = i.WF_DefinicionId
WHERE i.Id = @Id;";

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cn.Open();

                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read()) return null;

                    return new InstanciaInfo
                    {
                        Id = ToLong(rd["Id"]),
                        DefinicionId = ToInt(rd["WF_DefinicionId"]),
                        Estado = S(rd["Estado"]),
                        FechaInicio = D(rd["FechaInicio"]),
                        FechaFin = D(rd["FechaFin"]),
                        DatosContexto = S(rd["DatosContexto"]),
                        DefKey = S(rd["DefKey"]),
                        Codigo = S(rd["Codigo"]),
                        Nombre = S(rd["Nombre"]),
                        JsonDef = S(rd["JsonDef"])
                    };
                }
            }
        }

        private List<LogRow> CargarLogs(long instanciaId)
        {
            const string sql = @"
SELECT TOP (1000)
    Id,
    FechaLog,
    Nivel,
    Mensaje,
    NodoId,
    NodoTipo
FROM dbo.WF_InstanciaLog
WHERE WF_InstanciaId = @Id
ORDER BY Id ASC;";

            var list = new List<LogRow>();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cn.Open();

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        list.Add(new LogRow
                        {
                            Id = ToLong(rd["Id"]),
                            FechaLog = D(rd["FechaLog"]),
                            Nivel = S(rd["Nivel"]),
                            Mensaje = S(rd["Mensaje"]),
                            NodoId = S(rd["NodoId"]),
                            NodoTipo = S(rd["NodoTipo"])
                        });
                    }
                }
            }

            return list;
        }

        private List<TareaRow> CargarTareas(long instanciaId)
        {
            const string sql = @"
SELECT
    Id,
    NodoId,
    NodoTipo,
    Titulo,
    RolDestino,
    UsuarioAsignado,
    AsignadoA,
    Estado,
    Resultado,
    FechaCreacion,
    FechaCierre
FROM dbo.WF_Tarea
WHERE WF_InstanciaId = @Id
ORDER BY Id ASC;";

            var list = new List<TareaRow>();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cn.Open();

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        list.Add(new TareaRow
                        {
                            Id = ToLong(rd["Id"]),
                            NodoId = S(rd["NodoId"]),
                            NodoTipo = S(rd["NodoTipo"]),
                            Titulo = S(rd["Titulo"]),
                            RolDestino = S(rd["RolDestino"]),
                            UsuarioAsignado = S(rd["UsuarioAsignado"]),
                            AsignadoA = S(rd["AsignadoA"]),
                            Estado = S(rd["Estado"]),
                            Resultado = S(rd["Resultado"]),
                            FechaCreacion = D(rd["FechaCreacion"]),
                            FechaCierre = D(rd["FechaCierre"])
                        });
                    }
                }
            }

            return list;
        }

        private List<NodoRow> LeerNodos(string jsonDef)
        {
            var list = new List<NodoRow>();

            if (string.IsNullOrWhiteSpace(jsonDef))
                return list;

            try
            {
                var root = JObject.Parse(jsonDef);
                var nodes = root["Nodes"] as JObject;
                if (nodes == null) return list;

                foreach (var prop in nodes.Properties())
                {
                    var jo = prop.Value as JObject;
                    if (jo == null) continue;

                    var id = Convert.ToString(jo["Id"] ?? prop.Name);
                    var type = Convert.ToString(jo["Type"] ?? "");
                    var label = Convert.ToString(jo["Label"] ?? "");
                    var pars = jo["Parameters"] as JObject;

                    var x = ToNullableDouble(pars?["position"]?["x"]);
                    var y = ToNullableDouble(pars?["position"]?["y"]);

                    list.Add(new NodoRow
                    {
                        Id = id,
                        Tipo = type,
                        Label = string.IsNullOrWhiteSpace(label) ? FriendlyTipo(type) : label,
                        X = x,
                        Y = y,
                        Detalle = LeerDetalleNodo(type, pars)
                    });
                }

                return list
                    .OrderBy(n => n.Y.HasValue ? 0 : 1)
                    .ThenBy(n => n.Y ?? 0)
                    .ThenBy(n => n.X ?? 0)
                    .ThenBy(n => n.Id)
                    .ToList();
            }
            catch
            {
                return list;
            }
        }

        private string LeerDetalleNodo(string tipo, JObject pars)
        {
            if (pars == null) return "";

            if (string.Equals(tipo, "human.task", StringComparison.OrdinalIgnoreCase))
            {
                var rol = Convert.ToString(pars["rol"] ?? "");
                var usuario = Convert.ToString(pars["usuarioAsignado"] ?? "");
                var titulo = Convert.ToString(pars["titulo"] ?? "");
                var dest = !string.IsNullOrWhiteSpace(usuario) ? usuario : rol;
                return JoinParts(titulo, string.IsNullOrWhiteSpace(dest) ? null : "Destino: " + dest);
            }

            if (string.Equals(tipo, "control.if", StringComparison.OrdinalIgnoreCase))
            {
                var expression = Convert.ToString(pars["expression"] ?? "");
                if (!string.IsNullOrWhiteSpace(expression)) return expression;

                var rules = pars["rules"] as JArray;
                if (rules != null && rules.Count > 0)
                {
                    var mode = Convert.ToString(pars["rulesMode"] ?? "ALL");
                    return mode + " / " + rules.Count + " regla(s)";
                }

                var field = Convert.ToString(pars["field"] ?? "");
                var op = Convert.ToString(pars["op"] ?? "");
                var value = Convert.ToString(pars["value"] ?? "");
                return JoinParts(field, op, value);
            }

            if (string.Equals(tipo, "util.logger", StringComparison.OrdinalIgnoreCase))
                return JoinParts(Convert.ToString(pars["level"] ?? ""), Convert.ToString(pars["message"] ?? ""));

            if (string.Equals(tipo, "http.request", StringComparison.OrdinalIgnoreCase))
                return JoinParts(Convert.ToString(pars["method"] ?? ""), Convert.ToString(pars["url"] ?? ""));

            if (string.Equals(tipo, "data.sql", StringComparison.OrdinalIgnoreCase))
                return Convert.ToString(pars["commandText"] ?? "");

            return "";
        }

        private string RenderResumen(InstanciaInfo info, List<LogRow> logs, List<TareaRow> tareas, List<NodoRow> nodos)
        {
            var sb = new StringBuilder();
            sb.Append("<div class='ws-kv'>");
            sb.Append(Kv("Instancia", "#" + H(info.Id)));
            sb.Append(Kv("Estado", RenderEstadoChip(info.Estado)));
            sb.Append(Kv("Definición", "#" + H(info.DefinicionId) + " - " + H(FirstNonEmpty(info.DefKey, info.Codigo, info.Nombre))));
            sb.Append(Kv("Nombre", H(info.Nombre)));
            sb.Append(Kv("Inicio", H(Fmt(info.FechaInicio))));
            sb.Append(Kv("Fin", H(Fmt(info.FechaFin))));
            sb.Append(Kv("Nodos definidos", H(nodos.Count)));
            sb.Append(Kv("Logs", H(logs.Count)));
            sb.Append(Kv("Tareas", H(tareas.Count)));
            sb.Append("</div>");
            sb.Append("<div class='mt-3 d-flex flex-wrap gap-2'>");
            sb.Append("<a class='btn btn-sm btn-outline-primary' href='WF_Instancias.aspx?inst=" + H(info.Id) + "'>Ver datos/logs</a>");
            sb.Append("<a class='btn btn-sm btn-outline-secondary' href='WorkflowUI.aspx?defId=" + H(info.DefinicionId) + "'>Abrir definición</a>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private string RenderCaminoEjecutado(List<NodoRow> nodos, List<LogRow> logs, List<TareaRow> tareas)
        {
            var nodeById = nodos.ToDictionary(n => n.Id ?? "", n => n, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<NodoRender>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var log in logs)
            {
                var nodoId = NormalizarNodoId(log.NodoId);
                if (string.IsNullOrWhiteSpace(nodoId))
                    nodoId = ExtraerNodoIdDesdeMensaje(log.Mensaje);

                if (string.IsNullOrWhiteSpace(nodoId)) continue;
                if (!added.Add(nodoId)) continue;

                NodoRow n;
                if (!nodeById.TryGetValue(nodoId, out n))
                {
                    n = new NodoRow { Id = nodoId, Tipo = log.NodoTipo, Label = FriendlyTipo(log.NodoTipo) };
                }

                ordered.Add(new NodoRender { Nodo = n, PrimeraFecha = log.FechaLog });
            }

            foreach (var t in tareas)
            {
                var nodoId = NormalizarNodoId(t.NodoId);
                if (string.IsNullOrWhiteSpace(nodoId) || !added.Add(nodoId)) continue;

                NodoRow n;
                if (!nodeById.TryGetValue(nodoId, out n))
                    n = new NodoRow { Id = nodoId, Tipo = t.NodoTipo, Label = FriendlyTipo(t.NodoTipo) };

                ordered.Add(new NodoRender { Nodo = n, PrimeraFecha = t.FechaCreacion });
            }

            ordered = ordered
                .OrderBy(x => x.PrimeraFecha ?? DateTime.MaxValue)
                .ThenBy(x => x.Nodo.Id)
                .ToList();

            if (ordered.Count == 0)
                return "<div class='alert alert-light border mb-0'>Todavía no hay nodos ejecutados con NodoId registrado en logs o tareas.</div>";

            var sb = new StringBuilder();
            sb.Append("<div class='ws-flow'>");

            foreach (var item in ordered)
            {
                var n = item.Nodo;
                var logsNodo = logs.Where(l => string.Equals(NormalizarNodoId(l.NodoId), n.Id, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(ExtraerNodoIdDesdeMensaje(l.Mensaje), n.Id, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                var tareasNodo = tareas.Where(t => string.Equals(NormalizarNodoId(t.NodoId), n.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                var tieneError = logsNodo.Any(l => string.Equals(l.Nivel, "Error", StringComparison.OrdinalIgnoreCase) || Contiene(l.Mensaje, "error"));
                var tienePendiente = tareasNodo.Any(t => string.Equals(t.Estado, "Pendiente", StringComparison.OrdinalIgnoreCase));

                var cls = tieneError ? "error" : (tienePendiente ? "warn" : "ok");
                var icon = tieneError ? "!" : (tienePendiente ? "…" : "✓");

                sb.Append("<div class='ws-step'>");
                sb.Append("<div class='ws-dot ws-dot-" + cls + "'>" + icon + "</div>");
                sb.Append("<div class='ws-node-card ws-node-card-" + cls + "'>");
                sb.Append("<div class='d-flex align-items-start justify-content-between gap-2'>");
                sb.Append("<div>");
                sb.Append("<div class='fw-bold'>" + H(n.Id) + " · " + H(n.Label) + "</div>");
                sb.Append("<div class='small ws-muted'>" + H(n.Tipo) + "</div>");
                if (!string.IsNullOrWhiteSpace(n.Detalle))
                    sb.Append("<div class='small mt-1'>" + H(Short(n.Detalle, 180)) + "</div>");
                sb.Append("</div>");
                sb.Append("<div>" + RenderNodeBadge(cls, tienePendiente) + "</div>");
                sb.Append("</div>");

                var detalle = RenderDetalleNodoEjecutado(n, logsNodo, tareasNodo);
                if (!string.IsNullOrWhiteSpace(detalle))
                    sb.Append("<div class='mt-2 small'>" + detalle + "</div>");

                sb.Append("</div>");
                sb.Append("</div>");
            }

            sb.Append("</div>");
            return sb.ToString();
        }

        private string RenderDetalleNodoEjecutado(NodoRow n, List<LogRow> logsNodo, List<TareaRow> tareasNodo)
        {
            var sb = new StringBuilder();

            foreach (var t in tareasNodo)
            {
                sb.Append("<div class='mb-1'>");
                sb.Append("Tarea <a href='WF_Tarea_Detalle.aspx?id=" + H(t.Id) + "'>#" + H(t.Id) + "</a>");
                sb.Append(" · " + H(FirstNonEmpty(t.Titulo, "Tarea humana")));
                sb.Append(" · " + H(FirstNonEmpty(t.Estado, "Sin estado")));
                if (!string.IsNullOrWhiteSpace(t.Resultado)) sb.Append(" · Resultado: <strong>" + H(t.Resultado) + "</strong>");
                var dest = FirstNonEmpty(t.AsignadoA, t.UsuarioAsignado, t.RolDestino);
                if (!string.IsNullOrWhiteSpace(dest)) sb.Append(" · Destino: " + H(dest));
                sb.Append("</div>");
            }

            var logsImportantes = logsNodo
                .Where(l => EsLogImportante(l.Mensaje, n.Tipo) || string.Equals(l.Nivel, "Error", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            foreach (var l in logsImportantes)
            {
                sb.Append("<div class='text-muted'>");
                sb.Append(H(Fmt(l.FechaLog)) + " · " + H(Short(l.Mensaje, 240)));
                sb.Append("</div>");
            }

            return sb.ToString();
        }

        private string RenderTareas(List<TareaRow> tareas)
        {
            if (tareas == null || tareas.Count == 0)
                return "<div class='ws-muted small'>La instancia no tiene tareas humanas registradas.</div>";

            var sb = new StringBuilder();
            sb.Append("<div class='table-responsive'><table class='table table-sm table-hover ws-grid mb-0'>");
            sb.Append("<thead><tr><th>Id</th><th>Nodo</th><th>Destino</th><th>Estado</th><th>Resultado</th><th>Cierre</th></tr></thead><tbody>");

            foreach (var t in tareas)
            {
                var dest = FirstNonEmpty(t.AsignadoA, t.UsuarioAsignado, t.RolDestino, "-");
                sb.Append("<tr>");
                sb.Append("<td><a href='WF_Tarea_Detalle.aspx?id=" + H(t.Id) + "'>#" + H(t.Id) + "</a></td>");
                sb.Append("<td><div class='fw-semibold'>" + H(t.NodoId) + "</div><div class='small ws-muted'>" + H(FirstNonEmpty(t.Titulo, t.NodoTipo)) + "</div></td>");
                sb.Append("<td>" + H(dest) + "</td>");
                sb.Append("<td>" + H(t.Estado) + "</td>");
                sb.Append("<td>" + H(FirstNonEmpty(t.Resultado, "-")) + "</td>");
                sb.Append("<td>" + H(Fmt(t.FechaCierre ?? t.FechaCreacion)) + "</td>");
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></div>");
            return sb.ToString();
        }

        private string RenderNoEjecutados(List<NodoRow> nodos, List<LogRow> logs, List<TareaRow> tareas)
        {
            if (nodos == null || nodos.Count == 0)
                return "<div class='ws-muted small'>No se pudo leer el grafo de la definición.</div>";

            var ejecutados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in logs)
            {
                var id = NormalizarNodoId(l.NodoId);
                if (string.IsNullOrWhiteSpace(id)) id = ExtraerNodoIdDesdeMensaje(l.Mensaje);
                if (!string.IsNullOrWhiteSpace(id)) ejecutados.Add(id);
            }
            foreach (var t in tareas)
            {
                var id = NormalizarNodoId(t.NodoId);
                if (!string.IsNullOrWhiteSpace(id)) ejecutados.Add(id);
            }

            var noEj = nodos.Where(n => !ejecutados.Contains(n.Id)).ToList();
            if (noEj.Count == 0)
                return "<div class='alert alert-light border mb-0'>Todos los nodos definidos aparecen como ejecutados o asociados a tarea.</div>";

            var sb = new StringBuilder();
            sb.Append("<div class='d-flex flex-wrap gap-2'>");
            foreach (var n in noEj)
            {
                sb.Append("<span class='ws-chip ws-chip-muted' title='" + H(n.Tipo) + "'>" + H(n.Id) + " · " + H(Short(n.Label, 40)) + "</span>");
            }
            sb.Append("</div>");
            sb.Append("<div class='ws-muted small mt-2'>Esto es normal cuando la instancia tomó una rama y dejó otras sin recorrer.</div>");
            return sb.ToString();
        }

        private string RenderLogs(List<LogRow> logs)
        {
            if (logs == null || logs.Count == 0)
                return "<div class='ws-muted small'>No hay logs registrados para esta instancia.</div>";

            var sb = new StringBuilder();
            sb.Append("<pre class='ws-pre mb-0'>");
            foreach (var l in logs.Take(300))
            {
                sb.Append(H(Fmt(l.FechaLog)));
                sb.Append(" [" + H(l.Nivel) + "] ");
                if (!string.IsNullOrWhiteSpace(l.NodoId))
                    sb.Append("(" + H(l.NodoId) + " / " + H(l.NodoTipo) + ") ");
                sb.Append(H(l.Mensaje));
                sb.Append("\r\n");
            }
            if (logs.Count > 300)
                sb.Append("\r\n... se muestran los primeros 300 logs de " + H(logs.Count) + ".");
            sb.Append("</pre>");
            return sb.ToString();
        }

        private string RenderEstadoChip(string estado)
        {
            var e = estado ?? "";
            var cls = "ws-chip-muted";
            if (e.Equals("Finalizado", StringComparison.OrdinalIgnoreCase)) cls = "ws-chip-ok";
            else if (e.Equals("Error", StringComparison.OrdinalIgnoreCase)) cls = "ws-chip-error";
            else if (e.Equals("EnCurso", StringComparison.OrdinalIgnoreCase) || e.Equals("Iniciado", StringComparison.OrdinalIgnoreCase)) cls = "ws-chip-warn";
            return "<span class='ws-chip " + cls + "'>" + H(string.IsNullOrWhiteSpace(e) ? "Sin estado" : e) + "</span>";
        }

        private string RenderNodeBadge(string cls, bool pendiente)
        {
            if (cls == "error") return "<span class='ws-chip ws-chip-error'>Error</span>";
            if (pendiente) return "<span class='ws-chip ws-chip-warn'>Pendiente</span>";
            return "<span class='ws-chip ws-chip-ok'>Ejecutado</span>";
        }

        private void MostrarError(string msg)
        {
            pnlContenido.Visible = false;
            pnlError.Visible = true;
            litError.Text = msg;
        }

        private static string Kv(string k, string v)
        {
            return "<div class='ws-muted'>" + H(k) + "</div><div>" + (string.IsNullOrWhiteSpace(v) ? "-" : v) + "</div>";
        }

        private static bool EsLogImportante(string mensaje, string tipoNodo)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return false;
            if (Contiene(mensaje, "[If]") || Contiene(mensaje, "human.task") || Contiene(mensaje, "End") || Contiene(mensaje, "error")) return true;
            if (Contiene(mensaje, "resultado") || Contiene(mensaje, "tareaId") || Contiene(mensaje, "Finalizado")) return true;
            if (string.Equals(tipoNodo, "control.if", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ExtraerNodoIdDesdeMensaje(string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return "";

            var m = Regex.Match(mensaje, @"nodoId\s*=\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(mensaje, @"nodo\s+([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;

            return "";
        }

        private static string NormalizarNodoId(string s)
        {
            return (s ?? "").Trim();
        }

        private static string FriendlyTipo(string tipo)
        {
            switch ((tipo ?? "").Trim().ToLowerInvariant())
            {
                case "util.start": return "Inicio";
                case "util.end": return "Fin";
                case "control.if": return "Condición";
                case "human.task": return "Tarea humana";
                case "util.notify": return "Notificación";
                case "http.request": return "Solicitud HTTP";
                case "data.sql": return "Consulta SQL";
                case "util.logger": return "Logger";
                case "control.delay": return "Demora";
                case "control.retry": return "Reintentar";
                case "file.read": return "Leer archivo";
                case "file.write": return "Escribir archivo";
                case "util.subflow": return "Subflujo";
                default: return string.IsNullOrWhiteSpace(tipo) ? "Nodo" : tipo;
            }
        }

        private static string JoinParts(params string[] parts)
        {
            return string.Join(" · ", (parts ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        private static string FirstNonEmpty(params string[] parts)
        {
            foreach (var p in parts ?? new string[0])
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            return "";
        }

        private static bool Contiene(string s, string fragmento)
        {
            return (s ?? "").IndexOf(fragmento ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Short(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static string Fmt(DateTime? dt)
        {
            return dt.HasValue ? dt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "-";
        }

        private static string H(object value)
        {
            return HttpUtility.HtmlEncode(Convert.ToString(value ?? ""));
        }

        private static string S(object value)
        {
            return value == null || value == DBNull.Value ? "" : Convert.ToString(value);
        }

        private static int ToInt(object value)
        {
            int v;
            return int.TryParse(S(value), out v) ? v : 0;
        }

        private static long ToLong(object value)
        {
            long v;
            return long.TryParse(S(value), out v) ? v : 0L;
        }

        private static DateTime? D(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            try { return Convert.ToDateTime(value); } catch { return null; }
        }

        private static double? ToNullableDouble(JToken tok)
        {
            if (tok == null) return null;
            double d;
            return double.TryParse(Convert.ToString(tok), out d) ? (double?)d : null;
        }

        private class InstanciaInfo
        {
            public long Id { get; set; }
            public int DefinicionId { get; set; }
            public string Estado { get; set; }
            public DateTime? FechaInicio { get; set; }
            public DateTime? FechaFin { get; set; }
            public string DatosContexto { get; set; }
            public string DefKey { get; set; }
            public string Codigo { get; set; }
            public string Nombre { get; set; }
            public string JsonDef { get; set; }
        }

        private class LogRow
        {
            public long Id { get; set; }
            public DateTime? FechaLog { get; set; }
            public string Nivel { get; set; }
            public string Mensaje { get; set; }
            public string NodoId { get; set; }
            public string NodoTipo { get; set; }
        }

        private class TareaRow
        {
            public long Id { get; set; }
            public string NodoId { get; set; }
            public string NodoTipo { get; set; }
            public string Titulo { get; set; }
            public string RolDestino { get; set; }
            public string UsuarioAsignado { get; set; }
            public string AsignadoA { get; set; }
            public string Estado { get; set; }
            public string Resultado { get; set; }
            public DateTime? FechaCreacion { get; set; }
            public DateTime? FechaCierre { get; set; }
        }

        private class NodoRow
        {
            public string Id { get; set; }
            public string Tipo { get; set; }
            public string Label { get; set; }
            public string Detalle { get; set; }
            public double? X { get; set; }
            public double? Y { get; set; }
        }

        private class NodoRender
        {
            public NodoRow Nodo { get; set; }
            public DateTime? PrimeraFecha { get; set; }
        }
    }
}
