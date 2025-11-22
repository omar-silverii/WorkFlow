using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tarea_Detalle : System.Web.UI.Page
    {
        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                if (!long.TryParse(Request.QueryString["id"], out var tareaId))
                {
                    MostrarError("Id de tarea inválido.");
                    return;
                }

                ViewState["TareaId"] = tareaId;
                CargarTarea(tareaId);
            }
        }

        private void CargarTarea(long id)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT  Id, WF_InstanciaId, NodoId, NodoTipo,
        Titulo, Descripcion, RolDestino, UsuarioAsignado,
        Estado, Resultado, FechaCreacion, FechaVencimiento, FechaCierre,
        Datos
FROM    dbo.WF_Tarea
WHERE   Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                    {
                        MostrarError("Tarea no encontrada.");
                        return;
                    }

                    lblId.Text = dr["Id"].ToString();
                    lblInstancia.Text = dr["WF_InstanciaId"].ToString();
                    lblEstado.Text = Convert.ToString(dr["Estado"]);
                    lblTipo.Text = Convert.ToString(dr["NodoTipo"]);

                    txtTitulo.Text = Convert.ToString(dr["Titulo"]);
                    txtDescripcion.Text = Convert.ToString(dr["Descripcion"]);
                    txtRol.Text = Convert.ToString(dr["RolDestino"]);
                    txtUsuario.Text = Convert.ToString(dr["UsuarioAsignado"]);

                    string estado = Convert.ToString(dr["Estado"]);

                    // Si ya está cerrada, deshabilito edición
                    if (estado.Equals("Completada", StringComparison.OrdinalIgnoreCase) ||
                        estado.Equals("Cancelada", StringComparison.OrdinalIgnoreCase))
                    {
                        ddlResultado.Enabled = false;
                        txtObs.Enabled = false;
                        btnCompletar.Enabled = false;
                        lblInfo.Text = "La tarea ya está cerrada.";
                    }

                    // Si ya tenía resultado guardado, reflejarlo
                    var res = dr["Resultado"] as string;
                    if (!string.IsNullOrEmpty(res) && ddlResultado.Items.FindByValue(res) != null)
                        ddlResultado.SelectedValue = res;

                    // Si en Datos hay JSON con observaciones, intentar mostrarlo
                    var datos = dr["Datos"] as string;
                    if (!string.IsNullOrWhiteSpace(datos))
                    {
                        try
                        {
                            dynamic obj = JsonConvert.DeserializeObject(datos);
                            if (obj != null && obj.observaciones != null)
                                txtObs.Text = (string)obj.observaciones;
                        }
                        catch
                        {
                            // ignorar parse fallido
                        }
                    }
                }
            }
        }

        protected async void btnCompletar_Click(object sender, EventArgs e)
        {
            if (!Page.IsValid) return;

            if (!(ViewState["TareaId"] is long tareaId))
            {
                MostrarError("No se pudo determinar el Id de la tarea.");
                return;
            }

            string resultado = ddlResultado.SelectedValue;
            string observaciones = txtObs.Text ?? string.Empty;

            // === Aseguramos un usuario no vacío ===
            string usuarioActual = Context.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = Environment.UserName;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = "workflow.ui";

            var datosObj = new
            {
                observaciones,
                cerradoPor = usuarioActual,
                cerradoEn = DateTime.Now
            };

            string datosJson = JsonConvert.SerializeObject(datosObj, Formatting.None);

            try
            {
                await WorkflowRuntime.ReanudarDesdeTareaAsync(
                    tareaId,
                    resultado,
                    datosJson,
                    usuarioActual
                );

                lblInfo.Text = "Tarea completada y workflow reanudado.";
                CargarTarea(tareaId); // refresca pantalla
            }
            catch (Exception ex)
            {
                MostrarError("Error al reanudar el workflow: " + ex.Message);
            }
        }


        protected void btnVolver_Click(object sender, EventArgs e)
        {
            Response.Redirect("WF_Tareas.aspx");
        }

        private void MostrarError(string mensaje)
        {
            pnlDatos.Visible = false;
            pnlError.Visible = true;
            litError.Text = Server.HtmlEncode(mensaje);
        }
    }
}
