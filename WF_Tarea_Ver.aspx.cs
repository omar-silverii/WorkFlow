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

        private long _instanciaActualId
        {
            get { return (ViewState["__instActualId"] == null) ? 0 : (long)ViewState["__instActualId"]; }
            set { ViewState["__instActualId"] = value; }
        }

        private string _tareaActualId
        {
            get { return (string)(ViewState["__tareaActualId"] ?? ""); }
            set { ViewState["__tareaActualId"] = value; }
        }

        protected void chkDocAuditDedup_CheckedChanged(object sender, EventArgs e)
        {
            if (_instanciaActualId > 0)
                BindDocAuditForTask(_instanciaActualId.ToString(), _tareaActualId);
        }

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
                chkDocAuditDedup.Checked = true;
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

                    if (long.TryParse(lblInstanciaId.Text, out var instId))
                        _instanciaActualId = instId;

                    _tareaActualId = lblTareaId.Text;

                    BindDocAuditForTask(lblInstanciaId.Text, lblTareaId.Text);

                    // NUEVO: documentos asociados (instancia y tarea)
                    long instanciaId = 0;
                    long.TryParse(lblInstanciaId.Text, out instanciaId);
                    BindDocs(instanciaId, tareaId);

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

        private void BindDocAuditForTask(string instanciaIdText, string tareaIdText)
        {
            if (!long.TryParse(instanciaIdText, out var instId))
            {
                pnlDocAudit.Visible = false;
                pnlDocAuditEmpty.Visible = true;
                return;
            }

            var dt = new DataTable();

            var csItem = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (csItem == null) throw new InvalidOperationException("ConnectionString 'DefaultConnection' no encontrada.");
            var cnn = csItem.ConnectionString;

            bool dedup = (chkDocAuditDedup != null && chkDocAuditDedup.Checked);

            string sqlHistorico = @"
SELECT TOP 200
    FechaAlta,
    Accion,
    CASE
      WHEN EsRoot = 1 THEN 'Root'
      WHEN ISNULL(TareaId,'') <> '' AND TareaId = @Tarea THEN 'Tarea actual'
      WHEN ISNULL(TareaId,'') <> '' THEN 'Otra tarea'
      ELSE 'Instancia'
    END AS Scope,
    NodoTipo,
    Tipo,
    DocumentoId,
    ViewerUrl,
    IndicesJson
FROM dbo.WF_InstanciaDocumento
WHERE WF_InstanciaId = @Inst
ORDER BY FechaAlta DESC;";

            string sqlDedup = @"
;WITH X AS (
    SELECT
        *,
        ROW_NUMBER() OVER (
            PARTITION BY
                ISNULL(DocumentoId,''), EsRoot, ISNULL(TareaId,'')
            ORDER BY FechaAlta DESC, Id DESC
        ) AS rn
    FROM dbo.WF_InstanciaDocumento
    WHERE WF_InstanciaId = @Inst
)
SELECT
    FechaAlta,
    Accion,
    CASE
      WHEN EsRoot = 1 THEN 'Root'
      WHEN ISNULL(TareaId,'') <> '' AND TareaId = @Tarea THEN 'Tarea actual'
      WHEN ISNULL(TareaId,'') <> '' THEN 'Otra tarea'
      ELSE 'Instancia'
    END AS Scope,
    NodoTipo,
    Tipo,
    DocumentoId,
    ViewerUrl,
    IndicesJson
FROM X
WHERE rn = 1
ORDER BY FechaAlta DESC;";

            using (var cn = new SqlConnection(cnn))
            using (var cmd = new SqlCommand(dedup ? sqlDedup : sqlHistorico, cn))
            {
                cmd.Parameters.Add("@Inst", SqlDbType.BigInt).Value = instId;
                cmd.Parameters.Add("@Tarea", SqlDbType.NVarChar, 100).Value = (object)(tareaIdText ?? "") ?? "";

                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            if (dt.Rows.Count == 0)
            {
                pnlDocAudit.Visible = false;
                pnlDocAuditEmpty.Visible = true;
                gvDocAudit.DataSource = null;
                gvDocAudit.DataBind();
                return;
            }

            pnlDocAudit.Visible = true;
            pnlDocAuditEmpty.Visible = false;

            gvDocAudit.DataSource = dt;
            gvDocAudit.DataBind();
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


        // ===== Documentos (Caso) =====
        private class DocUiRow
        {
            public string DocumentoId { get; set; }
            public string CarpetaId { get; set; }
            public string FicheroId { get; set; }
            public string Tipo { get; set; }
            public string ViewerUrl { get; set; }
            public string Scope { get; set; }
        }

        private void BindDocs(long instanciaId, long tareaId)
        {
            try
            {
                var rows = new System.Collections.Generic.List<DocUiRow>();

                if (instanciaId <= 0)
                {
                    ShowDocs(rows);
                    return;
                }

                string json = "";
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand("SELECT DatosContexto FROM WF_Instancia WHERE Id=@Id;", cn))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instanciaId;
                    cn.Open();
                    var val = cmd.ExecuteScalar();
                    json = val == DBNull.Value || val == null ? "" : Convert.ToString(val);
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    ShowDocs(rows);
                    return;
                }

                Newtonsoft.Json.Linq.JObject root = null;
                try { root = Newtonsoft.Json.Linq.JObject.Parse(json); } catch { root = null; }
                if (root == null)
                {
                    ShowDocs(rows);
                    return;
                }

                var biz = root["biz"] as Newtonsoft.Json.Linq.JObject;
                var bcase = biz?["case"] as Newtonsoft.Json.Linq.JObject;

                // RootDoc (instancia)
                var rootDoc = bcase?["rootDoc"] as Newtonsoft.Json.Linq.JObject;
                if (rootDoc != null)
                    rows.Add(ToRow(rootDoc, "Instancia"));

                // Attachments
                var atts = bcase?["attachments"] as Newtonsoft.Json.Linq.JArray;
                if (atts != null)
                {
                    foreach (var it in atts)
                    {
                        var jo = it as Newtonsoft.Json.Linq.JObject;
                        if (jo == null) continue;

                        var tareaIdDoc = Convert.ToString(jo["tareaId"] ?? "");
                        var isForThisTask = (!string.IsNullOrWhiteSpace(tareaIdDoc) && tareaIdDoc == Convert.ToString(tareaId));

                        rows.Add(ToRow(jo, isForThisTask ? "Tarea actual" : "Instancia"));
                    }
                }

                ShowDocs(rows);
            }
            catch
            {
                ShowDocs(new System.Collections.Generic.List<DocUiRow>());
            }
        }

        private void ShowDocs(System.Collections.Generic.List<DocUiRow> rows)
        {
            if (rows != null && rows.Count > 0)
            {
                pnlDocs.Visible = true;
                pnlDocsEmpty.Visible = false;

                rptDocs.DataSource = rows;
                rptDocs.DataBind();
            }
            else
            {
                pnlDocs.Visible = false;
                pnlDocsEmpty.Visible = true;

                rptDocs.DataSource = null;
                rptDocs.DataBind();
            }
        }

        private static DocUiRow ToRow(Newtonsoft.Json.Linq.JObject doc, string scope)
        {
            return new DocUiRow
            {
                DocumentoId = Convert.ToString(doc["documentoId"] ?? ""),
                CarpetaId = Convert.ToString(doc["carpetaId"] ?? ""),
                FicheroId = Convert.ToString(doc["ficheroId"] ?? ""),
                Tipo = Convert.ToString(doc["tipo"] ?? ""),
                ViewerUrl = Convert.ToString(doc["viewerUrl"] ?? ""),
                Scope = scope
            };
        }
    }
}
