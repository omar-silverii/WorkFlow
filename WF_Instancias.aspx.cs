using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web;
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

                // Acepto varios nombres de parámetro:
                // ?defId=28  (viejo)
                // ?WF_DefinicionId=28  (el que estás usando desde WF_Tarea_Detalle)
                string defIdQS =
                    Request.QueryString["defId"]
                    ?? Request.QueryString["WF_DefinicionId"];

                if (!string.IsNullOrEmpty(defIdQS))
                {
                    ViewState["InstanciaSeleccionada"] = defIdQS;

                    ListItem li = ddlDef.Items.FindByValue(defIdQS);
                    if (li != null)
                    {
                        ddlDef.ClearSelection();
                        li.Selected = true;
                    }
                }
                else
                {
                    ViewState["InstanciaSeleccionada"] = null;
                }
                CargarInstancias();

                // Si venimos desde tareas, habilitamos botón "Volver a tareas"
                var returnTo = Request.QueryString["returnTo"];
                if (!string.IsNullOrWhiteSpace(returnTo) && returnTo.Equals("tareas", StringComparison.OrdinalIgnoreCase))
                {
                    lnkBackTareas.Visible = true;
                    lnkBackTareas.NavigateUrl = "WF_Gerente_Tareas.aspx";
                }
                else
                {
                    // Si querés, lo mostramos siempre:
                    lnkBackTareas.Visible = true;
                    lnkBackTareas.NavigateUrl = "WF_Gerente_Tareas.aspx";
                }

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
                SELECT i.Id, i.WF_DefinicionId, i.Estado, i.FechaInicio, i.FechaFin,
                i.DatosContexto
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

        protected void gvInst_RowDataBound(object sender, GridViewRowEventArgs e) {
            if (e.Row.RowType != DataControlRowType.DataRow)
                return;

            // 1) Obtener el DataRowView (asumiendo que el DataSource es un DataTable/DataView)
            var drv = e.Row.DataItem as DataRowView;
            if (drv == null)
                return;

            string estado = Convert.ToString(drv["Estado"]);
            string datosContexto = drv["DatosContexto"] as string;

            long? instSel = null;
            if (ViewState["InstanciaSeleccionada"] != null &&
                long.TryParse(ViewState["InstanciaSeleccionada"].ToString(), out var tmp))
            {
                instSel = tmp;
            }

            long idInstanciaFila = Convert.ToInt64(drv["Id"]);

            // 2) Buscar el Label de la columna Error
            var lblError = e.Row.FindControl("lblErrorMsg") as Label;
            if (lblError == null)
                return;

            // Si no está en estado 'Error', no mostramos nada
            if (!string.Equals(estado, "Error", StringComparison.OrdinalIgnoreCase))
            {
                lblError.Text = string.Empty;
            }
            else
            {
                // 3) Solo si Estado = 'Error', tratamos de leer DatosContexto.error.message
                string mensaje = null;

                if (!string.IsNullOrWhiteSpace(datosContexto))
                {
                    try
                    {
                        var root = JObject.Parse(datosContexto);
                        // Buscamos error.message, según lo que guarda WorkflowRuntime
                        mensaje = (string)(root["error"]?["message"]);
                    }
                    catch
                    {
                        // Si el JSON vino roto, al menos indicamos algo
                        mensaje = "(error sin detalle JSON)";
                    }
                }

                lblError.Text = mensaje ?? "(error sin detalle)";
            }

            // 4) Pintar la fila en rojo si está en estado Error
            if (string.Equals(estado, "Error", StringComparison.OrdinalIgnoreCase))
            {
                // Agregamos la clase de Bootstrap
                // (respetando cualquier CssClass previa)
                string cls = e.Row.CssClass ?? string.Empty;
                if (!cls.Contains("table-danger"))
                    e.Row.CssClass = (cls + " table-danger").Trim();
            }

            // 5) (Opcional) resaltar la instancia seleccionada
            if (instSel.HasValue && idInstanciaFila == instSel.Value)
            {
                // azul clarito de Bootstrap para marcar "seleccionado"
                string cls = e.Row.CssClass ?? string.Empty;
                if (!cls.Contains("table-info"))
                    e.Row.CssClass = (cls + " table-info").Trim();
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

            var html = new StringBuilder();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();

                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    if (!dr.HasRows)
                    {
                        html.Append("<div class='list-group-item'>Sin logs para esta instancia.</div>");
                    }
                    else
                    {
                        while (dr.Read())
                        {
                            DateTime fecha = dr.GetDateTime(0);
                            string nivelDb = dr["Nivel"]?.ToString() ?? "Info";
                            string mensaje = dr["Mensaje"]?.ToString() ?? "";

                            string nodoId = (dr["NodoId"] == DBNull.Value) ? "" : dr["NodoId"].ToString();
                            string nodoTipo = (dr["NodoTipo"] == DBNull.Value) ? "" : dr["NodoTipo"].ToString();

                            // Detectar "técnico" (oculto por defecto)
                            bool isTech = EsTecnico(mensaje);

                            // Nivel “derivado” (si DB viene Info pero el mensaje marca Error)
                            string nivel = DerivarNivel(nivelDb, mensaje);

                            string badgeLevel = CssBadgeNivel(nivel);
                            string nodoBadge = (!string.IsNullOrWhiteSpace(nodoId) || !string.IsNullOrWhiteSpace(nodoTipo))
                                ? $"<span class='badge text-bg-light ms-2'>{HttpUtility.HtmlEncode(nodoId)} / {HttpUtility.HtmlEncode(nodoTipo)}</span>"
                                : "";

                            string msgHtml = HttpUtility.HtmlEncode(mensaje);
                            // preservar saltos de línea si existieran
                            msgHtml = msgHtml.Replace("\r\n", "\n").Replace("\n", "<br/>");
                            msgHtml = LinkificarTareaId(msgHtml);

                            string dataText = HttpUtility.HtmlAttributeEncode($"{fecha:dd/MM/yyyy HH:mm:ss} {nivel} {nodoId} {nodoTipo} {mensaje}");

                            html.Append("<div class='list-group-item wf-log-item'");
                            html.Append($" data-level='{HttpUtility.HtmlAttributeEncode(nivel)}'");
                            html.Append($" data-tech='{(isTech ? "1" : "0")}'");
                            html.Append($" data-text='{dataText}'>");

                            html.Append("<div class='d-flex justify-content-between align-items-start'>");
                            html.Append("<div>");
                            html.Append($"<span class='badge {badgeLevel}'>{HttpUtility.HtmlEncode(nivel)}</span>");
                            html.Append(nodoBadge);
                            html.Append("</div>");
                            html.Append($"<small class='text-muted'>{fecha:dd/MM/yyyy HH:mm:ss}</small>");
                            html.Append("</div>");

                            html.Append($"<div class='mt-1'>{msgHtml}</div>");

                            html.Append("</div>");
                        }
                    }
                }
            }

            lblTituloDetalle.InnerText = "Instancia " + instId + " – Log";

            // IMPORTANTE: divLogList es runat="server"
            divLogList.InnerHtml = html.ToString();

            pnlDetalle.Visible = true;
            ScriptManager.RegisterStartupScript(this, this.GetType(),
    "wf_applyLogFilters", "setTimeout(function(){ if(window.applyLogFilters) window.applyLogFilters(); }, 0);", true);

        }

        private static bool EsTecnico(string mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje)) return false;

            // Todo esto lo ocultamos por defecto (debug/ruido)
            if (mensaje.IndexOf("[Logger DEBUG]", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (mensaje.IndexOf("raw=", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (mensaje.IndexOf("expanded=", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // Podés agregar más reglas si querés (por ahora minimal)
            return false;
        }

        private static string DerivarNivel(string nivelDb, string mensaje)
        {
            var n = (nivelDb ?? "Info").Trim();

            // Si el mensaje sugiere error, lo subimos a Error (solo para UI)
            if (!string.IsNullOrEmpty(mensaje))
            {
                if (mensaje.IndexOf("[Error]", StringComparison.OrdinalIgnoreCase) >= 0) return "Error";
                if (mensaje.IndexOf(" ERROR", StringComparison.OrdinalIgnoreCase) >= 0) return "Error";
                if (mensaje.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) return "Error";
                if (mensaje.IndexOf("[Warning]", StringComparison.OrdinalIgnoreCase) >= 0) return "Warning";
            }

            return string.IsNullOrWhiteSpace(n) ? "Info" : n;
        }

        private static string CssBadgeNivel(string nivel)
        {
            // Bootstrap 5: text-bg-*
            switch ((nivel ?? "").Trim().ToLowerInvariant())
            {
                case "error": return "text-bg-danger";
                case "warning": return "text-bg-warning";
                case "debug": return "text-bg-secondary";
                default: return "text-bg-primary"; // Info
            }
        }

        private static string LinkificarTareaId(string msgHtml)
        {
            // msgHtml ya viene HTML-encoded + <br/>, así que buscamos el patrón en texto encodeado igual.
            // Como "tareaId=20076" no tiene chars especiales, lo podemos reemplazar directo.
            // Si querés regex, también se puede.
            int idx = msgHtml.IndexOf("tareaId=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return msgHtml;

            // Reemplazo simple con regex para capturar el número
            return System.Text.RegularExpressions.Regex.Replace(
                msgHtml,
                @"tareaId=(\d+)",
                m =>
                {
                    var id = m.Groups[1].Value;
                    var url = "WF_Tareas.aspx?tareaId=" + id + "&returnTo=instancias";
                    return "tareaId=<a href='" + url + "'><b>" + id + "</b></a>";
                },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
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
