using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Instancias : System.Web.UI.Page
    {
        private long _instanciaActualId
        {
            get { return (ViewState["__instActualId"] == null) ? 0 : (long)ViewState["__instActualId"]; }
            set { ViewState["__instActualId"] = value; }
        }

        protected void chkDocAuditDedup_CheckedChanged(object sender, EventArgs e)
        {
            if (_instanciaActualId > 0)
            {
                BindDocAudit(_instanciaActualId);
                BindDocsFromDb(_instanciaActualId);
            }
        }


        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Ejecuciones"; } catch { }

            if (!IsPostBack)
            {
                chkMostrarFinalizados.Checked = false;
                ddlEstado.SelectedValue = "";
                MarcarEstadoPills();
                txtBuscar.Text = "";
                CargarDefiniciones();

                // Acepto varios nombres de parámetro:
                // ?defId=28  (viejo)
                // ?WF_DefinicionId=28  (el que estás usando desde WF_Tarea_Detalle)
                string defIdQS =
                    Request.QueryString["defId"]
                    ?? Request.QueryString["WF_DefinicionId"];

                if (!string.IsNullOrEmpty(defIdQS))
                {
                    ViewState["InstanciaSeleccionada"] = defIdQS;

                    ListItem li = ddlDef.Items.FindByValue(defIdQS);
                    if (li != null)
                    {
                        ddlDef.ClearSelection();
                        li.Selected = true;
                    }
                }
                else
                {
                    ViewState["InstanciaSeleccionada"] = null;
                }
                CargarInstancias();

                // Si venimos desde tareas, habilitamos botón "Volver a tareas"
                var returnTo = Request.QueryString["returnTo"];
                if (!string.IsNullOrWhiteSpace(returnTo) && returnTo.Equals("tareas", StringComparison.OrdinalIgnoreCase))
                {
                    lnkBackTareas.Visible = true;
                    lnkBackTareas.NavigateUrl = "WF_Gerente_Tareas.aspx";
                }
                chkDocAuditDedup.Checked = true;

                pnlDocAuditCard.Visible = false;

                // NUEVO: deep link a instancia (?inst=90364)
                string instQS = Request.QueryString["inst"];
                long instIdQS;
                if (!string.IsNullOrWhiteSpace(instQS) && long.TryParse(instQS, out instIdQS) && instIdQS > 0)
                {
                    IrAInstancia(instIdQS);
                }
            }
        }

        private void IrAInstancia(long instId)
        {
            // 1) Detectar la definición de esa instancia
            int defId = 0;
            string estado = null;

            string sql = "SELECT WF_DefinicionId, Estado FROM WF_Instancia WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instId;
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read())
                        return;

                    defId = Convert.ToInt32(rd["WF_DefinicionId"]);
                    estado = Convert.ToString(rd["Estado"] ?? "");
                }
            }

            // 2) Seleccionar la definición en el dropdown
            var li = ddlDef.Items.FindByValue(defId.ToString());
            if (li != null)
            {
                ddlDef.ClearSelection();
                li.Selected = true;
            }

            // 3) Ajustar filtros para que el listado la incluya
            // Si es finalizado, hay que permitir mostrar finalizados.
            if (!string.IsNullOrWhiteSpace(estado) && estado.Equals("Finalizado", StringComparison.OrdinalIgnoreCase))
                chkMostrarFinalizados.Checked = true;

            ddlEstado.SelectedValue = ""; // no filtrar por estado, para no excluirla
            MarcarEstadoPills();

            // 4) Buscar por Id
            txtBuscar.Text = instId.ToString();

            // 5) Recargar listado y mostrar datos directamente
            gvInst.PageIndex = 0;
            CargarInstancias();
            MostrarDatos((int)instId);
        }

        protected void lnkEstado_Click(object sender, EventArgs e)
        {
            var lb = sender as LinkButton;
            if (lb == null) return;

            ddlEstado.SelectedValue = Convert.ToString(lb.CommandArgument ?? "").Trim();
            MarcarEstadoPills();
            CargarInstancias();
        }

        private void MarcarEstadoPills()
        {
            string estado = Convert.ToString(ddlEstado.SelectedValue ?? "").Trim();

            // base classes (mantener el look "pill")
            lnkEstadoTodos.CssClass = "btn btn-outline-secondary";
            lnkEstadoEnCurso.CssClass = "btn btn-outline-primary";
            lnkEstadoError.CssClass = "btn btn-outline-danger";
            lnkEstadoFinalizado.CssClass = "btn btn-outline-dark";

            if (string.IsNullOrWhiteSpace(estado))
                lnkEstadoTodos.CssClass += " active";
            else if (estado.Equals("EnCurso", StringComparison.OrdinalIgnoreCase))
                lnkEstadoEnCurso.CssClass += " active";
            else if (estado.Equals("Error", StringComparison.OrdinalIgnoreCase))
                lnkEstadoError.CssClass += " active";
            else if (estado.Equals("Finalizado", StringComparison.OrdinalIgnoreCase))
                lnkEstadoFinalizado.CssClass += " active";
        }

        private void CargarDefiniciones()
        {
            string defIdQS =
                Request.QueryString["defId"]
                ?? Request.QueryString["WF_DefinicionId"];

            string sql = @"
SELECT Id, Codigo, Nombre
FROM WF_Definicion
WHERE Activo = 1
ORDER BY Codigo;";

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                var dt = new DataTable();
                cn.Open();
                dt.Load(cmd.ExecuteReader());

                ddlDef.DataSource = dt;
                ddlDef.DataTextField = "Codigo";
                ddlDef.DataValueField = "Id";
                ddlDef.DataBind();

                // Si no vino defId por querystring, elegir el primero
                if (ddlDef.Items.Count > 0 && string.IsNullOrEmpty(defIdQS))
                {
                    ddlDef.SelectedIndex = 0;
                }
            }
        }

        private void CargarInstancias()
        {
            int defId = 0;
            int.TryParse(Convert.ToString(ddlDef.SelectedValue), out defId);

            bool mostrarFinalizados = chkMostrarFinalizados.Checked;
            string estado = Convert.ToString(ddlEstado.SelectedValue ?? "").Trim();
            string q = (txtBuscar.Text ?? "").Trim();

            var where = new List<string>();
            where.Add("WF_DefinicionId = @DefId");

            // Por defecto: ocultar finalizados
            if (!mostrarFinalizados)
                where.Add("Estado <> 'Finalizado'");

            // Filtro por estado
            if (!string.IsNullOrWhiteSpace(estado))
                where.Add("Estado = @Estado");

            // Buscar (SIEMPRE por DatosContexto; y si es numérico, también por Id contiene)
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

                pnlDocAuditCard.Visible = false;
                pnlDocAudit.Visible = false;
                pnlDocAuditEmpty.Visible = true;
                gvDocAudit.DataSource = null;
                gvDocAudit.DataBind();
            }
        }

        protected void ddlDef_SelectedIndexChanged(object sender, EventArgs e)
        {
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

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected void btnRefrescar_Click(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected async void btnCrearInst_Click(object sender, EventArgs e)
        {
            int defId = 0;
            int.TryParse(Convert.ToString(ddlDef.SelectedValue), out defId);

            string usuario =
                (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : (Environment.UserName ?? "web");

            string datosEntradaJson = null;

            try
            {
                long instId = await WorkflowRuntime.CrearInstanciaYEjecutarAsync(
                    defId,
                    datosEntradaJson,
                    usuario
                );

                txtBuscar.Text = instId.ToString();
                ddlEstado.SelectedValue = "";
                MarcarEstadoPills();
                chkMostrarFinalizados.Checked = true;
                CargarInstancias();
            }
            catch (Exception ex)
            {
                pnlDatos.Visible = true;
                pnlDatosEmpty.Visible = false;
                litDatos.Text = Server.HtmlEncode("Error al crear/ejecutar instancia:\r\n" + ex.Message);
            }
        }

        protected void gvInst_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvInst.PageIndex = e.NewPageIndex;
            CargarInstancias();
        }

        protected void gvInst_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            int instId;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out instId))
                return;

            // NUEVO: recordar instancia seleccionada para toggle y consistencia UX
            _instanciaActualId = instId;
            pnlDocAuditCard.Visible = true;

            if (e.CommandName == "Datos")
            {
                MostrarDatos(instId);
            }
            else if (e.CommandName == "Logs")
            {
                MostrarLogs(instId);

                // NUEVO: también refrescamos auditoría documental
                BindDocAudit(instId);
            }
            else if (e.CommandName == "Reejecutar")
            {
                Reejecutar(instId);
            }
        }


        private void MostrarDatos(int instId)
        {
            string sql = "SELECT DatosContexto FROM WF_Instancia WHERE Id=@Id;";
            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = instId;
                cn.Open();
                var val = cmd.ExecuteScalar();
                string json = val == DBNull.Value || val == null ? "" : Convert.ToString(val);

                pnlDatos.Visible = true;
                pnlDatosEmpty.Visible = false;

                litDatos.Text = Server.HtmlEncode(PrettyJson(json));

                // NUEVO: documentos del caso desde auditoría documental (DB)
                BindDocsFromDb(instId);

                _instanciaActualId = instId;
                pnlDocAuditCard.Visible = true;
                BindDocAudit(instId);
            }

        }

        private void BindDocsFromDb(long instanciaId)
        {
            try
            {
                var rows = new List<DocUiRow>();

                bool dedup = (chkDocAuditDedup != null && chkDocAuditDedup.Checked);

                string sqlHistorico = @"
SELECT TOP 200
    DocumentoId, CarpetaId, FicheroId, Tipo, ViewerUrl, TareaId, EsRoot, FechaAlta
FROM dbo.WF_InstanciaDocumento
WHERE WF_InstanciaId = @Inst
ORDER BY FechaAlta DESC, Id DESC;";

                // Deduplicado: último por (DocumentoId + EsRoot + TareaId)
                string sqlDedup = @"
;WITH X AS (
    SELECT *,
        ROW_NUMBER() OVER(
            PARTITION BY ISNULL(DocumentoId,''), EsRoot, ISNULL(TareaId,'')
            ORDER BY FechaAlta DESC, Id DESC
        ) AS rn
    FROM dbo.WF_InstanciaDocumento
    WHERE WF_InstanciaId = @Inst
)
SELECT TOP 200
    DocumentoId, CarpetaId, FicheroId, Tipo, ViewerUrl, TareaId, EsRoot, FechaAlta
FROM X
WHERE rn = 1
ORDER BY FechaAlta DESC;";

                using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                using (var cmd = new SqlCommand(dedup ? sqlDedup : sqlHistorico, cn))
                {
                    cmd.Parameters.Add("@Inst", SqlDbType.BigInt).Value = instanciaId;
                    cn.Open();

                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            rows.Add(new DocUiRow
                            {
                                DocumentoId = Convert.ToString(rd["DocumentoId"] ?? ""),
                                CarpetaId = Convert.ToString(rd["CarpetaId"] ?? ""),
                                FicheroId = Convert.ToString(rd["FicheroId"] ?? ""),
                                Tipo = Convert.ToString(rd["Tipo"] ?? ""),
                                ViewerUrl = Convert.ToString(rd["ViewerUrl"] ?? ""),
                                TareaId = Convert.ToString(rd["TareaId"] ?? "")
                            });
                        }
                    }
                }

                ShowDocs(rows);
            }
            catch
            {
                ShowDocs(new List<DocUiRow>());
            }
        }

        private void BindDocAudit(long instanciaId)
        {
            var dt = new DataTable();

            var csItem = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (csItem == null) throw new InvalidOperationException("ConnectionString 'DefaultConnection' no encontrada.");
            var cnn = csItem.ConnectionString;

            // Toggle: deduplicado vs histórico
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

            // Deduplicado: último por (DocumentoId + EsRoot + TareaId)
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

            using (var cn = new SqlConnection(cnn))
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
                pnlDocAudit.Visible = false;
                pnlDocAuditEmpty.Visible = true;
                gvDocAudit.DataSource = null;
                gvDocAudit.DataBind();
                return;
            }

            pnlDocAudit.Visible = true;
            pnlDocAuditEmpty.Visible = false;

            gvDocAudit.DataSource = dt;
            gvDocAudit.DataBind();
        }


        private void MostrarLogs(int instId)
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

            pnlLogs.Visible = true;
            pnlLogsEmpty.Visible = false;
            litLogs.Text = Server.HtmlEncode(sb.ToString());

            pnlDocAuditCard.Visible = true;
            BindDocAudit(instId);
        }

        private async void Reejecutar(int instId)
        {
            string usuario =
                (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                    ? User.Identity.Name
                    : (Environment.UserName ?? "web");

            try
            {
                await WorkflowRuntime.ReejecutarInstanciaAsync(instId, usuario);

                // refrescar
                CargarInstancias();
                MostrarLogs(instId);

                // NUEVO: refrescar auditoría documental post-ejecución
                BindDocAudit(instId);
            }
            catch (Exception ex)
            {
                pnlLogs.Visible = true;
                pnlLogsEmpty.Visible = false;
                litLogs.Text = Server.HtmlEncode("Error al reejecutar:\r\n" + ex.Message);
            }

        }

        private string PrettyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                return JToken.Parse(json).ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }


        // ===== Documentos (Caso) =====
        private class DocUiRow
        {
            public string DocumentoId { get; set; }
            public string CarpetaId { get; set; }
            public string FicheroId { get; set; }
            public string Tipo { get; set; }
            public string ViewerUrl { get; set; }
            public string TareaId { get; set; }
        }

        private void BindDocsFromDatosContexto(string datosContextoJson)
        {
            try
            {
                var rows = new List<DocUiRow>();
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

                var biz = root["biz"] as JObject;
                var bcase = biz?["case"] as JObject;

                // rootDoc
                var rootDoc = bcase?["rootDoc"] as JObject;
                if (rootDoc != null)
                    rows.Add(ToRow(rootDoc));

                // attachments[]
                var atts = bcase?["attachments"] as JArray;
                if (atts != null)
                {
                    foreach (var it in atts)
                    {
                        var jo = it as JObject;
                        if (jo == null) continue;
                        rows.Add(ToRow(jo));
                    }
                }

                ShowDocs(rows);
            }
            catch
            {
                ShowDocs(new List<DocUiRow>());
            }
        }

        private void ShowDocs(List<DocUiRow> rows)
        {
            if (rows != null && rows.Count > 0)
            {
                pnlDocs.Visible = true;
                pnlDocsEmpty.Visible = false;

                rptDocs.DataSource = rows;
                rptDocs.DataBind();
            }
            else
            {
                pnlDocs.Visible = false;
                pnlDocsEmpty.Visible = true;

                rptDocs.DataSource = null;
                rptDocs.DataBind();
            }
        }

        private static DocUiRow ToRow(JObject doc)
        {
            return new DocUiRow
            {
                DocumentoId = Convert.ToString(doc["documentoId"] ?? ""),
                CarpetaId = Convert.ToString(doc["carpetaId"] ?? ""),
                FicheroId = Convert.ToString(doc["ficheroId"] ?? ""),
                Tipo = Convert.ToString(doc["tipo"] ?? ""),
                ViewerUrl = Convert.ToString(doc["viewerUrl"] ?? ""),
                TareaId = Convert.ToString(doc["tareaId"] ?? "")
            };
        }
    }
}
