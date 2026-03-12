using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.UI.WebControls;


namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tarea_Detalle : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "TAREAS_MIS" };

        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        private long TareaIdActual => (ViewState["TareaId"] is long v) ? v : 0;

        private long InstanciaIdActual
        {
            get { return (ViewState["InstanciaId"] is long v) ? v : 0; }
            set { ViewState["InstanciaId"] = value; }
        }

        private string ResolveBackUrl()
        {
            string src = (Request.QueryString["src"] ?? "").Trim().ToLowerInvariant();

            if (src == "gerencia")
                return "WF_Gerente_Tareas.aspx";

            return "WF_Tareas.aspx";
        }

        private class AdjRow
        {
            public string Tipo { get; set; }
            public string FileName { get; set; }
            public string Fecha { get; set; }
            public string Url { get; set; }
            public string StoredFileName { get; set; }
            public string TareaIdDoc { get; set; }
            public bool PuedeEliminar { get; set; }
        }

        private class ObservacionInstanciaRow
        {
            public long TareaId;
            public string NodoId;
            public string Datos;
            public string CerradoPor;
            public DateTime? CerradoEn;
            public string Resultado;
        }

        private sealed class VolverAItem
        {
            public string NodeId { get; set; }
            public string Texto { get; set; }
            public int Distancia { get; set; }
        }

        private void CargarOpcionesVolverA(long tareaId)
        {
            ddlVolverA.Items.Clear();

            var items = ObtenerOpcionesVolverA(tareaId);
            foreach (var it in items)
                ddlVolverA.Items.Add(new ListItem(it.Texto, it.NodeId));

            if (ddlVolverA.Items.Count == 0)
                ddlVolverA.Items.Add(new ListItem("Inicio", "__inicio__"));

            if (ddlVolverA.Items.FindByValue("__inicio__") == null)
                ddlVolverA.Items.Add(new ListItem("Inicio", "__inicio__"));
        }

        private List<VolverAItem> ObtenerOpcionesVolverA(long tareaId)
        {
            var result = new List<VolverAItem>();

            string nodoActual = null;
            string jsonDef = null;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT TOP 1
    t.NodoId,
    d.JsonDef
FROM dbo.WF_Tarea t
INNER JOIN dbo.WF_Instancia i ON i.Id = t.WF_InstanciaId
INNER JOIN dbo.WF_Definicion d ON d.Id = i.WF_DefinicionId
WHERE t.Id = @TareaId;", cn))
            {
                cmd.Parameters.AddWithValue("@TareaId", tareaId);
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (dr.Read())
                    {
                        nodoActual = dr.IsDBNull(0) ? null : dr.GetString(0);
                        jsonDef = dr.IsDBNull(1) ? null : dr.GetString(1);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(nodoActual) || string.IsNullOrWhiteSpace(jsonDef))
                return result;

            var root = JObject.Parse(jsonDef);
            var nodes = root["Nodes"] as JObject;
            var edges = root["Edges"] as JArray;

            if (nodes == null || edges == null)
                return result;

            var incoming = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in edges.OfType<JObject>())
            {
                var from = Convert.ToString(e["From"] ?? "");
                var to = Convert.ToString(e["To"] ?? "");

                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                    continue;

                if (!incoming.ContainsKey(to))
                    incoming[to] = new List<string>();

                incoming[to].Add(from);
            }

            var visitados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var agregados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cola = new Queue<Tuple<string, int>>();

            cola.Enqueue(Tuple.Create(nodoActual, 0));
            visitados.Add(nodoActual);

            while (cola.Count > 0)
            {
                var item = cola.Dequeue();
                var current = item.Item1;
                var dist = item.Item2;

                if (!incoming.ContainsKey(current))
                    continue;

                foreach (var prev in incoming[current])
                {
                    if (!visitados.Add(prev))
                        continue;

                    cola.Enqueue(Tuple.Create(prev, dist + 1));

                    var n = nodes[prev] as JObject;
                    if (n == null) continue;

                    var tipo = Convert.ToString(n["Type"] ?? "");
                    if (!string.Equals(tipo, "human.task", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!agregados.Add(prev))
                        continue;

                    var label = Convert.ToString(n["Label"] ?? "");
                    var titulo = Convert.ToString(n["Parameters"]?["titulo"] ?? "");
                    var rol = Convert.ToString(n["Parameters"]?["rol"] ?? "");

                    var baseTexto = !string.IsNullOrWhiteSpace(label) ? label
                                  : !string.IsNullOrWhiteSpace(titulo) ? titulo
                                  : !string.IsNullOrWhiteSpace(rol) ? rol
                                  : prev;

                    var texto = baseTexto + " (" + prev + ")";

                    result.Add(new VolverAItem
                    {
                        NodeId = prev,
                        Texto = texto,
                        Distancia = dist + 1
                    });
                }
            }

            return result
                .OrderBy(x => x.Distancia)
                .ThenBy(x => x.Texto)
                .ToList();
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
FROM    dbo.WF_Tarea t
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

                    InstanciaIdActual = instanciaId;

                    lblId.Text = dr["Id"].ToString();
                    lblInstancia.Text = dr["WF_InstanciaId"].ToString();
                    lblEstado.Text = Convert.ToString(dr["Estado"]);
                    lblTipo.Text = Convert.ToString(dr["NodoTipo"]);

                    txtTitulo.Text = Convert.ToString(dr["Titulo"]);
                    txtDescripcion.Text = Convert.ToString(dr["Descripcion"]);
                    txtRol.Text = Convert.ToString(dr["RolDestino"]);
                    txtUsuario.Text = Convert.ToString(dr["UsuarioAsignado"]);

                    string estado = Convert.ToString(dr["Estado"]);

                    if (estado.Equals("Completada", StringComparison.OrdinalIgnoreCase) ||
                        estado.Equals("Cancelada", StringComparison.OrdinalIgnoreCase))
                    {
                        ddlResultado.Enabled = false;
                        txtObs.Enabled = false;
                        btnCompletar.Enabled = false;
                        lblInfo.Text = "La tarea ya está cerrada.";
                        btnAdjuntar.Enabled = false;   // ✔️
                        fuAdjunto.Enabled = false;     // ✔️
                        
                    }

                    var res = dr["Resultado"] as string;
                    if (!string.IsNullOrEmpty(res) && ddlResultado.Items.FindByValue(res) != null)
                        ddlResultado.SelectedValue = res;

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
                    BindAdjuntos();
                    CargarOpcionesVolverA(id);
                }
            }
        }

        protected void btnAdjuntar_Click(object sender, EventArgs e)
        {
            try
            {
                if (!fuAdjunto.HasFile)
                {
                    ShowAdjMsg("Seleccione un archivo para adjuntar.", isError: true);
                    return;
                }

                var tareaId = TareaIdActual;
                var instanciaId = InstanciaIdActual;

                if (tareaId <= 0 || instanciaId <= 0)
                {
                    ShowAdjMsg("No se pudo resolver Instancia/Tarea.", isError: true);
                    return;
                }

                var baseDir = Server.MapPath("~/App_Data/WFUploads");
                var dir = Path.Combine(baseDir, instanciaId.ToString(), tareaId.ToString());
                Directory.CreateDirectory(dir);

                var originalName = Path.GetFileName(fuAdjunto.FileName);
                var safeName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "__" + originalName;
                var fullPath = Path.Combine(dir, safeName);

                fuAdjunto.SaveAs(fullPath);

                var ext = (Path.GetExtension(originalName) ?? "").Trim().TrimStart('.').ToUpperInvariant();
                var tipo = string.IsNullOrWhiteSpace(ext) ? "Archivo" : ext;

                var user = (Context.User?.Identity?.Name ?? "").Trim();

                var url = ResolveUrl("~/API/WF_Upload_Get.ashx") +
                      "?inst=" + instanciaId +
                      "&tarea=" + tareaId +
                      "&f=" + HttpUtility.UrlEncode(safeName);

                AppendAttachmentToDatosContexto(instanciaId, tareaId, originalName, safeName, tipo, url, user);

                ShowAdjMsg("Adjunto cargado OK.", isError: false);
                BindAdjuntos();
            }
            catch (Exception ex)
            {
                ShowAdjMsg("Error al adjuntar: " + ex.Message, isError: true);
            }
        }

        protected void rptAdjuntos_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "delAdjunto", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var tareaIdActual = TareaIdActual;
                var instanciaId = InstanciaIdActual;

                if (tareaIdActual <= 0 || instanciaId <= 0)
                {
                    ShowAdjMsg("No se pudo resolver Instancia/Tarea.", isError: true);
                    return;
                }

                if (!string.Equals(lblEstado.Text ?? "", "Pendiente", StringComparison.OrdinalIgnoreCase))
                {
                    ShowAdjMsg("La tarea ya no está abierta para eliminar adjuntos.", isError: true);
                    return;
                }

                var arg = Convert.ToString(e.CommandArgument ?? "").Trim();
                if (string.IsNullOrWhiteSpace(arg) || arg.IndexOf('|') < 0)
                {
                    ShowAdjMsg("No se pudo resolver el adjunto a eliminar.", isError: true);
                    return;
                }

                var parts = arg.Split(new[] { '|' }, 2);
                var safeName = (parts[0] ?? "").Trim();
                var tareaIdDoc = (parts[1] ?? "").Trim();

                if (string.IsNullOrWhiteSpace(safeName) || string.IsNullOrWhiteSpace(tareaIdDoc))
                {
                    ShowAdjMsg("No se pudo resolver el adjunto a eliminar.", isError: true);
                    return;
                }

                if (!string.Equals(tareaIdDoc, tareaIdActual.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    ShowAdjMsg("Solo se pueden eliminar adjuntos cargados en la tarea actual.", isError: true);
                    return;
                }

                var baseDir = Server.MapPath("~/App_Data/WFUploads");
                var fullPath = Path.Combine(baseDir, instanciaId.ToString(), tareaIdActual.ToString(), safeName);

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                RemoveAttachmentFromDatosContexto(instanciaId, tareaIdActual, safeName);

                ShowAdjMsg("Adjunto eliminado OK.", isError: false);
                BindAdjuntos();
            }
            catch (Exception ex)
            {
                ShowAdjMsg("Error al eliminar adjunto: " + ex.Message, isError: true);
            }
        }

        private void RemoveAttachmentFromDatosContexto(long instanciaId, long tareaId, string safeName)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmdSel = new SqlCommand("SELECT DatosContexto FROM dbo.WF_Instancia WHERE Id=@Id", cn))
            {
                cmdSel.Parameters.AddWithValue("@Id", instanciaId);

                cn.Open();

                var raw = Convert.ToString(cmdSel.ExecuteScalar() ?? "");
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var root = JObject.Parse(raw);

                var estado = root["estado"] as JObject;
                if (estado == null) return;

                var biz = estado["biz"] as JObject;
                if (biz == null) return;

                var caseObj = biz["case"] as JObject;
                if (caseObj == null) return;

                var arr = caseObj["attachments"] as JArray;
                if (arr == null || arr.Count == 0) return;

                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var item = arr[i] as JObject;
                    if (item == null) continue;

                    var itemTareaId = Convert.ToString(item["tareaId"] ?? "");
                    var itemStored = Convert.ToString(item["storedFileName"] ?? "");
                    var itemUrl = Convert.ToString(item["viewerUrl"] ?? "");

                    if (itemTareaId == tareaId.ToString() &&
                        (
                            string.Equals(itemStored, safeName, StringComparison.OrdinalIgnoreCase) ||
                            itemUrl.IndexOf(safeName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            itemUrl.IndexOf(HttpUtility.UrlEncode(safeName), StringComparison.OrdinalIgnoreCase) >= 0
                        ))
                    {
                        arr.RemoveAt(i);
                    }
                }

                caseObj["attachments"] = arr;
                biz["case"] = caseObj;
                estado["biz"] = biz;
                root["estado"] = estado;

                using (var cmdUpd = new SqlCommand("UPDATE dbo.WF_Instancia SET DatosContexto=@Datos WHERE Id=@Id", cn))
                {
                    cmdUpd.Parameters.AddWithValue("@Id", instanciaId);
                    cmdUpd.Parameters.AddWithValue("@Datos", root.ToString(Newtonsoft.Json.Formatting.None));
                    cmdUpd.ExecuteNonQuery();
                }
            }
        }

        private void CargarPedidosPendientes(long tareaIdActual, long instanciaId)
        {
            try
            {
                string html = RenderObservacionesInstancia(instanciaId, tareaIdActual);

                if (string.IsNullOrWhiteSpace(html))
                {
                    pnlPedidosPendientes.Visible = false;
                    litPedidosPendientes.Text = "";
                    return;
                }

                litPedidosPendientes.Text = html;
                pnlPedidosPendientes.Visible = true;
            }
            catch
            {
                pnlPedidosPendientes.Visible = false;
                litPedidosPendientes.Text = "";
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
                cmd.Parameters.Add("@LikeFrame", SqlDbType.NVarChar, 200).Value = "%" + frameId + "%";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;

                    var info = new RechazoInfo();
                    info.TareaId = Convert.ToInt64(dr["Id"]);
                    info.Datos = dr["Datos"] as string;
                    info.CerradoEn = dr["FechaCierre"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["FechaCierre"]);
                    info.CerradoPor = ExtraerCerradoPorDesdeDatos(info.Datos);

                    if (!info.CerradoEn.HasValue)
                        info.CerradoEn = ExtraerCerradoEnDesdeDatos(info.Datos);

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
                if (o.pedido != null && o.pedido.cerradoPor != null) return (string)o.pedido.cerradoPor;
                if (o.data != null && o.data.pedido != null && o.data.pedido.cerradoPor != null) return (string)o.data.pedido.cerradoPor;

                return null;
            }
            catch { return null; }
        }
        private string ExtraerReturnToDesdeDatos(string datosJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(datosJson)) return null;

                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return null;

                if (o.wfBack != null && o.wfBack.returnToNodeId != null)
                    return (string)o.wfBack.returnToNodeId;

                if (o.meta != null && o.meta.wfBack != null && o.meta.wfBack.returnToNodeId != null)
                    return (string)o.meta.wfBack.returnToNodeId;

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string ResolveEstadoNegocioCierre(string resultado, string returnToNodeId)
        {
            var r = (resultado ?? "").Trim().ToLowerInvariant();

            if (r == "aprobado" || r == "aprobada" || r == "approve" || r == "approved" || r == "ok")
                return "Aprobada";

            if (r == "rechazado" || r == "rechazada" || r == "reject" || r == "rejected")
            {
                if (!string.IsNullOrWhiteSpace(returnToNodeId))
                    return "Observada";

                return "Rechazada";
            }

            return null;
        }
        private DateTime? ExtraerCerradoEnDesdeDatos(string datosJson)
        {
            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return null;

                object v = null;

                if (o.cerradoEn != null) v = o.cerradoEn;
                else if (o.data != null && o.data.cerradoEn != null) v = o.data.cerradoEn;
                else if (o.pedido != null && o.pedido.cerradoEn != null) v = o.pedido.cerradoEn;
                else if (o.data != null && o.data.pedido != null && o.data.pedido.cerradoEn != null) v = o.data.pedido.cerradoEn;

                if (v == null) return null;

                DateTime dt;
                return DateTime.TryParse(Convert.ToString(v), out dt) ? (DateTime?)dt : null;
            }
            catch { return null; }
        }

        private string ExtraerObservacionDesdeDatos(string datosJson)
        {
            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosJson);
                if (o == null) return null;

                if (o.observaciones != null) return (string)o.observaciones;
                if (o.data != null && o.data.observaciones != null) return (string)o.data.observaciones;
                if (o.pedido != null && o.pedido.observaciones != null) return (string)o.pedido.observaciones;
                if (o.data != null && o.data.pedido != null && o.data.pedido.observaciones != null) return (string)o.data.pedido.observaciones;

                return null;
            }
            catch { return null; }
        }

        private System.Collections.Generic.List<ObservacionInstanciaRow> ObtenerObservacionesInstancia(long instanciaId, long tareaIdActual)
        {
            var list = new System.Collections.Generic.List<ObservacionInstanciaRow>();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT
    t.Id,
    t.NodoId,
    t.Datos,
    t.FechaCierre,
    t.Resultado
FROM dbo.WF_Tarea t
WHERE
    t.WF_InstanciaId = @InstanciaId
    AND t.NodoTipo = 'human.task'
    AND t.Estado = 'Completada'
ORDER BY
    ISNULL(t.FechaCierre, t.FechaCreacion) ASC,
    t.Id ASC;", cn))
            {
                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        var row = new ObservacionInstanciaRow();
                        row.TareaId = Convert.ToInt64(dr["Id"]);
                        row.NodoId = dr["NodoId"] == DBNull.Value ? "" : Convert.ToString(dr["NodoId"]);
                        row.Datos = dr["Datos"] as string;
                        row.CerradoEn = dr["FechaCierre"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["FechaCierre"]);
                        row.CerradoPor = ExtraerCerradoPorDesdeDatos(row.Datos);
                        row.Resultado = dr["Resultado"] == DBNull.Value ? "" : Convert.ToString(dr["Resultado"]);

                        if (!row.CerradoEn.HasValue)
                            row.CerradoEn = ExtraerCerradoEnDesdeDatos(row.Datos);

                        var obs = ExtraerObservacionDesdeDatos(row.Datos);
                        if (string.IsNullOrWhiteSpace(obs))
                            continue;

                        list.Add(row);
                    }
                }
            }

            return list;
        }

        private string RenderObservacionesInstancia(long instanciaId, long tareaIdActual)
        {
            var list = ObtenerObservacionesInstancia(instanciaId, tareaIdActual);
            if (list == null || list.Count == 0) return null;

            string html = "";
            html += "<div class='table-responsive'>";
            html += "<table class='table table-sm table-hover align-middle mb-0'>";
            html += "<thead>";
            html += "<tr>";
            html += "<th style='width:140px'>Fecha</th>";
            html += "<th style='width:180px'>Usuario</th>";
            html += "<th style='width:90px'>Nodo</th>";
            html += "<th style='width:110px'>Resultado</th>";
            html += "<th>Observación</th>";
            html += "</tr>";
            html += "</thead>";
            html += "<tbody>";

            foreach (var it in list)
            {
                string rowHtml = RenderObservacionInstanciaRow(it);
                if (!string.IsNullOrWhiteSpace(rowHtml))
                    html += rowHtml;
            }

            html += "</tbody>";
            html += "</table>";
            html += "</div>";
            html += "<div class='mt-2'><a href='#adjuntos'>Ir a adjuntar documentación</a></div>";

            return html;
        }

        private string RenderObservacionInstanciaRow(ObservacionInstanciaRow it)
        {
            if (it == null || string.IsNullOrWhiteSpace(it.Datos)) return null;

            try
            {
                string obs = ExtraerObservacionDesdeDatos(it.Datos);
                if (string.IsNullOrWhiteSpace(obs)) return null;

                string quien = !string.IsNullOrWhiteSpace(it.CerradoPor) ? it.CerradoPor : "—";
                string cuando = it.CerradoEn.HasValue ? it.CerradoEn.Value.ToString("dd/MM/yyyy HH:mm") : "—";
                string nodo = !string.IsNullOrWhiteSpace(it.NodoId) ? it.NodoId : "—";
                string resultado = !string.IsNullOrWhiteSpace(it.Resultado) ? it.Resultado : "—";

                string html = "";
                html += "<tr>";
                html += "<td>" + Server.HtmlEncode(cuando) + "</td>";
                html += "<td>" + Server.HtmlEncode(quien) + "</td>";
                html += "<td>" + Server.HtmlEncode(nodo) + "</td>";
                html += "<td>" + Server.HtmlEncode(resultado) + "</td>";
                html += "<td>" + Server.HtmlEncode(obs).Replace("\r\n", "<br/>").Replace("\n", "<br/>") + "</td>";
                html += "</tr>";

                return html;
            }
            catch
            {
                return null;
            }
        }

        private string RenderPedidoPendiente(string datosRechazoJson, string cerradoPor, DateTime? cerradoEn, long tareaRechazadaId)
        {
            if (string.IsNullOrWhiteSpace(datosRechazoJson)) return null;

            try
            {
                dynamic o = JsonConvert.DeserializeObject(datosRechazoJson);
                if (o == null) return null;

                dynamic data = (o.data != null) ? o.data : o;
                dynamic pedido = (data.pedido != null) ? data.pedido : null;

                string obs = null;
                if (pedido != null && pedido.observaciones != null)
                    obs = (string)pedido.observaciones;
                else if (data.observaciones != null)
                    obs = (string)data.observaciones;
                else if (o.observaciones != null)
                    obs = (string)o.observaciones;

                string quien = !string.IsNullOrWhiteSpace(cerradoPor) ? cerradoPor : "—";
                string cuando = cerradoEn.HasValue ? cerradoEn.Value.ToString("dd/MM/yyyy HH:mm") : "—";

                string titulo = "Pedido de reproceso";
                if (pedido != null && pedido.titulo != null && !string.IsNullOrWhiteSpace((string)pedido.titulo))
                    titulo = (string)pedido.titulo;

                string detalle = null;
                if (pedido != null && pedido.detalle != null)
                    detalle = (string)pedido.detalle;

                string codigo = null;
                if (pedido != null && pedido.codigo != null)
                    codigo = (string)pedido.codigo;

                if (string.IsNullOrWhiteSpace(obs) && string.IsNullOrWhiteSpace(detalle) && string.IsNullOrWhiteSpace(codigo))
                    return null;

                string html = "";
                html += "<div><b>Solicitado por:</b> " + Server.HtmlEncode(quien) + "</div>";
                html += "<div><b>Fecha:</b> " + Server.HtmlEncode(cuando) + "</div>";
                html += "<div class='mt-2'><b>" + Server.HtmlEncode(titulo) + "</b></div>";

                if (!string.IsNullOrWhiteSpace(codigo))
                    html += "<div class='mt-1'><b>Código:</b> " + Server.HtmlEncode(codigo) + "</div>";

                if (!string.IsNullOrWhiteSpace(detalle))
                    html += "<div class='mt-1'><b>Detalle:</b> " + Server.HtmlEncode(detalle) + "</div>";

                if (!string.IsNullOrWhiteSpace(obs))
                    html += "<div class='mt-2'><b>Observaciones:</b><br/>" + Server.HtmlEncode(obs).Replace("\r\n", "<br/>").Replace("\n", "<br/>") + "</div>";

                html += "<div class='mt-2'><a href='#adjuntos'>Ir a adjuntar documentación</a></div>";

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
            string datosTareaActual = ObtenerDatosTarea(tareaId);
            string returnToNodeId = ExtraerReturnToDesdeDatos(datosTareaActual);
            string estadoNegocioCierre = ResolveEstadoNegocioCierre(resultado, returnToNodeId);

            string observaciones = txtObs.Text ?? string.Empty;
            string volverA = string.Equals(resultado, "rechazado", StringComparison.OrdinalIgnoreCase)
                ? (ddlVolverA.SelectedValue ?? "").Trim()
                : null;
            if (string.Equals(resultado, "rechazado", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(volverA))
            {
                MostrarError("Seleccione a qué etapa desea volver.");
                return;
            }

            string usuarioActual = Context.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = Environment.UserName;
            if (string.IsNullOrWhiteSpace(usuarioActual))
                usuarioActual = "workflow.ui";

            string datosJson;

            Func<object> buildDatos = () => new
            {
                observaciones,
                cerradoPor = usuarioActual,
                cerradoEn = DateTime.Now,
                estadoNegocio = estadoNegocioCierre,
                volverA = string.IsNullOrWhiteSpace(volverA) ? null : volverA
            };

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
                        if (o.estadoNegocio == null && !string.IsNullOrWhiteSpace(estadoNegocioCierre)) o.estadoNegocio = estadoNegocioCierre;
                        if (o.volverA == null && !string.IsNullOrWhiteSpace(volverA)) o.volverA = volverA;

                        datosJson = JsonConvert.SerializeObject(o, Formatting.None);
                    }
                    else
                    {
                        datosJson = JsonConvert.SerializeObject(buildDatos(), Formatting.None);
                    }
                }
                catch
                {
                    datosJson = JsonConvert.SerializeObject(buildDatos(), Formatting.None);
                }
            }
            else
            {
                datosJson = JsonConvert.SerializeObject(buildDatos(), Formatting.None);
            }

            try
            {
                await WorkflowRuntime.ReanudarDesdeTareaAsync(
                    tareaId,
                    resultado,
                    datosJson,
                    usuarioActual
                );

                lblInfo.Text = "Tarea completada correctamente. Volviendo...";
                CargarTarea(tareaId);

                string backUrl = ResolveBackUrl();
                string js = "setTimeout(function(){ window.location='" + ResolveUrl("~/" + backUrl) + "'; }, 1800);";
                ClientScript.RegisterStartupScript(this.GetType(), "volverAutomatico", js, true);
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
            Response.Redirect(ResolveBackUrl(), false);
        }

        private void MostrarError(string mensaje)
        {
            pnlDatos.Visible = false;
            pnlError.Visible = true;
            litError.Text = Server.HtmlEncode(mensaje);
        }

        private void ShowAdjMsg(string msg, bool isError)
        {
            pnlAdjuntosMsg.Visible = true;
            pnlAdjuntosMsg.Controls.Clear();
            pnlAdjuntosMsg.Controls.Add(new Literal
            {
                Text = "<div class='alert " + (isError ? "alert-danger" : "alert-success") + " mb-2'>" +
                       Server.HtmlEncode(msg) + "</div>"
            });
        }

        private void BindAdjuntos()
        {
            var tareaId = TareaIdActual;
            var instanciaId = InstanciaIdActual;

            var list = new System.Collections.Generic.List<AdjRow>();

            if (instanciaId > 0)
            {
                var json = GetDatosContexto(instanciaId);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    JObject root = null;
                    try { root = JObject.Parse(json); } catch { root = null; }

                    var atts =
                        (root?["biz"]?["case"]?["attachments"] as JArray) ??
                        (root?["estado"]?["biz"]?["case"]?["attachments"] as JArray);

                    if (atts != null)
                    {
                        foreach (var it in atts)
                        {
                            var jo = it as JObject;
                            if (jo == null) continue;

                            var tareaIdDoc = Convert.ToString(jo["tareaId"] ?? "");
                            var fileName = Convert.ToString(jo["fileName"] ?? "");
                            var fecha = Convert.ToString(jo["fecha"] ?? "");
                            var usuario = Convert.ToString(jo["usuario"] ?? "");
                            var tipo = Convert.ToString(jo["tipo"] ?? "");

                            var url = Convert.ToString(jo["viewerUrl"] ?? "");

                            var storedFileName = Convert.ToString(jo["storedFileName"] ?? "");

                            if (!string.IsNullOrWhiteSpace(tareaIdDoc) && !string.IsNullOrWhiteSpace(storedFileName))
                            {
                                url = ResolveUrl("~/API/WF_Upload_Get.ashx")
                                    + "?inst=" + instanciaId
                                    + "&tarea=" + tareaIdDoc
                                    + "&authTarea=" + tareaId
                                    + "&f=" + HttpUtility.UrlEncode(storedFileName);
                            }

                            var subido = fecha;
                            if (!string.IsNullOrWhiteSpace(usuario))
                                subido = string.IsNullOrWhiteSpace(subido) ? usuario : (fecha + " - " + usuario);

                            list.Add(new AdjRow
                            {
                                Tipo = tipo,
                                FileName = fileName,
                                Fecha = subido,
                                Url = url,
                                StoredFileName = storedFileName,
                                TareaIdDoc = tareaIdDoc,
                                PuedeEliminar =
                                string.Equals(tareaIdDoc, tareaId.ToString(), StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(lblEstado.Text ?? "", "Pendiente", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                }
            }

            pnlAdjuntosEmpty.Visible = (list.Count == 0);
            rptAdjuntos.DataSource = list;
            rptAdjuntos.DataBind();
        }

        private string GetDatosContexto(long instanciaId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("SELECT DatosContexto FROM WF_Instancia WHERE Id=@Id", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cn.Open();
                var val = cmd.ExecuteScalar();
                return (val == null || val == DBNull.Value) ? "" : Convert.ToString(val);
            }
        }

        private void SetDatosContexto(long instanciaId, string json)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("UPDATE WF_Instancia SET DatosContexto=@J WHERE Id=@Id", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@J", SqlDbType.NVarChar).Value = (object)(json ?? "") ?? "";
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void AppendAttachmentToDatosContexto(long instanciaId, long tareaId, string fileName, string storedFileName, string tipo, string viewerUrl, string user)
        {
            var json = GetDatosContexto(instanciaId);
            JObject root;

            if (string.IsNullOrWhiteSpace(json))
                root = new JObject();
            else
            {
                try { root = JObject.Parse(json); }
                catch { root = new JObject(); }
            }

            var estado = root["estado"] as JObject;
            if (estado == null) { estado = new JObject(); root["estado"] = estado; }

            var biz = estado["biz"] as JObject;
            if (biz == null) { biz = new JObject(); estado["biz"] = biz; }

            var bcase = biz["case"] as JObject;
            if (bcase == null) { bcase = new JObject(); biz["case"] = bcase; }

            var atts = bcase["attachments"] as JArray;
            if (atts == null) { atts = new JArray(); bcase["attachments"] = atts; }

            var jo = new JObject
            {
                ["tareaId"] = tareaId.ToString(),
                ["fileName"] = fileName ?? "",
                ["storedFileName"] = storedFileName ?? "",
                ["tipo"] = tipo ?? "",
                ["viewerUrl"] = viewerUrl ?? "",
                ["fecha"] = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                ["usuario"] = user ?? ""
            };

            atts.Add(jo);

            SetDatosContexto(instanciaId, root.ToString(Formatting.None));
        }
    }
}