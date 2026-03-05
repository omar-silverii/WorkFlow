using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tarea_Detalle : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "TAREAS_MIS" };

        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Documentos"; } catch { }

            if (!IsPostBack)
            {
                if (!long.TryParse(Request.QueryString["id"], out var tareaId))
                {
                    MostrarError("Id de tarea inválido.");
                    return;
                }

                var userKey = (Context.User?.Identity?.Name ?? "").Trim();
                if (!PuedeAbrirTarea(tareaId, userKey))
                {
                    MostrarError("No tenés permisos para abrir esta tarea.");
                    return;
                }

                ViewState["TareaId"] = tareaId;
                CargarTarea(tareaId);
            }
        }

        private bool PuedeAbrirTarea(long tareaId, string userKey)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("dbo.WF_Tarea_PuedeAbrir", cn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@TareaId", SqlDbType.BigInt).Value = tareaId;
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey ?? "";

                cn.Open();
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return false;
                return Convert.ToBoolean(v);
            }
        }

        private void CargarTarea(long id)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT  t.Id,
        t.WF_InstanciaId,
        t.NodoId,
        t.NodoTipo,
        t.Titulo,
        t.Descripcion,
        t.RolDestino,
        t.UsuarioAsignado,
        t.Estado,
        t.Resultado,
        t.FechaCreacion,
        t.FechaVencimiento,
        t.FechaCierre,
        t.Datos
FROM    dbo.WF_Tarea      t
WHERE   t.Id = @Id;", cn))
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

                    long instanciaId = Convert.ToInt64(dr["WF_InstanciaId"]);
                    string nodoId = Convert.ToString(dr["NodoId"]);

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
                            if (obj == null) { }
                            else if (obj.observaciones != null)
                            {
                                txtObs.Text = (string)obj.observaciones;
                            }
                            else if (obj.data != null && obj.data.observaciones != null)
                            {
                                txtObs.Text = (string)obj.data.observaciones;
                            }
                        }
                        catch
                        {
                            // ignorar parse fallido
                        }
                    }

                    CargarPedidosPendientes(id, instanciaId);
                }
            }
        }
        private void CargarPedidosPendientes(long tareaIdActual, long instanciaId)
        {
            try
            {
                // 1) Obtener frameId/cycle desde Datos de la tarea actual
                string datosActual = ObtenerDatosTarea(tareaIdActual);
                if (string.IsNullOrWhiteSpace(datosActual)) { pnlPedidosPendientes.Visible = false; return; }

                string frameId = ExtraerFrameIdDesdeDatos(datosActual);
                int cycle = ExtraerCycleDesdeDatos(datosActual);

                if (string.IsNullOrWhiteSpace(frameId) || cycle <= 1)
                {
                    pnlPedidosPendientes.Visible = false;
                    return;
                }

                // 2) Buscar la última tarea rechazada del mismo frame
                var rech = ObtenerUltimaTareaRechazadaPorFrame(instanciaId, frameId, tareaIdActual);
                if (rech == null)
                {
                    pnlPedidosPendientes.Visible = false;
                    return;
                }

                // 3) Renderizar pedido
                string html = RenderPedidoPendiente(rech.Datos, rech.CerradoPor, rech.CerradoEn, rech.TareaId);

                if (string.IsNullOrWhiteSpace(html))
                {
                    pnlPedidosPendientes.Visible = false;
                    return;
                }

                litPedidosPendientes.Text = html;
                pnlPedidosPendientes.Visible = true;
            }
            catch
            {
                // Si algo falla, no rompemos la página
                pnlPedidosPendientes.Visible = false;
            }
        }

        private string ObtenerDatosTarea(long tareaId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"SELECT Datos FROM dbo.WF_Tarea WHERE Id=@Id", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = tareaId;
                cn.Open();
                return cmd.ExecuteScalar() as string;
            }
        }

        private class RechazoInfo
        {
            public long TareaId;
            public string Datos;
            public string CerradoPor;
            public DateTime? CerradoEn;
        }

        private RechazoInfo ObtenerUltimaTareaRechazadaPorFrame(long instanciaId, string frameId, long tareaIdActual)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT TOP 1
    t.Id,
    t.Datos,
    t.FechaCierre
FROM dbo.WF_Tarea t
WHERE
    t.WF_InstanciaId = @InstanciaId
    AND t.Id <> @TareaActual
    AND t.Resultado = @ResRech
    AND t.Datos LIKE @LikeFrame
