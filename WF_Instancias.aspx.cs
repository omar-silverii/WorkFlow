using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
                chkMostrarFinalizados.Checked = false;
                ddlEstado.SelectedValue = "";
                txtBuscar.Text = "";
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
            int defId = 0;
            int.TryParse(Convert.ToString(ddlDef.SelectedValue), out defId);

            bool mostrarFinalizados = chkMostrarFinalizados.Checked;
            string estado = Convert.ToString(ddlEstado.SelectedValue ?? "").Trim();
            string q = (txtBuscar.Text ?? "").Trim();

            var where = new List<string>();
            where.Add("WF_DefinicionId = @DefId");

            // Por defecto: ocultar finalizados
            if (!mostrarFinalizados)
                where.Add("Estado <> 'Finalizado'");

            // Filtro por estado
            if (!string.IsNullOrWhiteSpace(estado))
                where.Add("Estado = @Estado");

            // Buscar (SIEMPRE por DatosContexto; y si es numérico, también por Id contiene)
            bool qEsNumero = false;
            long dummy;
            if (!string.IsNullOrWhiteSpace(q))
                qEsNumero = long.TryParse(q, out dummy);

            if (!string.IsNullOrWhiteSpace(q))
            {
                if (qEsNumero)
                    where.Add("(CONVERT(varchar(30), Id) LIKE @Q OR DatosContexto LIKE @Q)");
                else
                    where.Add("(DatosContexto LIKE @Q)");
            }

            string sql = @"
SELECT TOP (500)
    Id,
    WF_DefinicionId,
    Estado,
    FechaInicio,
    FechaFin,
    DatosContexto
FROM WF_Instancia
WHERE " + string.Join(" AND ", where) + @"
ORDER BY Id DESC;";

            using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;

                if (!string.IsNullOrWhiteSpace(estado))
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = estado;

                if (!string.IsNullOrWhiteSpace(q))
                    cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value = "%" + q + "%";

                var dt = new DataTable();
                cn.Open();
                dt.Load(cmd.ExecuteReader());

                gvInst.DataSource = dt;
                gvInst.DataBind();
            }
        }



        protected void ddlDef_SelectedIndexChanged(object sender, EventArgs e)
        {
            CargarInstancias();
        }

        protected void btnRefrescar_Click(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
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

        protected void gvInst_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;

            var lbl = e.Row.FindControl("lblErrorMsg") as Label;
            if (lbl == null) return;

            lbl.Text = "";

            // Estado (para pintar)
            var estadoObj = DataBinder.Eval(e.Row.DataItem, "Estado");
            var estado = Convert.ToString(estadoObj ?? "");

            // Intentar extraer mensaje de error desde DatosContexto (JSON)
            var datosCtx = Convert.ToString(DataBinder.Eval(e.Row.DataItem, "DatosContexto") ?? "");

            string msg = TryExtractErrorMessageFromDatosContexto(datosCtx);

            if (!string.IsNullOrWhiteSpace(msg))
                lbl.Text = msg;

            // Opcional: resaltar según estado
            if (estado.Equals("Error", StringComparison.OrdinalIgnoreCase))
                lbl.CssClass = "text-danger small";
            else
                lbl.CssClass = "text-muted small";
        }

        private static string TryExtractErrorMessageFromDatosContexto(string datosContexto)
        {
            if (string.IsNullOrWhiteSpace(datosContexto)) return null;

            try
            {
                var jo = Newtonsoft.Json.Linq.JObject.Parse(datosContexto);

                // soporta varias formas (por si cambió el formato)
                var msg =
                    (string)jo.SelectToken("error.message") ??
                    (string)jo.SelectToken("wf.error.message") ??
                    (string)jo.SelectToken("mensajeError") ??
                    (string)jo.SelectToken("error") ??
                    null;

                if (string.IsNullOrWhiteSpace(msg)) return null;

                msg = msg.Trim();
                if (msg.Length > 180) msg = msg.Substring(0, 180) + "...";
                return msg;
            }
            catch
            {
                // Si no es JSON, no mostramos nada
                return null;
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

        protected void chkMostrarFinalizados_CheckedChanged(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected void ddlEstado_SelectedIndexChanged(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvInst.PageIndex = 0;
            CargarInstancias();
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
