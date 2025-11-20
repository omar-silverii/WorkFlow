using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class Poliza_Bandeja : System.Web.UI.Page
    {
        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack) Cargar();
        }

        private void Cargar()
        {
            string sql = @"SELECT Id, Numero, Asegurado, FechaAlta
                           FROM dbo.PolizasDemo
                           WHERE (@Filtro='' OR Numero LIKE @FiltroLike)
                           ORDER BY Id DESC";

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                var f = (txtFiltro.Text ?? "").Trim();
                cmd.Parameters.AddWithValue("@Filtro", f);
                cmd.Parameters.AddWithValue("@FiltroLike", "%" + f + "%");
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    gv.DataSource = dr;
                    gv.DataBind();
                }
            }
        }

        protected void btnBuscar_Click(object sender, EventArgs e) => Cargar();

        protected void gv_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gv.PageIndex = e.NewPageIndex;
            Cargar();
        }

        protected void gv_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (e.CommandName == "VerInst")
            {
                // opcion 1: ir sin filtro
                // Response.Redirect("WF_Instancias.aspx");

                // opcion 2: pasar el número y filtrar por DatosEntrada (LIKE)
                string numero = Convert.ToString(e.CommandArgument);
                Response.Redirect("WF_Instancias.aspx?poliza=" + Server.UrlEncode(numero));
            }
        }
    }
}
