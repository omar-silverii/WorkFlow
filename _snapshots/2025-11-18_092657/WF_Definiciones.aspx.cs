using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Definiciones : System.Web.UI.Page
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
                SELECT Id, Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor
                FROM dbo.WF_Definicion
                WHERE 1 = 1";

            string filtro = txtFiltro.Text.Trim();
            if (!string.IsNullOrEmpty(filtro))
            {
                sql += " AND Codigo LIKE @Filtro";
            }

            sql += " ORDER BY Codigo, Version DESC";

            DataTable dt = new DataTable();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                if (!string.IsNullOrEmpty(filtro))
                    cmd.Parameters.AddWithValue("@Filtro", "%" + filtro + "%");

                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            gvDef.DataSource = dt;
            gvDef.DataBind();

            pnlJson.Visible = false;
            preJson.InnerText = string.Empty;
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvDef.PageIndex = 0;
            CargarGrid();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtFiltro.Text = string.Empty;
            gvDef.PageIndex = 0;
            CargarGrid();
        }

        protected void btnNuevo_Click(object sender, EventArgs e)
        {
            // Abre el editor vacío
            Response.Redirect("WorkflowUI.aspx");
        }

        protected void gvDef_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvDef.PageIndex = e.NewPageIndex;
            CargarGrid();
        }

        protected void gvDef_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (e.CommandName == "VerJson")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                VerJson(id);
            }
            else if (e.CommandName == "VerInst")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                Response.Redirect("WF_Instancias.aspx?defId=" + id);
            }
        }

        private void VerJson(int id)
        {
            string json = null;

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand("SELECT JsonDef FROM dbo.WF_Definicion WHERE Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                object o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value)
                    json = o.ToString();
            }

            preJson.InnerText = string.IsNullOrEmpty(json) ? "-- sin JSON --" : json;
            pnlJson.Visible = true;
        }
    }
}
