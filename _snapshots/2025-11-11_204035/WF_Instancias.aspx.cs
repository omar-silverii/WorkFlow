using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;

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

            // dentro de CargarInstancias()
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

                // Envolver la operación async correctamente para WebForms
                RegisterAsyncTask(new PageAsyncTask(async ct =>
                {
                    nuevaId = await Intranet.WorkflowStudio.Runtime.WorkflowRuntime
                                 .ReejecutarInstanciaAsync(instId, usuario);
                }));

                // Ejecutar las tareas registradas en ESTE postback
                ExecuteRegisteredAsyncTasks();

                if (nuevaId.HasValue)
                {
                    lblTituloDetalle.InnerText = "Nueva instancia creada: " + nuevaId.Value;
                    preDetalle.InnerText = "Se re-ejecutó la instancia " + instId +
                                           " → nueva Id = " + nuevaId.Value;
                    pnlDetalle.Visible = true;

                    // refrescá la grilla
                    CargarInstancias();
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
        // NUEVO: crear instancia dummy
        // =========================
        protected void btnCrearInst_Click(object sender, EventArgs e)
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
                return;
            }

            int instId;
            using (SqlConnection cn = new SqlConnection(Cnn))
            {
                cn.Open();

                // 2) crear la instancia
                using (SqlCommand cmd = new SqlCommand(@"
                    INSERT INTO dbo.WF_Instancia
                        (WF_DefinicionId, Estado, FechaInicio, DatosEntrada, DatosContexto)
                    VALUES
                        (@DefId, @Estado, GETDATE(), @DatosEntrada, @DatosContexto);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
                {
                    cmd.Parameters.AddWithValue("@DefId", defId);
                    cmd.Parameters.AddWithValue("@Estado", "EnCurso");
                    cmd.Parameters.AddWithValue("@DatosEntrada", (object)"{ \"demo\": true }");
                    cmd.Parameters.AddWithValue("@DatosContexto", (object)"{}");
                    instId = (int)cmd.ExecuteScalar();
                }

                // 3) escribir un log inicial
                using (SqlCommand cmdLog = new SqlCommand(@"
                    INSERT INTO dbo.WF_InstanciaLog
                        (WF_InstanciaId, FechaLog, Nivel, Mensaje)
                    VALUES
                        (@InstId, GETDATE(), 'Info', 'Instancia creada manualmente desde WF_Instancias.aspx');", cn))
                {
                    cmdLog.Parameters.AddWithValue("@InstId", instId);
                    cmdLog.ExecuteNonQuery();
                }
            }

            // 4) refrescar listado
            CargarInstancias();
        }
    }
}
