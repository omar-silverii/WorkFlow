using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.IO;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Instancias : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "INSTANCIAS" };

        private static long _instanciaActualId = 0;

        // Modo deep-link (?inst=xxxx)
        private int? InstIdFromQuery
        {
            get
            {
                object o = ViewState["InstIdFromQuery"];
                if (o == null) return null;
                int v;
                return int.TryParse(Convert.ToString(o), out v) ? (int?)v : null;
            }
            set { ViewState["InstIdFromQuery"] = value; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Ejecuciones"; } catch { }

            if (!IsPostBack)
            {
                // 1) Leer ?inst=xxxx (si viene, manda sobre defId)
                int instQs;
                if (int.TryParse(Convert.ToString(Request.QueryString["inst"]), out instQs) && instQs > 0)
                    InstIdFromQuery = instQs;
                else
                    InstIdFromQuery = null;

                BindDefiniciones();

                ddlEstado.SelectedValue = "";
                chkMostrarFinalizados.Checked = true;

                // 2) Si hay inst, forzar la definición real (ignorar defId si vino mal)
                if (InstIdFromQuery.HasValue)
                {
                    int defReal = GetDefIdByInstancia(InstIdFromQuery.Value);
                    if (defReal > 0)
                    {
                        SetDefPicker(defReal);
                    }

                    // Para que quede visible el target
                    txtBuscar.Text = InstIdFromQuery.Value.ToString();
                }

                MarcarEstadoPills();
                CargarInstancias();

                // 3) Auto abrir datos/logs del deep-link (solo en el primer load)
                if (InstIdFromQuery.HasValue)
                {
                    MostrarDatos(InstIdFromQuery.Value);
                    MostrarLogs(InstIdFromQuery.Value);
                }

                // al entrar, ocultamos ambas tarjetas (no hay selección)
                // OJO: si vino inst y hay datos, MostrarDatos/Logs vuelve a prender lo que corresponde
                // y ShowDocs/BindDocAudit manejan visibilidad de cards
                // Si no hay docs/auditoría, quedan ocultas.
                if (!InstIdFromQuery.HasValue)
                {
                    pnlDocsCard.Visible = false;
                    pnlDocAuditCard.Visible = false;
                }
            }
        }

        private int GetDefIdByInstancia(int instId)
        {
            const string sql = "SELECT WF_DefinicionId FROM WF_Instancia WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = instId;
                cn.Open();
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return 0;

                int defId;
                return int.TryParse(Convert.ToString(v), out defId) ? defId : 0;
            }
        }

        private sealed class DefUiRow
        {
            public int Id { get; set; }
            public string Key { get; set; }
            public string Codigo { get; set; }
            public string Nombre { get; set; }
            public string Display { get; set; }
            public DateTime? UltimaEjecucion { get; set; }
            public int InstanciasCount { get; set; }
        }

        private static string Nz(object v)
        {
            return v == null || v == DBNull.Value ? "" : Convert.ToString(v).Trim();
        }

        private static bool HasColumn(IDataRecord rd, string columnName)
        {
            for (int i = 0; i < rd.FieldCount; i++)
                if (string.Equals(rd.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string BuildDefDisplay(string key, string codigo, string nombre)
        {
            string cod = !string.IsNullOrWhiteSpace(codigo) ? codigo.Trim() : (key ?? "").Trim();
            string nom = (nombre ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(cod) && !string.IsNullOrWhiteSpace(nom))
                return cod + " — " + nom;

            if (!string.IsNullOrWhiteSpace(cod))
                return cod;

            return nom;
        }

        private DefUiRow ReadDef(SqlDataReader rd)
        {
            var key = Nz(rd["DefKey"]);
            var codigo = Nz(rd["Codigo"]);
            var nombre = Nz(rd["Nombre"]);

            DateTime ultima;
            DateTime? ultimaEjecucion = null;
            if (HasColumn(rd, "UltimaEjecucion") && rd["UltimaEjecucion"] != DBNull.Value && DateTime.TryParse(Convert.ToString(rd["UltimaEjecucion"]), out ultima))
                ultimaEjecucion = ultima;

            int instanciasCount = 0;
            if (HasColumn(rd, "InstanciasCount") && rd["InstanciasCount"] != DBNull.Value)
                int.TryParse(Convert.ToString(rd["InstanciasCount"]), out instanciasCount);

            return new DefUiRow
            {
                Id = Convert.ToInt32(rd["Id"]),
                Key = key,
                Codigo = codigo,
                Nombre = nombre,
                Display = BuildDefDisplay(key, codigo, nombre),
                UltimaEjecucion = ultimaEjecucion,
                InstanciasCount = instanciasCount
            };
        }

        private static string Enc(string value)
        {
            return HttpUtility.HtmlEncode(value ?? "");
        }

        private static string Attr(string value)
        {
            return HttpUtility.HtmlAttributeEncode(value ?? "");
        }

        private void BindDefiniciones()
        {
            var defs = new List<DefUiRow>();

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
SELECT d.Id,
       ISNULL(d.[Key], '') AS DefKey,
       ISNULL(d.Codigo, '') AS Codigo,
       ISNULL(d.Nombre, '') AS Nombre,
       MAX(i.FechaInicio) AS UltimaEjecucion,
       COUNT(i.Id) AS InstanciasCount
FROM dbo.WF_Definicion d
LEFT JOIN dbo.WF_Instancia i ON i.WF_DefinicionId = d.Id
GROUP BY d.Id, d.[Key], d.Codigo, d.Nombre
ORDER BY MAX(i.FechaInicio) DESC, d.Id DESC;", cn))
            {
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                        defs.Add(ReadDef(rd));
                }
            }

            var sb = new StringBuilder();
            foreach (var d in defs)
            {
                if (string.IsNullOrWhiteSpace(d.Display))
                    continue;

                string codigo = !string.IsNullOrWhiteSpace(d.Codigo) ? d.Codigo : d.Key;
                string nombre = d.Nombre;
                string ultimaTexto = d.UltimaEjecucion.HasValue ? d.UltimaEjecucion.Value.ToString("dd/MM/yyyy HH:mm") : "";
                string badgeTexto = d.UltimaEjecucion.HasValue ? "Última ejecución" : "Sin ejecución";
                string badgeCss = d.UltimaEjecucion.HasValue ? "ws-def-badge" : "ws-def-badge ws-def-badge-empty";
                string search = (d.Display + " " + codigo + " " + nombre + " " + d.Id.ToString()).Trim();

                sb.Append("<button type=\"button\" class=\"ws-def-item\" role=\"option\"")
                  .Append(" data-id=\"").Append(d.Id.ToString()).Append("\"")
                  .Append(" data-display=\"").Append(Attr(d.Display)).Append("\"")
                  .Append(" data-code=\"").Append(Attr(codigo)).Append("\"")
                  .Append(" data-name=\"").Append(Attr(nombre)).Append("\"")
                  .Append(" data-search=\"").Append(Attr(search)).Append("\">")
                  .Append("<span class=\"ws-def-code\">").Append(Enc(codigo)).Append("</span>")
                  .Append("<span class=\"ws-def-name\">").Append(Enc(nombre)).Append("</span>")
                  .Append("<span class=\"ws-def-meta\">")
                  .Append("<span class=\"").Append(badgeCss).Append("\">").Append(Enc(badgeTexto)).Append("</span>");

                if (!string.IsNullOrWhiteSpace(ultimaTexto))
                    sb.Append("<span class=\"ws-def-date\">&#128336; ").Append(Enc(ultimaTexto)).Append("</span>");

                sb.Append("</span></button>");
            }
            litDefResults.Text = sb.ToString();

            int selectedDefId = 0;

            if (InstIdFromQuery.HasValue)
                selectedDefId = GetDefIdByInstancia(InstIdFromQuery.Value);

            if (selectedDefId <= 0)
            {
                var qsDef = Request.QueryString["defId"];
                int.TryParse(Convert.ToString(qsDef), out selectedDefId);
            }

            if (selectedDefId <= 0 && defs.Count > 0)
                selectedDefId = defs[0].Id;

            if (selectedDefId > 0)
                SetDefPicker(selectedDefId, defs);
        }

        private void SetDefPicker(int defId)
        {
            SetDefPicker(defId, null);
        }

        private void SetDefPicker(int defId, List<DefUiRow> cache)
        {
            if (defId <= 0)
                return;

            DefUiRow def = null;
            if (cache != null)
                def = cache.FirstOrDefault(x => x.Id == defId);

            if (def == null)
                def = GetDefById(defId);

            if (def == null)
                return;

            hidDefId.Value = def.Id.ToString();
            txtDef.Text = def.Display;
        }

        private DefUiRow GetDefById(int defId)
        {
            if (defId <= 0) return null;

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
SELECT TOP (1) d.Id,
       ISNULL(d.[Key], '') AS DefKey,
       ISNULL(d.Codigo, '') AS Codigo,
       ISNULL(d.Nombre, '') AS Nombre,
       MAX(i.FechaInicio) AS UltimaEjecucion,
       COUNT(i.Id) AS InstanciasCount
FROM dbo.WF_Definicion d
LEFT JOIN dbo.WF_Instancia i ON i.WF_DefinicionId = d.Id
WHERE d.Id=@Id
GROUP BY d.Id, d.[Key], d.Codigo, d.Nombre;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                        return ReadDef(rd);
                }
            }

            return null;
        }

        private int ResolverDefIdPorTexto(string texto)
        {
            texto = (texto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(texto))
                return 0;

            int id;
            if (int.TryParse(texto, out id) && GetDefById(id) != null)
                return id;

            string codigoEscrito = texto;
            int sep = texto.IndexOf(" — ", StringComparison.Ordinal);
            if (sep > 0)
                codigoEscrito = texto.Substring(0, sep).Trim();

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
SELECT TOP (1) Id
FROM dbo.WF_Definicion
WHERE [Key] = @Texto
   OR Codigo = @Texto
   OR Nombre = @Texto
   OR [Key] = @Codigo
   OR Codigo = @Codigo
ORDER BY Id DESC;", cn))
            {
                cmd.Parameters.Add("@Texto", SqlDbType.NVarChar, 300).Value = texto;
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 300).Value = codigoEscrito;
                cn.Open();
                var v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(Convert.ToString(v), out id))
                    return id;
            }

            if (texto.Length < 2)
                return 0;

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
SELECT TOP (1) Id
FROM dbo.WF_Definicion
WHERE [Key] LIKE @Q
   OR Codigo LIKE @Q
   OR Nombre LIKE @Q
