using System;
using System.Data;
using System.Data.SqlClient;
using System.Configuration;
using System.Web;
using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tarea_Ver : System.Web.UI.Page
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            lblUser.InnerText = (HttpContext.Current?.User?.Identity?.Name ?? "").Trim();

            if (!IsPostBack)
            {
                long tareaId = GetTareaId();
                if (tareaId <= 0)
                {
                    ShowError("Falta tareaId en la URL.");
                    DisableActions();
                    return;
                }

                CargarTarea(tareaId);
            }
        }

        private long GetTareaId()
        {
            var s = Request.QueryString["tareaId"];
            if (long.TryParse(s, out var id)) return id;
            return 0;
        }

        private void CargarTarea(long tareaId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("dbo.WF_Tarea_Get", cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                    {
                        ShowError("Tarea no encontrada: " + tareaId);
                        DisableActions();
                        return;
                    }

                    lblTareaId.Text = Convert.ToString(dr["TareaId"]);
                    lblInstanciaId.Text = Convert.ToString(dr["InstanciaId"]);
                    lblTitulo.Text = Convert.ToString(dr["Titulo"]);
                    lblDesc.Text = Convert.ToString(dr["Descripcion"]);
                    lblRol.Text = Convert.ToString(dr["RolDestino"]);
                    lblEstado.Text = Convert.ToString(dr["TareaEstado"]);

                    var vence = dr["FechaVencimiento"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["FechaVencimiento"]);
                    lblVence.Text = vence.HasValue ? vence.Value.ToString("dd/MM/yyyy HH:mm") : "-";

                    lblResultado.Text = Convert.ToString(dr["TareaResultado"]);
                    var cierre = dr["TareaFechaCierre"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["TareaFechaCierre"]);
                    lblCerrada.Text = cierre.HasValue ? cierre.Value.ToString("dd/MM/yyyy HH:mm") : "-";

                    // Si está cerrada, bloqueamos botones
                    var estado = (Convert.ToString(dr["TareaEstado"]) ?? "").Trim();
                    if (estado.Equals("Completada", StringComparison.OrdinalIgnoreCase) ||
                        estado.Equals("Cancelada", StringComparison.OrdinalIgnoreCase) ||
                        estado.Equals("Cerrada", StringComparison.OrdinalIgnoreCase))
                    {
                        DisableActions();
                        ShowInfo("Esta tarea ya está cerrada.");
                    }
                }
            }
        }

        protected async void btnAprobar_Click(object sender, EventArgs e)
        {
            await Resolver("apto");
        }

        protected async void btnRechazar_Click(object sender, EventArgs e)
        {
            await Resolver("rechazado");
        }

        private async System.Threading.Tasks.Task Resolver(string resultado)
        {
            long tareaId = GetTareaId();
            if (tareaId <= 0)
            {
                ShowError("Falta tareaId en la URL.");
                return;
            }

            var user = (HttpContext.Current?.User?.Identity?.Name ?? "").Trim();
            var obs = (txtObs.Text ?? "").Trim();

            var datos = new
            {
                observaciones = obs,
                cerradoPor = user,
                cerradoEn = DateTimeOffset.Now,
                resultado = resultado
            };
            string datosJson = JsonConvert.SerializeObject(datos, Formatting.None);

            try
            {
               
                // 2) Reanudamos la instancia
                await WorkflowRuntime.ReanudarDesdeTareaAsync(
                    tareaId: tareaId,
                    resultado: resultado,
                    datosJson: datosJson,
                    usuario: user
                );

                // 3) Redirección limpia
                Response.Redirect("WF_Gerente_Tareas.aspx", endResponse: false);
            }
            catch (Exception ex)
            {
                ShowError("Error al reanudar: " + ex.Message);
            }
        }


        private void DisableActions()
        {
            btnAprobar.Enabled = false;
            btnRechazar.Enabled = false;
        }

        private void ShowError(string msg)
        {
            litMsg.Text = "<div class='alert alert-danger'>" + Server.HtmlEncode(msg) + "</div>";
        }

        private void ShowInfo(string msg)
        {
            litMsg.Text = "<div class='alert alert-info'>" + Server.HtmlEncode(msg) + "</div>";
        }
    }
}
