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
            // Topbar activo siempre (postback o no)
            try
            {
                Topbar1.ActiveSection = "Tareas";
            }
            catch
            {
                // si por alguna razón aún no está el control en el aspx, no rompemos la página
            }

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
FROM dbo.WF_Tarea T
JOIN dbo.WF_Instancia i ON i.Id = T.WF_InstanciaId
WHERE 1 = 1";

            bool soloPend = chkSoloPendientes.Checked;
            string filtro = (txtFiltro.Text ?? "").Trim();

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

            var dt = new DataTable();

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
            // Si usás CommandName="Detalle" en algún lado
            if (e.CommandName == "Detalle")
            {
                long tareaId = Convert.ToInt64(e.CommandArgument);
                Response.Redirect("WF_Tarea_Detalle.aspx?id=" + tareaId);
                return;
            }

            // ===== Ir a la instancia (CORREGIDO) =====
            if (e.CommandName == "VerInstancia")
            {
                // CommandArgument: "WF_InstanciaId|WF_DefinicionId"
                var arg = Convert.ToString(e.CommandArgument) ?? string.Empty;

                long instId = 0;
                int defId = 0;

                var parts = arg.Split('|');
                if (parts.Length >= 2)
                {
                    long.TryParse(parts[0], out instId);
                    int.TryParse(parts[1], out defId);
                }

                // IMPORTANTE:
                // WF_Instancias hoy entiende "defId" (según tu URL /WF_Instancias?defId=6117)
                // y para abrir una instancia específica vamos con "inst"
                if (defId > 0 && instId > 0)
                {
                    Response.Redirect("WF_Instancias.aspx?defId=" + defId + "&inst=" + instId);
                    return;
                }

                // Fallbacks seguros
                if (instId > 0)
                {
                    Response.Redirect("WF_Instancias.aspx?inst=" + instId);
                    return;
                }

                Response.Redirect("WF_Instancias.aspx");
                return;
            }

            // ...otros comandos...
        }
    }
}