ORDER BY Id DESC;", cn))
            {
                cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 300).Value = "%" + texto + "%";
                cn.Open();
                var v = cmd.ExecuteScalar();
                if (v != null && v != DBNull.Value && int.TryParse(Convert.ToString(v), out id))
                    return id;
            }

            return 0;
        }

        private int GetDefIdSeleccionada(bool actualizarUi, bool mostrarAviso)
        {
            if (lblDefMsg != null)
                lblDefMsg.Text = "";

            int defId;
            if (int.TryParse(Convert.ToString(hidDefId.Value), out defId) && GetDefById(defId) != null)
            {
                if (actualizarUi)
                    SetDefPicker(defId);
                return defId;
            }

            defId = ResolverDefIdPorTexto(txtDef.Text);
            if (defId > 0)
            {
                if (actualizarUi)
                    SetDefPicker(defId);
                return defId;
            }

            if (mostrarAviso && lblDefMsg != null)
                lblDefMsg.Text = "No se encontró una definición con ese código o nombre.";

            return 0;
        }

        private string EstadoSeleccionado
        {
            get { return Convert.ToString(ddlEstado.SelectedValue ?? "").Trim(); }
        }

        private void MarcarEstadoPills()
        {
            lnkEstadoTodos.CssClass = "btn btn-outline-secondary";
            lnkEstadoEnCurso.CssClass = "btn btn-outline-primary";
            lnkEstadoError.CssClass = "btn btn-outline-danger";
            lnkEstadoFinalizado.CssClass = "btn btn-outline-success";

            var est = EstadoSeleccionado ?? "";
            if (string.IsNullOrWhiteSpace(est))
                lnkEstadoTodos.CssClass = "btn btn-secondary";
            else if (est.Equals("EnCurso", StringComparison.OrdinalIgnoreCase))
                lnkEstadoEnCurso.CssClass = "btn btn-primary";
            else if (est.Equals("Error", StringComparison.OrdinalIgnoreCase))
                lnkEstadoError.CssClass = "btn btn-danger";
            else if (est.Equals("Finalizado", StringComparison.OrdinalIgnoreCase))
                lnkEstadoFinalizado.CssClass = "btn btn-success";
        }

        protected void lnkEstado_Click(object sender, EventArgs e)
        {
            var l = sender as LinkButton;
            ddlEstado.SelectedValue = l?.CommandArgument ?? "";
            gvInst.PageIndex = 0;

            MarcarEstadoPills();
            CargarInstancias();
        }

        protected void txtDef_TextChanged(object sender, EventArgs e)
        {
            // Cambió la definición manualmente => ya no estamos en deep-link
            InstIdFromQuery = null;

            GetDefIdSeleccionada(true, true);
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected void ddlEstado_SelectedIndexChanged(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            MarcarEstadoPills();
            CargarInstancias();
        }

        protected void chkMostrarFinalizados_CheckedChanged(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected void btnRefrescar_Click(object sender, EventArgs e)
        {
            CargarInstancias();
        }

        protected void btnAbrirMapa_Click(object sender, EventArgs e)
        {
            lblAbrirMapaMsg.Text = "";

            int instId;
            if (!int.TryParse((txtInstanciaMapa.Text ?? "").Trim(), out instId) || instId <= 0)
            {
                lblAbrirMapaMsg.Text = "Ingresá un Id de instancia válido.";
                return;
            }

            int defId = GetDefIdByInstancia(instId);
            if (defId <= 0)
            {
                lblAbrirMapaMsg.Text = "No se encontró la instancia " + Server.HtmlEncode(instId.ToString()) + ".";
                return;
            }

            Response.Redirect("WF_Instancia_Mapa.aspx?id=" + instId, false);
            Context.ApplicationInstance.CompleteRequest();
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            // Búsqueda manual => salir de deep-link
            InstIdFromQuery = null;

            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        private void CargarInstancias()
        {
            int defId = GetDefIdSeleccionada(true, false);

            bool mostrarFinalizados = chkMostrarFinalizados.Checked;
            string estado = Convert.ToString(ddlEstado.SelectedValue ?? "").Trim();
            string q = (txtBuscar.Text ?? "").Trim();

            if (defId <= 0 && !InstIdFromQuery.HasValue)
            {
                gvInst.DataSource = new DataTable();
                gvInst.DataBind();
                return;
            }

            // ✅ Si estamos en deep-link por instancia, traer SOLO esa fila (Id exacto)
            if (InstIdFromQuery.HasValue && InstIdFromQuery.Value > 0)
            {
                string sqlExact = @"
SELECT TOP (1)
    Id,
    WF_DefinicionId,
    Estado,
    FechaInicio,
    FechaFin,
    DatosContexto
FROM WF_Instancia
WHERE Id = @InstId
ORDER BY Id DESC;";

                using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                using (var cmd = new SqlCommand(sqlExact, cn))
                {
                    cmd.Parameters.Add("@InstId", SqlDbType.Int).Value = InstIdFromQuery.Value;

                    var dt = new DataTable();
                    cn.Open();
                    dt.Load(cmd.ExecuteReader());

                    gvInst.DataSource = dt;
                    gvInst.DataBind();
                }

                return;
            }

            var where = new List<string>();
            where.Add("WF_DefinicionId = @DefId");

            if (!mostrarFinalizados)
                where.Add("Estado <> 'Finalizado'");

            if (!string.IsNullOrWhiteSpace(estado))
                where.Add("Estado = @Estado");

            bool qEsNumero = false;
            long dummy;
            if (!string.IsNullOrWhiteSpace(q))
                qEsNumero = long.TryParse(q, out dummy);

            if (!string.IsNullOrWhiteSpace(q))
            {
                if (qEsNumero)
                    where.Add("(CONVERT(varchar(30), Id) LIKE @Q OR DatosContexto LIKE @Q)");
                else
                    where.Add("(DatosContexto LIKE @Q)");
            }

            string sql = @"
SELECT TOP (500)
    Id,
    WF_DefinicionId,
    Estado,
    FechaInicio,
    FechaFin,
    DatosContexto
FROM WF_Instancia
WHERE " + string.Join(" AND ", where) + @"
ORDER BY Id DESC;";

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;

                if (!string.IsNullOrWhiteSpace(estado))
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = estado;

                if (!string.IsNullOrWhiteSpace(q))
                    cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value = "%" + q + "%";

                var dt = new DataTable();
                cn.Open();
                dt.Load(cmd.ExecuteReader());

                gvInst.DataSource = dt;
                gvInst.DataBind();
            }
        }

        protected async void btnCrearInst_Click(object sender, EventArgs e)
        {
            try
            {
                int defId = GetDefIdSeleccionada(true, true);
                if (defId <= 0) return;

                string usuario = (Context?.User?.Identity?.Name ?? "").Trim();
                var instId = await Runtime.WorkflowRuntime.CrearInstanciaYEjecutarAsync(defId, null, usuario);

                txtBuscar.Text = instId.ToString();
                CargarInstancias();
                MostrarDatos((int)instId);
                MostrarLogs((int)instId);
            }
            catch (Exception ex)
            {
                pnlLogs.Visible = true;
                pnlLogsEmpty.Visible = false;
                litLogs.Text = Server.HtmlEncode("Error al crear/ejecutar instancia:\n" + ex.Message);
            }
        }

        protected void gvInst_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvInst.PageIndex = e.NewPageIndex;
            CargarInstancias();
        }

        protected void gvInst_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.CommandName))
                return;

            int instId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out instId))
                return;

            _instanciaActualId = instId;

            if (e.CommandName == "Datos")
            {
                MostrarDatos(instId);
            }
            else if (e.CommandName == "Logs")
            {
                MostrarLogs(instId);
            }
            else if (e.CommandName == "Docs")
            {
                MostrarDocumentos(instId);
            }
        }

        void MostrarDocumentos(int instId)
        {
            string sql = "SELECT DatosContexto FROM WF_Instancia WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = instId;
                cn.Open();
                var val = cmd.ExecuteScalar();
                string json = val == DBNull.Value || val == null ? "" : Convert.ToString(val);

                _instanciaActualId = instId;

                pnlDatosCard.Visible = false;
                pnlLogsCard.Visible = false;

                pnlDatos.Visible = false;
                pnlDatosEmpty.Visible = false;

                pnlLogs.Visible = false;
                pnlLogsEmpty.Visible = false;

                BindDocsFromDatosContexto(json);
                BindDocAudit(instId);
            }
        }

        void MostrarDatos(int instId)
        {
            string sql = "SELECT DatosContexto FROM WF_Instancia WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = instId;
                cn.Open();
                var val = cmd.ExecuteScalar();
                string json = val == DBNull.Value || val == null ? "" : Convert.ToString(val);

                pnlDatosCard.Visible = true;
                pnlLogsCard.Visible = false;

                pnlDatos.Visible = true;
                pnlDatosEmpty.Visible = false;

                pnlLogs.Visible = false;
                pnlLogsEmpty.Visible = false;

                litDatos.Text = Server.HtmlEncode(PrettyJson(json));

                // docs desde DatosContexto
                BindDocsFromDatosContexto(json);

                // auditoría desde DB
                BindDocAudit(instId);
            }
        }

        // ✅ CAMBIO MÍNIMO: si viene envuelto {logs, estado}, mostrar solo estado
        string PrettyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                var tok = JToken.Parse(json);

                if (tok is JObject o && o["estado"] != null)
                    tok = o["estado"];

                return tok.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        private class DocUiRow
        {
            public string DocumentoId { get; set; }
            public string CarpetaId { get; set; }
            public string FicheroId { get; set; }
            public string Tipo { get; set; }
            public string ViewerUrl { get; set; }
            public string TareaId { get; set; }

            public string FileName { get; set; }
            public string Fecha { get; set; }
            public string Usuario { get; set; }

            public string StoredFileName { get; set; }
            public bool PuedeEliminar { get; set; }
        }

        private void BindDocsFromDatosContexto(string datosContextoJson)
        {
            var rows = new List<DocUiRow>();

            try
            {
                if (string.IsNullOrWhiteSpace(datosContextoJson))
                {
                    ShowDocs(rows);
                    return;
                }

                JObject root = null;
                try { root = JObject.Parse(datosContextoJson); } catch { root = null; }
                if (root == null)
                {
                    ShowDocs(rows);
                    return;
                }

                JObject bcase =
                    (root["estado"]?["biz"]?["case"] as JObject) ??
                    (root["biz"]?["case"] as JObject);

                if (bcase == null)
                {
                    ShowDocs(rows);
                    return;
                }

                var rootDoc = bcase["rootDoc"] as JObject;
                if (rootDoc != null)
                    rows.Add(ToRow(rootDoc));

                var atts = bcase["attachments"] as JArray;
                if (atts != null)
                {
                    foreach (var it in atts)
                    {
                        var jo = it as JObject;
                        if (jo == null) continue;

                        bool eliminado;
                        if (bool.TryParse(Convert.ToString(jo["eliminado"] ?? "false"), out eliminado) && eliminado)
                            continue;

                        var row = ToRow(jo);
                        row.StoredFileName = Convert.ToString(jo["storedFileName"] ?? "");
                        row.PuedeEliminar =
                            UsuarioPuedeEliminarAdjuntoInstancia() &&
                            !string.IsNullOrWhiteSpace(row.StoredFileName) &&
                            !string.IsNullOrWhiteSpace(row.TareaId);

                        rows.Add(row);
                    }
                }

                rows = rows
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.DocumentoId) ||
                        !string.IsNullOrWhiteSpace(x.ViewerUrl) ||
                        !string.IsNullOrWhiteSpace(x.FileName))
                    .ToList();

                ShowDocs(rows);
            }
            catch
            {
                ShowDocs(new List<DocUiRow>());
            }
        }

        private void ShowDocs(List<DocUiRow> rows)
        {
            if (litDocsTitle != null)
            {
                litDocsTitle.Text = _instanciaActualId > 0
                    ? ("Documentos de la instancia #" + _instanciaActualId.ToString())
                    : "Documentos (Caso)";
            }

            if (rows != null && rows.Count > 0)
            {
                pnlDocsCard.Visible = true;

                pnlDocs.Visible = true;
                pnlDocsEmpty.Visible = false;

                rptDocs.DataSource = rows;
                rptDocs.DataBind();
            }
            else
            {
                // ✅ ocultar card completo si no hay docs
                pnlDocsCard.Visible = false;

                pnlDocs.Visible = false;
                pnlDocsEmpty.Visible = false;

                rptDocs.DataSource = null;
                rptDocs.DataBind();
            }
        }

        private DocUiRow ToRow(JObject doc)
        {
            if (doc == null) return new DocUiRow();

            return new DocUiRow
            {
                DocumentoId = Convert.ToString(doc["documentoId"] ?? ""),
                CarpetaId = Convert.ToString(doc["carpetaId"] ?? ""),
                FicheroId = Convert.ToString(doc["ficheroId"] ?? ""),
                Tipo = Convert.ToString(doc["tipo"] ?? doc["mimeType"] ?? "Documento"),
                ViewerUrl = Convert.ToString(doc["viewerUrl"] ?? ""),
                TareaId = Convert.ToString(doc["tareaId"] ?? ""),

                FileName = Convert.ToString(doc["fileName"] ?? doc["nombre"] ?? ""),
                Fecha = Convert.ToString(doc["fecha"] ?? ""),
                Usuario = Convert.ToString(doc["usuario"] ?? ""),

                StoredFileName = Convert.ToString(doc["storedFileName"] ?? "")
            };
        }

        private bool UsuarioPuedeEliminarAdjuntoInstancia()
        {
            var userKey = (Context.User?.Identity?.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(userKey))
                return false;

            return RbacService.HasPermiso(userKey, "ADJUNTOS_ELIMINAR_INSTANCIA");
        }

        protected void rptDocs_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "EliminarAdjuntoInst", StringComparison.OrdinalIgnoreCase))
                return;

            if (!UsuarioPuedeEliminarAdjuntoInstancia())
            {
                ClientScript.RegisterStartupScript(GetType(), "wf_no_perm_adj", "alert('No tenés permisos para eliminar adjuntos de instancia.');", true);
                return;
            }

            if (_instanciaActualId <= 0)
            {
                ClientScript.RegisterStartupScript(GetType(), "wf_no_inst_adj", "alert('No se pudo resolver la instancia actual.');", true);
                return;
            }

            var arg = Convert.ToString(e.CommandArgument ?? "").Trim();
            if (string.IsNullOrWhiteSpace(arg) || arg.IndexOf('|') < 0)
            {
                ClientScript.RegisterStartupScript(GetType(), "wf_bad_adj_arg", "alert('No se pudo resolver el adjunto.');", true);
                return;
            }

            var parts = arg.Split(new[] { '|' }, 3);
            var storedFileName = (parts.Length > 0 ? parts[0] : "").Trim();
            var tareaIdDoc = (parts.Length > 1 ? parts[1] : "").Trim();
            var fileName = (parts.Length > 2 ? parts[2] : "").Trim();

            var motivo = (hfMotivoEliminarAdjunto.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(motivo))
            {
                ClientScript.RegisterStartupScript(GetType(), "wf_no_motivo_adj", "alert('Debe indicar un motivo.');", true);
                return;
            }

            var user = (Context.User?.Identity?.Name ?? "").Trim();

            try
            {
                var ok = MarcarAdjuntoEliminadoEnInstancia(_instanciaActualId, tareaIdDoc, storedFileName, fileName, user, motivo);
                if (!ok)
                {
                    ClientScript.RegisterStartupScript(GetType(), "wf_no_match_adj", "alert('No se encontró el adjunto a eliminar.');", true);
                    return;
                }

                MostrarDocumentos((int)_instanciaActualId);
                ClientScript.RegisterStartupScript(GetType(), "wf_ok_adj", "alert('Adjunto eliminado correctamente.');", true);
            }
            catch (Exception ex)
            {
                ClientScript.RegisterStartupScript(GetType(), "wf_err_adj", "alert('Error al eliminar adjunto: " + HttpUtility.JavaScriptStringEncode(ex.Message) + "');", true);
            }
        }

        private bool MarcarAdjuntoEliminadoEnInstancia(long instanciaId, string tareaIdDoc, string storedFileName, string fileName, string user, string motivo)
        {
            string json = "";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand("SELECT DatosContexto FROM dbo.WF_Instancia WHERE Id=@Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cn.Open();
                json = Convert.ToString(cmd.ExecuteScalar() ?? "");
            }

            if (string.IsNullOrWhiteSpace(json))
                return false;

            JObject root = null;
            try { root = JObject.Parse(json); } catch { root = null; }
            if (root == null) return false;

            JObject bcase =
                (root["estado"]?["biz"]?["case"] as JObject) ??
                (root["biz"]?["case"] as JObject);

            if (bcase == null) return false;

            var atts = bcase["attachments"] as JArray;
            if (atts == null || atts.Count == 0) return false;

            JObject target = null;

            for (int i = atts.Count - 1; i >= 0; i--)
            {
                var jo = atts[i] as JObject;
                if (jo == null) continue;

                var itemTareaId = Convert.ToString(jo["tareaId"] ?? "").Trim();
                var itemStored = Convert.ToString(jo["storedFileName"] ?? "").Trim();
                var itemViewer = Convert.ToString(jo["viewerUrl"] ?? "").Trim();
                var itemFileName = Convert.ToString(jo["fileName"] ?? jo["nombre"] ?? "").Trim();

                if (!string.Equals(itemTareaId, tareaIdDoc, StringComparison.OrdinalIgnoreCase))
                    continue;

                var matchStored =
                    !string.IsNullOrWhiteSpace(storedFileName) &&
                    string.Equals(itemStored, storedFileName, StringComparison.OrdinalIgnoreCase);

                var matchViewer =
                    !string.IsNullOrWhiteSpace(storedFileName) &&
                    (
                        itemViewer.IndexOf(storedFileName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        itemViewer.IndexOf(HttpUtility.UrlEncode(storedFileName), StringComparison.OrdinalIgnoreCase) >= 0
                    );

                var matchFileName =
                    !string.IsNullOrWhiteSpace(fileName) &&
                    string.Equals(itemFileName, fileName, StringComparison.OrdinalIgnoreCase);

                if (!(matchStored || matchViewer || matchFileName))
                    continue;

                target = (JObject)jo.DeepClone();
                atts.RemoveAt(i);
                break;
            }

            if (target == null)
                return false;

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Instancia
SET DatosContexto = @J
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@J", SqlDbType.NVarChar).Value = root.ToString(Newtonsoft.Json.Formatting.None);
                cn.Open();
                cmd.ExecuteNonQuery();
            }

            var path = Path.Combine(
                HttpContext.Current.Server.MapPath("~/App_Data/WFUploads"),
                instanciaId.ToString(),
                tareaIdDoc,
                storedFileName);

            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }

            GuardarLogAdjuntoEliminado(instanciaId, tareaIdDoc, target, user, motivo);

            return true;
        }

        private void GuardarLogAdjuntoEliminado(long instanciaId, string tareaIdDoc, JObject adjunto, string user, string motivo)
        {
            var fileName = Convert.ToString(adjunto["fileName"] ?? "");
            var storedFileName = Convert.ToString(adjunto["storedFileName"] ?? "");
            var tipo = Convert.ToString(adjunto["tipo"] ?? "");
            var usuarioSubio = Convert.ToString(adjunto["usuario"] ?? "");
            var fechaSubida = Convert.ToString(adjunto["fecha"] ?? "");
            var viewerUrl = Convert.ToString(adjunto["viewerUrl"] ?? "");

            var datos = new JObject
            {
                ["accion"] = "ADJUNTO_ELIMINADO",
                ["instanciaId"] = instanciaId,
                ["tareaId"] = tareaIdDoc ?? "",
                ["fileName"] = fileName,
                ["storedFileName"] = storedFileName,
                ["tipo"] = tipo,
                ["usuarioSubio"] = usuarioSubio,
                ["fechaSubida"] = fechaSubida,
                ["viewerUrl"] = viewerUrl,
                ["eliminadoPor"] = user ?? "",
                ["fechaEliminacion"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["motivo"] = motivo ?? ""
            };

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_InstanciaLog
    (WF_InstanciaId, FechaLog, Nivel, Mensaje, NodoId, NodoTipo, Datos)
VALUES
    (@InstId, GETDATE(), 'Warn', @Msg, NULL, 'adjunto.delete.manual', @Datos);", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@Msg", SqlDbType.NVarChar, 4000).Value =
                    "Adjunto eliminado manualmente. Archivo: " + fileName +
                    ". Tarea: " + (tareaIdDoc ?? "") +
                    ". Subido por: " + usuarioSubio +
                    ". Eliminado por: " + (user ?? "") +
                    ". Motivo: " + (motivo ?? "");

                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value =
                    datos.ToString(Newtonsoft.Json.Formatting.None);

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void BindDocAudit(long instanciaId)
        {
            var dt = new DataTable();

            bool dedup = (chkDocAuditDedup != null && chkDocAuditDedup.Checked);

            string sqlHistorico = @"
SELECT TOP 200
    FechaAlta,
    Accion,
    CASE WHEN EsRoot = 1 THEN 'Root' ELSE 'Attachment' END AS Scope,
    NodoTipo,
    TareaId,
    Tipo,
    DocumentoId,
    ViewerUrl,
    IndicesJson
FROM dbo.WF_InstanciaDocumento
WHERE WF_InstanciaId = @Inst
ORDER BY FechaAlta DESC;";

            string sqlDedup = @"
;WITH X AS (
    SELECT
        *,
        ROW_NUMBER() OVER (
            PARTITION BY
                ISNULL(DocumentoId,''), EsRoot, ISNULL(TareaId,'')
            ORDER BY FechaAlta DESC, Id DESC
        ) AS rn
    FROM dbo.WF_InstanciaDocumento
    WHERE WF_InstanciaId = @Inst
)
SELECT
    FechaAlta,
    Accion,
    CASE WHEN EsRoot = 1 THEN 'Root' ELSE 'Attachment' END AS Scope,
    NodoTipo,
    TareaId,
    Tipo,
    DocumentoId,
    ViewerUrl,
    IndicesJson
FROM X
WHERE rn = 1
ORDER BY FechaAlta DESC;";

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(dedup ? sqlDedup : sqlHistorico, cn))
            {
                cmd.Parameters.Add("@Inst", SqlDbType.BigInt).Value = instanciaId;
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            if (dt.Rows.Count == 0)
            {
                // ✅ ocultar card completo si no hay auditoría
                pnlDocAuditCard.Visible = false;

                pnlDocAudit.Visible = false;
                pnlDocAuditEmpty.Visible = false;

                gvDocAudit.DataSource = null;
                gvDocAudit.DataBind();
                return;
            }

            pnlDocAuditCard.Visible = true;

            pnlDocAudit.Visible = true;
            pnlDocAuditEmpty.Visible = false;

            gvDocAudit.DataSource = dt;
            gvDocAudit.DataBind();
        }

        protected void chkDocAuditDedup_CheckedChanged(object sender, EventArgs e)
        {
            if (_instanciaActualId > 0)
            {
                BindDocAudit(_instanciaActualId);
            }
        }

        void MostrarLogs(int instId)
        {
            string sql = @"
SELECT TOP (500)
    Fechalog,
    Nivel,
    Mensaje
FROM WF_InstanciaLog
WHERE WF_InstanciaId=@Id
ORDER BY Id ASC;";

            var sb = new StringBuilder();
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = instId;
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        sb.AppendFormat("{0:dd/MM/yyyy HH:mm:ss} [{1}] {2}\r\n",
                            rd["Fechalog"], rd["Nivel"], rd["Mensaje"]);
                    }
                }
            }

            pnlDatosCard.Visible = false;
            pnlLogsCard.Visible = true;

            pnlLogs.Visible = true;
            pnlLogsEmpty.Visible = false;
            litLogs.Text = Server.HtmlEncode(sb.ToString());

            // auditoría también desde logs
            BindDocAudit(instId);
        }
    }
}
