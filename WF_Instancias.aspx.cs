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
        protected void Page_Load(object sender, EventArgs e)
        {
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
            }
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

            if (e.CommandName == "Datos")
            {
                MostrarDatos(instId);
            }
            else if (e.CommandName == "Logs")
            {
                MostrarLogs(instId);
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
            }
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
    }
}
