using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tareas : System.Web.UI.Page
    {
        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                CargarGrid();
            }
        }

        private void CargarGrid()
        {
            string sql = @"
SELECT
    T.Id,
    T.WF_InstanciaId,
    i.WF_DefinicionId,
    T.NodoId,
    T.NodoTipo,
    T.Titulo,
    T.Descripcion,
    T.RolDestino,
    T.UsuarioAsignado,
    T.Estado,
    T.Resultado,
    T.FechaCreacion,
    T.FechaVencimiento
FROM dbo.WF_Tarea T JOIN    dbo.WF_Instancia  i ON i.Id = T.WF_InstanciaId
WHERE 1 = 1";

            bool soloPend = chkSoloPendientes.Checked;
            string filtro = txtFiltro.Text.Trim();

            if (soloPend)
            {
                sql += " AND T.Estado = 'Pendiente'";
            }

            if (!string.IsNullOrEmpty(filtro))
            {
                sql += @"
 AND (
        T.Titulo          LIKE @Filtro
     OR T.Descripcion     LIKE @Filtro
     OR T.RolDestino      LIKE @Filtro
     OR T.UsuarioAsignado LIKE @Filtro
    )";
            }

            sql += " ORDER BY T.FechaCreacion DESC";

            DataTable dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                if (!string.IsNullOrEmpty(filtro))
                    cmd.Parameters.AddWithValue("@Filtro", "%" + filtro + "%");

                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            gvTareas.DataSource = dt;
            gvTareas.DataBind();
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvTareas.PageIndex = 0;
            CargarGrid();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtFiltro.Text = string.Empty;
            chkSoloPendientes.Checked = true;
            gvTareas.PageIndex = 0;
            CargarGrid();
        }

        protected void gvTareas_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvTareas.PageIndex = e.NewPageIndex;
            CargarGrid();
        }

        protected void gvTareas_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            // ya tendrás algo como esto para “Detalle”
            if (e.CommandName == "Detalle")
            {
                long tareaId = Convert.ToInt64(e.CommandArgument);
                Response.Redirect("WF_Tarea_Detalle.aspx?id=" + tareaId);
                return;
            }

            // ===== NUEVO: Ir a la instancia =====
            if (e.CommandName == "VerInstancia")
            {
                // CommandArgument viene como "WF_InstanciaId|WF_DefinicionId"
                var arg = Convert.ToString(e.CommandArgument) ?? string.Empty;
                long instId = 0;
                int defId = 0;

                var parts = arg.Split('|');
                if (parts.Length >= 2)
                {
                    long.TryParse(parts[0], out instId);
                    int.TryParse(parts[1], out defId);
                }

                if (instId > 0 && defId > 0)
                {
                    // WF_Instancias ya la preparamos para entender estos parámetros
                    Response.Redirect(
                        "WF_Instancias.aspx?WF_DefinicionId=" + defId +
                        "&instId=" + instId);
                }
                else if (instId > 0)
                {
                    // Fallback si por alguna razón no vino la definición
                    Response.Redirect(
                        "WF_Instancias.aspx?WF_InstanciaId=" + instId);
                }
                else
                {
                    Response.Redirect("WF_Instancias.aspx");
                }

                return;
            }

            // ...el resto de tus comandos (re-asignar, etc)...
        }

    }
}