ORDER BY
    ISNULL(t.FechaCierre, t.FechaCreacion) DESC,
    t.Id DESC;", cn))
            {
                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@TareaActual", SqlDbType.BigInt).Value = tareaIdActual;
                cmd.Parameters.Add("@ResRech", SqlDbType.VarChar, 50).Value = "rechazado";

                // MVP robusto: buscamos el frameId como substring JSON
                cmd.Parameters.Add("@LikeFrame", SqlDbType.NVarChar, 200).Value = "%" + frameId + "%";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;

                    var info = new RechazoInfo();
                    info.TareaId = Convert.ToInt64(dr["Id"]);
                    info.Datos = dr["Datos"] as string;
                    info.CerradoEn = dr["FechaCierre"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["FechaCierre"]);

                    // CerradoPor está dentro del JSON "data" (porque lo guardamos como meta+data)
                    info.CerradoPor = ExtraerCerradoPorDesdeDatos(info.Datos);

                    return info;
                }
            }
        }

        private string ExtraerFrameIdDesdeDatos(string datosJson)
        {
            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return null;

                if (o.wfBack != null && o.wfBack.frameId != null) return (string)o.wfBack.frameId;
                if (o.meta != null && o.meta.wfBack != null && o.meta.wfBack.frameId != null) return (string)o.meta.wfBack.frameId;

                return null;
            }
            catch { return null; }
        }

        private int ExtraerCycleDesdeDatos(string datosJson)
        {
            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return 0;

                object v = null;

                if (o.wfBack != null && o.wfBack.cycle != null) v = o.wfBack.cycle;
                else if (o.meta != null && o.meta.wfBack != null && o.meta.wfBack.cycle != null) v = o.meta.wfBack.cycle;

                if (v == null) return 0;
                return Convert.ToInt32(v);
            }
            catch { return 0; }
        }

        private string ExtraerCerradoPorDesdeDatos(string datosJson)
        {
            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return null;

                if (o.cerradoPor != null) return (string)o.cerradoPor;
                if (o.data != null && o.data.cerradoPor != null) return (string)o.data.cerradoPor;

                return null;
            }
            catch { return null; }
        }

        private string RenderPedidoPendiente(string datosRechazoJson, string cerradoPor, DateTime? cerradoEn, long tareaRechazadaId)
        {
            if (string.IsNullOrWhiteSpace(datosRechazoJson)) return null;

            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosRechazoJson);
                if (o == null) return null;

                // el pedido está en data.pedido (nuevo formato) o en pedido (viejo)
                dynamic data = (o.data != null) ? o.data : o;

                dynamic pedido = data.pedido;
                string obs = data.observaciones != null ? (string)data.observaciones : null;

                if (pedido == null && string.IsNullOrWhiteSpace(obs)) return null;

                string quien = !string.IsNullOrWhiteSpace(cerradoPor) ? cerradoPor : "—";
                string cuando = cerradoEn.HasValue ? cerradoEn.Value.ToString("dd/MM/yyyy HH:mm") : "—";

                string titulo = pedido != null && pedido.titulo != null ? (string)pedido.titulo : "Pedido";
                string detalle = pedido != null && pedido.detalle != null ? (string)pedido.detalle : null;
                string codigo = pedido != null && pedido.codigo != null ? (string)pedido.codigo : null;

                // HTML simple (sin depender de nada)
                string html = "";
                html += "<div><b>Último rechazo:</b> " + Server.HtmlEncode(quien) + " – " + Server.HtmlEncode(cuando) + "</div>";
                html += "<div class='mt-1'><b>" + Server.HtmlEncode(titulo) + "</b></div>";

                if (!string.IsNullOrWhiteSpace(codigo))
                    html += "<div><b>Código:</b> " + Server.HtmlEncode(codigo) + "</div>";

                if (!string.IsNullOrWhiteSpace(detalle))
                    html += "<div>" + Server.HtmlEncode(detalle) + "</div>";

                if (!string.IsNullOrWhiteSpace(obs))
                    html += "<div class='mt-1'><b>Observaciones:</b> " + Server.HtmlEncode(obs) + "</div>";

                html += "<div class='mt-2'><a href='WF_Tarea_Detalle.aspx?id=" + tareaRechazadaId + "'>Ver tarea rechazada</a></div>";
                return html;
            }
            catch
            {
                return null;
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

            string usuarioActual = Context.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = Environment.UserName;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = "workflow.ui";

            // Si el usuario pegó JSON (empieza con "{"), lo respetamos como "data"
            // y solo aseguramos cerradoPor/cerradoEn si faltan.
            string datosJson;

            string obsTrim = observaciones.TrimStart();
            if (obsTrim.StartsWith("{"))
            {
                try
                {
                    dynamic o = JsonConvert.DeserializeObject(observaciones);
                    if (o != null)
                    {
                        if (o.cerradoPor == null) o.cerradoPor = usuarioActual;
                        if (o.cerradoEn == null) o.cerradoEn = DateTime.Now;
                        datosJson = JsonConvert.SerializeObject(o, Formatting.None);
                    }
                    else
                    {
                        datosJson = JsonConvert.SerializeObject(new { observaciones, cerradoPor = usuarioActual, cerradoEn = DateTime.Now }, Formatting.None);
                    }
                }
                catch
                {
                    // Si pegó algo que parece JSON pero está mal, caemos al formato simple
                    datosJson = JsonConvert.SerializeObject(new { observaciones, cerradoPor = usuarioActual, cerradoEn = DateTime.Now }, Formatting.None);
                }
            }
            else
            {
                datosJson = JsonConvert.SerializeObject(new { observaciones, cerradoPor = usuarioActual, cerradoEn = DateTime.Now }, Formatting.None);
            }

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
                if (ex is InvalidOperationException &&
                    ex.Message.StartsWith("WF_Tarea ya fue completada"))
                {
                    MostrarError("La tarea ya fue completada o cancelada por otro usuario.");
                }
                else
                {
                    MostrarError("Error al reanudar el workflow: " + ex.Message);
                }
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
