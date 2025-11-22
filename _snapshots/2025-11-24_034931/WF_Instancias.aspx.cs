using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;
using Intranet.WorkflowStudio.Runtime;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Instancias : System.Web.UI.Page
    {
        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                CargarDefiniciones();

                // si viene de WF_Definiciones.aspx?defId=...
                string defIdQS = Request.QueryString["defId"];
                if (!string.IsNullOrEmpty(defIdQS))
                {
                    ListItem li = ddlDef.Items.FindByValue(defIdQS);
                    if (li != null)
                    {
                        ddlDef.ClearSelection();
                        li.Selected = true;
                    }
                }

                CargarInstancias();
            }
        }

        private void CargarDefiniciones()
        {
            ddlDef.Items.Clear();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(@"
                SELECT Id,
                       Codigo + ' - v' + CAST(Version AS NVARCHAR(10)) AS Nombre
                FROM dbo.WF_Definicion
                WHERE Activo = 1
                ORDER BY Codigo, Version DESC", cn))
            {
                cn.Open();
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    ddlDef.DataSource = dr;
                    ddlDef.DataValueField = "Id";
                    ddlDef.DataTextField = "Nombre";
                    ddlDef.DataBind();
                }
            }

            // Null-safety: si no hay definiciones activas
            if (ddlDef.Items.Count == 0)
                ddlDef.Items.Add(new ListItem("(sin definiciones activas)", ""));
        }

        private void CargarInstancias()
        {
            string sql = @"
                SELECT i.Id, i.Estado, i.FechaInicio, i.FechaFin
                FROM dbo.WF_Instancia i
                WHERE 1=1";

            bool tieneDef = ddlDef.Items.Count > 0 && !string.IsNullOrEmpty(ddlDef.SelectedValue);

            if (tieneDef)
            {
                sql += " AND i.WF_DefinicionId = @DefId";
            }

            // Filtro adicional opcional por querystring (ej: ?poliza=xxxx)
            string numeroQS = Request.QueryString["poliza"];
            if (!string.IsNullOrEmpty(numeroQS))
            {
                sql += " AND ISNULL(i.DatosEntrada,'') LIKE @Poliza";
            }

            sql += " ORDER BY i.Id DESC";

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                if (tieneDef)
                {
                    cmd.Parameters.AddWithValue("@DefId", Convert.ToInt32(ddlDef.SelectedValue));
                }
                if (!string.IsNullOrEmpty(numeroQS))
                {
                    cmd.Parameters.AddWithValue("@Poliza", "%" + numeroQS + "%");
                }

                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                {
                    DataTable dt = new DataTable();
                    da.Fill(dt);

                    gvInst.DataSource = dt;
                    gvInst.DataBind();
                }
            }

            pnlDetalle.Visible = false;
            preDetalle.InnerText = string.Empty;
        }

        protected void ddlDef_SelectedIndexChanged(object sender, EventArgs e)
        {
            CargarInstancias();
        }

        protected void btnRefrescar_Click(object sender, EventArgs e)
        {
            CargarInstancias();
        }

        protected void gvInst_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvInst.PageIndex = e.NewPageIndex;
            CargarInstancias();
        }

        protected void gvInst_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            long instId = Convert.ToInt64(e.CommandArgument);

            if (e.CommandName == "VerDatos")
            {
                VerDatos(instId);               // método SIN async
                return;
            }

            if (e.CommandName == "VerLog")
            {
                VerLog(instId);                 // método SIN async
                return;
            }

            if (e.CommandName == "Reejecutar")
            {
                string usuario = (User?.Identity?.IsAuthenticated ?? false)
                    ? User.Identity.Name
                    : "wf.ui";

                long? nuevaId = null;

                // Ejecutar la operación async correctamente en WebForms
                RegisterAsyncTask(new PageAsyncTask(async ct =>
                {
                    nuevaId = await Intranet.WorkflowStudio.Runtime.WorkflowRuntime
                                 .ReejecutarInstanciaAsync(instId, usuario);
                }));

                ExecuteRegisteredAsyncTasks();

                if (nuevaId.HasValue)
                {
                    lblTituloDetalle.InnerText = "Nueva instancia creada: " + nuevaId.Value;
                    preDetalle.InnerText = "Se re-ejecutó la instancia " + instId +
                                           " → nueva Id = " + nuevaId.Value;
                    pnlDetalle.Visible = true;

                    // Refrescar la grilla para ver el nuevo registro
                    CargarInstancias();
                }
                else
                {
                    lblTituloDetalle.InnerText = "Re-ejecución";
                    preDetalle.InnerText = "No se devolvió un Id de nueva instancia.";
                    pnlDetalle.Visible = true;
                }
                return;
            }
        }

        private void VerDatos(long instId)
        {
            string sql = "SELECT DatosEntrada, DatosContexto FROM dbo.WF_Instancia WHERE Id = @Id";
            StringBuilder sb = new StringBuilder();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    if (dr.Read())
                    {
                        sb.AppendLine("Datos de entrada:");
                        sb.AppendLine(Convert.ToString(dr["DatosEntrada"]));
                        sb.AppendLine();
                        sb.AppendLine("Contexto / payload:");
                        sb.AppendLine(Convert.ToString(dr["DatosContexto"]));
                    }
                    else
                    {
                        sb.AppendLine("No se encontraron datos.");
                    }
                }
            }

            lblTituloDetalle.InnerText = "Instancia " + instId + " – Datos";
            preDetalle.InnerText = sb.ToString();
            pnlDetalle.Visible = true;
        }

        private void VerLog(long instId)
        {
            string sql = @"
                SELECT FechaLog, Nivel, Mensaje, NodoId, NodoTipo
                FROM dbo.WF_InstanciaLog
                WHERE WF_InstanciaId = @Id
                ORDER BY FechaLog";

            StringBuilder sb = new StringBuilder();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    if (dr.HasRows)
                    {
                        while (dr.Read())
                        {
                            DateTime fecha = dr.GetDateTime(0);
                            string nivel = dr["Nivel"].ToString();
                            string mensaje = dr["Mensaje"].ToString();
                            object nodoIdObj = dr["NodoId"];
                            object nodoTipoObj = dr["NodoTipo"];

                            sb.Append(fecha.ToString("dd/MM/yyyy HH:mm:ss"));
                            sb.Append(" [");
                            sb.Append(nivel);
                            sb.Append("] ");

                            if (nodoIdObj != DBNull.Value)
                            {
                                sb.Append("(Nodo=" + nodoIdObj.ToString());
                                if (nodoTipoObj != DBNull.Value)
                                {
                                    sb.Append(" / " + nodoTipoObj.ToString());
                                }
                                sb.Append(") ");
                            }

                            sb.AppendLine(mensaje);
                        }
                    }
                    else
                    {
                        sb.AppendLine("Sin logs para esta instancia.");
                    }
                }
            }

            lblTituloDetalle.InnerText = "Instancia " + instId + " – Log";
            preDetalle.InnerText = sb.ToString();
            pnlDetalle.Visible = true;
        }

        // =========================
        // NUEVO: crear instancia + ejecutar workflow (async/await directo)
        // =========================
        protected async void btnCrearInst_Click(object sender, EventArgs e)
        {
            // 1) sacar la definición seleccionada
            int defId = 0;
            if (ddlDef.Items.Count > 0 && !string.IsNullOrEmpty(ddlDef.SelectedValue))
                int.TryParse(ddlDef.SelectedValue, out defId);

            // si no hay selección, intento tomar la primera
            if (defId == 0 && ddlDef.Items.Count > 0)
                int.TryParse(ddlDef.Items[0].Value, out defId);

            if (defId == 0)
            {
                // no hay definiciones activas
                lblTituloDetalle.InnerText = "Crear instancia";
                preDetalle.InnerText = "No hay definiciones activas para crear una instancia.";
                pnlDetalle.Visible = true;
                return;
            }

            // 2) Datos de entrada de prueba
            string datosEntradaJson = "{ \"demo\": true }";

            // 3) Usuario actual
            string usuario = (User?.Identity?.IsAuthenticated ?? false)
                ? User.Identity.Name
                : "wf.instancias";

            long nuevaInstId;

            try
            {
                // CREA la instancia en WF_Instancia + ejecuta el workflow
                nuevaInstId = await WorkflowRuntime.CrearInstanciaYEjecutarAsync(
                    defId,
                    datosEntradaJson,
                    usuario
                );
            }
            catch (Exception ex)
            {
                // si hay cualquier error del motor, lo vemos acá
                lblTituloDetalle.InnerText = "Error al crear instancia";
                preDetalle.InnerText = ex.ToString();
                pnlDetalle.Visible = true;
                return;
            }

            // 4) Mostrar resultado y refrescar grilla
            lblTituloDetalle.InnerText = "Crear instancia + ejecutar";
            preDetalle.InnerText =
                "Instancia creada y ejecutada.\r\n" +
                "WF_DefinicionId = " + defId + "\r\n" +
                "WF_InstanciaId  = " + nuevaInstId;

            pnlDetalle.Visible = true;

            CargarInstancias();
        }


    }
}
