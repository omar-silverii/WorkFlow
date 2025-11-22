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
FROM dbo.WF_Tarea T
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
    }
}
