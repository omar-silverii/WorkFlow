using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI.WebControls; // si falta
using Newtonsoft.Json.Linq; // arriba

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Gerente_Tareas : System.Web.UI.Page
    {
        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                lblUser.Text = (HttpContext.Current?.User?.Identity?.Name) ?? "";
                CargarTodo();
            }
        }

        protected void btnRefresh_Click(object sender, EventArgs e)
        {
            CargarTodo();
        }

        private void CargarTodo()
        {
            lblError.Visible = false;
            lblError.Text = "";

            var userKey = (HttpContext.Current?.User?.Identity?.Name) ?? "";

            try
            {
                // MIS TAREAS
                var dtMis = EjecutarSP("dbo.WF_Gerente_Tareas_MisTareas", userKey);
                EnriquecerSla(dtMis);
                EnriquecerEscalamiento(dtMis);
                dtMis.DefaultView.Sort = "SlaVencida DESC, FechaVencimiento ASC, FechaCreacion DESC";
                gvMis.DataSource = dtMis.DefaultView;
                gvMis.DataBind();
                lblCountMis.Text = dtMis.Rows.Count.ToString();

                // POR MI ROL
                var dtRol = EjecutarSP("dbo.WF_Gerente_Tareas_PorMiRol", userKey);
                EnriquecerSla(dtRol);
                EnriquecerEscalamiento(dtRol);
                dtRol.DefaultView.Sort = "SlaVencida DESC, FechaVencimiento ASC, FechaCreacion DESC";
                gvRol.DataSource = dtRol.DefaultView;
                gvRol.DataBind();
                lblCountRol.Text = dtRol.Rows.Count.ToString();

                // MI ALCANCE
                var dtAlc = EjecutarSP("dbo.WF_Gerente_Tareas_Pendientes_MiAlcance", userKey);
                EnriquecerSla(dtAlc);
                EnriquecerEscalamiento(dtAlc);
                dtAlc.DefaultView.Sort = "SlaVencida DESC, FechaVencimiento ASC, FechaCreacion DESC";
                gvAlcance.DataSource = dtAlc.DefaultView;
                gvAlcance.DataBind();
                lblCountAlcance.Text = dtAlc.Rows.Count.ToString();

                // CERRADAS (sin SLA)
                var dtCer = EjecutarSP("dbo.WF_Gerente_Tareas_Cerradas_Mis", userKey);
                gvCerradas.DataSource = dtCer;
                gvCerradas.DataBind();
                lblCountCerradas.Text = dtCer.Rows.Count.ToString();

                // Si querés diagnóstico visible sin debugger:
                //lblError.Visible = true;
                //lblError.CssClass = "alert alert-info";
                //lblError.Text = $"DEBUG: Mis={dtMis.Rows.Count}, Rol={dtRol.Rows.Count}, Alcance={dtAlc.Rows.Count}";
            }
            catch (Exception ex)
            {
                lblError.Visible = true;
                lblError.Text = "Error al cargar bandejas: " + Server.HtmlEncode(ex.ToString());
            }
        }

        private DataTable EjecutarSP(string spName, string userKey)
        {
            var dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(spName, cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = (object)userKey ?? DBNull.Value;

                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            return dt;
        }
        protected void gvMis_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "Liberar", StringComparison.OrdinalIgnoreCase))
                return;

            int tareaId = Convert.ToInt32(e.CommandArgument);
            string userKey = HttpContext.Current.User.Identity.Name;

            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand("dbo.WF_Tarea_Liberar", cn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;
                    cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;

                    cn.Open();
                    cmd.ExecuteNonQuery();
                }

                CargarTodo();
            }
            catch (Exception ex)
            {
                lblError.Visible = true;
                lblError.Text = Server.HtmlEncode(ex.Message);
            }
        }


        protected void gvAlcance_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "Tomar", StringComparison.OrdinalIgnoreCase))
                return;

            int tareaId = Convert.ToInt32(e.CommandArgument);
            string userKey = (HttpContext.Current?.User?.Identity?.Name) ?? "";

            try
            {
                TomarTarea(tareaId, userKey);

                // Mensaje OK (opcional, pero útil)
                lblError.Visible = true;
                lblError.CssClass = "alert alert-success";
                lblError.Text = "Tarea tomada correctamente.";

                CargarTodo();
            }
            catch (SqlException ex)
            {
                lblError.Visible = true;
                lblError.CssClass = "alert alert-danger";
                lblError.Text = "No se pudo tomar la tarea: " + Server.HtmlEncode(ex.Message);
            }
            catch (Exception ex)
            {
                lblError.Visible = true;
                lblError.CssClass = "alert alert-danger";
                lblError.Text = "Error inesperado: " + Server.HtmlEncode(ex.Message);
            }
        }





        protected void gvRol_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "TomarRol", StringComparison.OrdinalIgnoreCase))
                return;

            int tareaId = Convert.ToInt32(e.CommandArgument);
            string userKey = (HttpContext.Current?.User?.Identity?.Name) ?? "";

            try
            {
                TomarTarea(tareaId, userKey);

                lblError.Visible = true;
                lblError.CssClass = "alert alert-success";
                lblError.Text = "Tarea tomada correctamente (por rol).";

                CargarTodo();
            }
            catch (SqlException ex)
            {
                lblError.Visible = true;
                lblError.CssClass = "alert alert-danger";
                lblError.Text = "No se pudo tomar la tarea: " + Server.HtmlEncode(ex.Message);
            }
            catch (Exception ex)
            {
                lblError.Visible = true;
                lblError.CssClass = "alert alert-danger";
                lblError.Text = "Error inesperado: " + Server.HtmlEncode(ex.Message);
            }
        }

        private void TomarTarea(int tareaId, string userKey)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("dbo.WF_Tarea_Tomar", cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@TareaId", SqlDbType.Int).Value = tareaId;
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;

                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void EnriquecerSla(DataTable dt)
        {
            if (dt == null) return;

            if (!dt.Columns.Contains("SlaTexto"))
                dt.Columns.Add("SlaTexto", typeof(string));

            if (!dt.Columns.Contains("SlaVencida"))
                dt.Columns.Add("SlaVencida", typeof(bool));

            foreach (DataRow r in dt.Rows)
            {
                DateTime? vence = null;

                if (dt.Columns.Contains("FechaVencimiento") && r["FechaVencimiento"] != DBNull.Value)
                    vence = Convert.ToDateTime(r["FechaVencimiento"]);

                if (!vence.HasValue)
                {
                    r["SlaVencida"] = false;
                    r["SlaTexto"] = "-";
                    continue;
                }

                var now = DateTime.Now;
                var diff = vence.Value - now;

                if (diff.TotalSeconds < 0)
                {
                    r["SlaVencida"] = true;
                    var minutos = Math.Abs((int)Math.Round(diff.TotalMinutes));
                    r["SlaTexto"] = $"Vencida ({minutos} min)";
                }
                else
                {
                    r["SlaVencida"] = false;

                    if (diff.TotalDays >= 1)
                        r["SlaTexto"] = $"Faltan {Math.Floor(diff.TotalDays)} d";
                    else if (diff.TotalHours >= 1)
                        r["SlaTexto"] = $"Faltan {Math.Floor(diff.TotalHours)} h";
                    else
                        r["SlaTexto"] = $"Faltan {Math.Floor(diff.TotalMinutes)} min";
                }
            }
        }

        protected void gv_RowDataBound(object sender, System.Web.UI.WebControls.GridViewRowEventArgs e)
        {
            if (e.Row.RowType != System.Web.UI.WebControls.DataControlRowType.DataRow)
                return;

            var drv = e.Row.DataItem as DataRowView;
            if (drv == null || !drv.DataView.Table.Columns.Contains("SlaVencida"))
                return;

            bool vencida = drv["SlaVencida"] != DBNull.Value && Convert.ToBoolean(drv["SlaVencida"]);

            if (vencida)
                e.Row.CssClass = (e.Row.CssClass + " table-warning").Trim();
        }

        private void EnriquecerEscalamiento(DataTable dt)
        {
            if (dt == null) return;

            if (!dt.Columns.Contains("Escalada"))
                dt.Columns.Add("Escalada", typeof(bool));

            if (!dt.Columns.Contains("EscaladaTexto"))
                dt.Columns.Add("EscaladaTexto", typeof(string));

            foreach (DataRow r in dt.Rows)
            {
                bool escalada = false;
                string texto = "";

                try
                {
                    var datos = (dt.Columns.Contains("Datos") && r["Datos"] != DBNull.Value)
                        ? Convert.ToString(r["Datos"])
                        : null;

                    if (!string.IsNullOrWhiteSpace(datos))
                    {
                        var j = JObject.Parse(datos);
                        var v = j.SelectToken("escalado")?.ToString();
                        escalada = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

                        if (escalada)
                        {
                            var en = j.SelectToken("escaladoEn")?.ToString();
                            texto = string.IsNullOrWhiteSpace(en) ? "Escalada" : $"Escalada ({en})";
                        }
                    }
                }
                catch
                {
                    // si el JSON está roto, no rompemos UI
                    escalada = false;
                    texto = "";
                }

                r["Escalada"] = escalada;
                r["EscaladaTexto"] = texto;
            }
        }





    }
}
