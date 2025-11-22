using System;
using System.Configuration;
using System.Data.SqlClient;
using Newtonsoft.Json;
using Intranet.WorkflowStudio.Runtime;   // <-- ahora sí lo usa

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class Poliza_Nueva : System.Web.UI.Page
    {
        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
                CargarWorkflows();
        }

        private void CargarWorkflows()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"SELECT Id, Codigo + ' - ' + Nombre AS Nom
                                              FROM dbo.WF_Definicion
                                              WHERE Activo = 1
                                              ORDER BY Codigo, Version DESC", cn))
            {
                cn.Open();
                ddlWF.DataSource = cmd.ExecuteReader();
                ddlWF.DataValueField = "Id";
                ddlWF.DataTextField = "Nom";
                ddlWF.DataBind();
            }
        }

        protected async void btnEnviar_Click(object sender, EventArgs e)
        {
            try
            {
                var datos = new
                {
                    NroPoliza = txtPoliza.Text.Trim(),
                    Asegurado = txtAsegurado.Text.Trim(),
                    Fecha = DateTime.Now
                };
                string jsonEntrada = JsonConvert.SerializeObject(datos);

                int defId = int.Parse(ddlWF.SelectedValue);
                string usuario = (User?.Identity?.IsAuthenticated ?? false)
                                    ? User.Identity.Name
                                    : "poliza.ui";

                long instId = await WorkflowRuntime.CrearInstanciaYEjecutarAsync(
                                    defId,
                                    jsonEntrada,
                                    usuario);

                lblMsg.ForeColor = System.Drawing.Color.Green;
                lblMsg.Text = "Instancia creada y ejecutada. Id = " + instId +
                              ". Ver en WF_Instancias.aspx?defId=" + defId;
            }
            catch (Exception ex)
            {
                lblMsg.ForeColor = System.Drawing.Color.Red;
                lblMsg.Text = ex.Message;
            }
        }
    }
}

