using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Entidades : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "ENTIDADES_ABM" };

        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        private long? SelectedEntidadId
        {
            get
            {
                object o = ViewState["SelectedEntidadId"];
                if (o == null) return null;
                long v;
                return long.TryParse(Convert.ToString(o), out v) ? (long?)v : null;
            }
            set { ViewState["SelectedEntidadId"] = value; }
        }

        private string FiltroEstado
        {
            get { return Convert.ToString(ViewState["FiltroEstado"] ?? ""); }
            set { ViewState["FiltroEstado"] = value ?? ""; }
        }

        protected void chkModoTecnico_CheckedChanged(object sender, EventArgs e)
        {
            pnlTecnico.Visible = chkModoTecnico.Checked;

            if (SelectedEntidadId.HasValue)
                BindDetalle(SelectedEntidadId.Value);
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                CargarTipos();
                CargarIndiceKeys();

                // ✅ Default SaaS: solo activas
                chkSoloActivas.Checked = true;
                FiltroEstado = "Iniciado";

                // Default de modo técnico: OFF (vista usuario)
                chkModoTecnico.Checked = false;
                pnlTecnico.Visible = false;

                BindGrid(resetPageIndex: true);
            }
        }

        private void CargarIndiceKeys()
        {
            ddlIdxKey.Items.Clear();
            ddlIdxKey.Items.Add(new System.Web.UI.WebControls.ListItem("— (sin índice) —", ""));

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT DISTINCT [Key]
FROM dbo.WF_EntidadIndice
ORDER BY [Key];", cn))
            {
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var k = Convert.ToString(rd["Key"]);
                        if (!string.IsNullOrWhiteSpace(k))
                            ddlIdxKey.Items.Add(new System.Web.UI.WebControls.ListItem(k, k));
                    }
                }
            }
        }


        protected void ddlIdxKey_SelectedIndexChanged(object sender, EventArgs e)
        {
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void btnFiltrarIdx_Click(object sender, EventArgs e)
        {
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void chkSoloActivas_CheckedChanged(object sender, EventArgs e)
        {
            // Si está activado, forzamos estado Iniciado (activas)
            if (chkSoloActivas.Checked)
                FiltroEstado = "Iniciado";
            else
                FiltroEstado = "";

            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }


        protected void ddlTipo_SelectedIndexChanged(object sender, EventArgs e)
        {
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void lnkEstado_Click(object sender, EventArgs e)
        {
            var lb = sender as System.Web.UI.WebControls.LinkButton;
            FiltroEstado = lb?.CommandArgument ?? "";
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtBuscar.Text = "";
            ddlTipo.SelectedIndex = 0;
            FiltroEstado = "";
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
            BindGrid(resetPageIndex: true);
        }

        protected void gvEnt_PageIndexChanging(object sender, System.Web.UI.WebControls.GridViewPageEventArgs e)
        {
            gvEnt.PageIndex = e.NewPageIndex;
            BindGrid(resetPageIndex: false);
        }

        protected void gvEnt_RowCommand(object sender, System.Web.UI.WebControls.GridViewCommandEventArgs e)
        {
            if (e.CommandName == "Sel")
            {
                long id;
                if (long.TryParse(Convert.ToString(e.CommandArgument), out id))
                {
                    SelectedEntidadId = id;
                    BindDetalle(id);
                }
            }
        }

        protected void btnCerrarDetalle_Click(object sender, EventArgs e)
        {
            SelectedEntidadId = null;
            pnlDetalle.Visible = false;
        }

        private void CargarTipos()
        {
            ddlTipo.Items.Clear();
            ddlTipo.Items.Add(new System.Web.UI.WebControls.ListItem("Todos", ""));

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT DISTINCT TipoEntidad
FROM dbo.WF_Entidad
ORDER BY TipoEntidad;", cn))
            {
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var t = Convert.ToString(rd["TipoEntidad"]);
                        if (!string.IsNullOrWhiteSpace(t))
                            ddlTipo.Items.Add(new System.Web.UI.WebControls.ListItem(t, t));
                    }
                }
            }
        }

        private void BindGrid(bool resetPageIndex)
        {
            if (resetPageIndex) gvEnt.PageIndex = 0;

            string tipo = ddlTipo.SelectedValue;
            string estado = FiltroEstado;
            string q = (txtBuscar.Text ?? "").Trim();
            string idxKey = ddlIdxKey.SelectedValue;
            string idxVal = (txtIdxValue.Text ?? "").Trim();

            // ✅ KPIs (universo filtrado)
            BindKpis(tipo, estado, q, idxKey, idxVal);

            var dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand())
            {
                cmd.Connection = cn;

                var sb = new StringBuilder();
                sb.Append(@"
SELECT
    q.EntidadId,
    q.TipoEntidad,
    q.EstadoActual,
    q.Total,
    q.CreadoUtc,
    q.ActualizadoUtc,
    q.InstanciaId,
    q.TareaPendiente,
    q.UsuarioAsignado,
    q.FechaVencimiento
FROM (
    SELECT
        e.EntidadId,
        e.TipoEntidad,

        -- ✅ Estado efectivo: si hay tarea pendiente => Iniciado
        CASE
            WHEN t.Titulo IS NOT NULL THEN 'Iniciado'
            ELSE ISNULL(NULLIF(e.EstadoActual,''), '-')
        END AS EstadoActual,

        e.Total,
        e.CreadoUtc,
        e.ActualizadoUtc,
        e.InstanciaId,

        t.Titulo AS TareaPendiente,
        COALESCE(NULLIF(t.UsuarioAsignado,''), t.RolDestino) AS UsuarioAsignado,
        t.FechaVencimiento,

        -- ✅ flag para filtros
        CASE WHEN t.Titulo IS NOT NULL THEN 1 ELSE 0 END AS TieneTareaPendiente
    FROM dbo.WF_Entidad e
    OUTER APPLY (
        SELECT TOP 1
            Titulo,
            UsuarioAsignado,
            RolDestino,
            FechaVencimiento
        FROM dbo.WF_Tarea
        WHERE WF_InstanciaId = e.InstanciaId
          AND Estado = 'Pendiente'
        ORDER BY Id DESC
    ) t
) q
WHERE 1=1
");

                if (!string.IsNullOrWhiteSpace(tipo))
                {
                    sb.Append(" AND q.TipoEntidad = @Tipo ");
                    cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = tipo;
                }

                // ✅ Estado: filtra por el estado EFECTIVO (q.EstadoActual)
                if (!string.IsNullOrWhiteSpace(estado))
                {
                    sb.Append(" AND q.EstadoActual = @Estado ");
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 50).Value = estado;
                }

                // ✅ Solo activas: SOLO las que tienen tarea pendiente
                if (chkSoloActivas.Checked)
                {
                    sb.Append(" AND q.TieneTareaPendiente = 1 ");
                }

                if (!string.IsNullOrWhiteSpace(q))
                {
                    sb.Append(@"
 AND (
    EXISTS (
        SELECT 1
        FROM dbo.WF_EntidadIndice i
        WHERE i.EntidadId = q.EntidadId
          AND (i.ValueNorm LIKE @Q OR i.Value LIKE @Q)
    )
    OR q.EntidadId IN (
        SELECT e2.EntidadId
        FROM dbo.WF_Entidad e2
        WHERE e2.EntidadId = q.EntidadId
          AND e2.DataJson LIKE @QJson
    )
 )
");
                    cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 420).Value = "%" + q.ToLowerInvariant() + "%";
                    cmd.Parameters.Add("@QJson", SqlDbType.NVarChar).Value = "%" + q + "%";
                }

                if (!string.IsNullOrWhiteSpace(idxKey) && !string.IsNullOrWhiteSpace(idxVal))
                {
                    sb.Append(@"
 AND EXISTS (
    SELECT 1
    FROM dbo.WF_EntidadIndice i
    WHERE i.EntidadId = q.EntidadId
      AND i.[Key] = @IdxKey
      AND (i.ValueNorm LIKE @IdxValNorm OR i.[Value] LIKE @IdxValRaw)
 )
");
                    cmd.Parameters.Add("@IdxKey", SqlDbType.NVarChar, 100).Value = idxKey;
                    cmd.Parameters.Add("@IdxValNorm", SqlDbType.NVarChar, 420).Value = "%" + idxVal.ToLowerInvariant() + "%";
                    cmd.Parameters.Add("@IdxValRaw", SqlDbType.NVarChar, 420).Value = "%" + idxVal + "%";
                }

                sb.Append(" ORDER BY q.EntidadId DESC;");

                cmd.CommandText = sb.ToString();

                using (var da = new SqlDataAdapter(cmd))
                    da.Fill(dt);
            }

            gvEnt.DataSource = dt;
            gvEnt.DataBind();
            lblKpiMostrando.Text = dt.Rows.Count.ToString();
        }

        private void BindKpis(string tipo, string estado, string q, string idxKey, string idxVal)
        {
            var dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand())
            {
                cmd.Connection = cn;

                var sb = new StringBuilder();
                sb.Append(@"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN q.EstadoActual = 'Iniciado' THEN 1 ELSE 0 END) AS Iniciado,
    SUM(CASE WHEN q.EstadoActual = 'Finalizado' THEN 1 ELSE 0 END) AS Finalizado,
    SUM(CASE WHEN q.EstadoActual = 'Error' THEN 1 ELSE 0 END) AS Error
FROM (
    SELECT
        e.EntidadId,
        e.TipoEntidad,
        CASE
            WHEN t.Titulo IS NOT NULL THEN 'Iniciado'
            ELSE ISNULL(NULLIF(e.EstadoActual,''), '-')
        END AS EstadoActual,
        CASE WHEN t.Titulo IS NOT NULL THEN 1 ELSE 0 END AS TieneTareaPendiente
    FROM dbo.WF_Entidad e
    OUTER APPLY (
        SELECT TOP 1 Titulo
        FROM dbo.WF_Tarea
        WHERE WF_InstanciaId = e.InstanciaId
          AND Estado = 'Pendiente'
        ORDER BY Id DESC
    ) t
) q
WHERE 1=1
");

                if (!string.IsNullOrWhiteSpace(tipo))
                {
                    sb.Append(" AND q.TipoEntidad = @Tipo ");
                    cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = tipo;
                }

                if (!string.IsNullOrWhiteSpace(estado))
                {
                    sb.Append(" AND q.EstadoActual = @Estado ");
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 50).Value = estado;
                }

                if (chkSoloActivas.Checked)
                {
                    sb.Append(" AND q.TieneTareaPendiente = 1 ");
                }

                if (!string.IsNullOrWhiteSpace(q))
                {
                    sb.Append(@"
 AND (
    EXISTS (
        SELECT 1
        FROM dbo.WF_EntidadIndice i
        WHERE i.EntidadId = q.EntidadId
          AND (i.ValueNorm LIKE @Q OR i.Value LIKE @Q)
    )
    OR EXISTS (
        SELECT 1
        FROM dbo.WF_Entidad e2
        WHERE e2.EntidadId = q.EntidadId
          AND e2.DataJson LIKE @QJson
    )
 )
");
                    cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 420).Value = "%" + q.ToLowerInvariant() + "%";
                    cmd.Parameters.Add("@QJson", SqlDbType.NVarChar).Value = "%" + q + "%";
                }

                if (!string.IsNullOrWhiteSpace(idxKey) && !string.IsNullOrWhiteSpace(idxVal))
                {
                    sb.Append(@"
 AND EXISTS (
    SELECT 1
    FROM dbo.WF_EntidadIndice i
    WHERE i.EntidadId = q.EntidadId
      AND i.[Key] = @IdxKey
      AND (i.ValueNorm LIKE @IdxValNorm OR i.[Value] LIKE @IdxValRaw)
 )
");
                    cmd.Parameters.Add("@IdxKey", SqlDbType.NVarChar, 100).Value = idxKey;
                    cmd.Parameters.Add("@IdxValNorm", SqlDbType.NVarChar, 420).Value = "%" + idxVal.ToLowerInvariant() + "%";
                    cmd.Parameters.Add("@IdxValRaw", SqlDbType.NVarChar, 420).Value = "%" + idxVal + "%";
                }

                cmd.CommandText = sb.ToString();

                using (var da = new SqlDataAdapter(cmd))
                    da.Fill(dt);
            }

            if (dt.Rows.Count == 1)
            {
                var r = dt.Rows[0];
                lblKpiTotal.Text = Convert.ToString(r["Total"] ?? "0");
                lblKpiIniciado.Text = Convert.ToString(r["Iniciado"] ?? "0");
                lblKpiFinalizado.Text = Convert.ToString(r["Finalizado"] ?? "0");
                lblKpiError.Text = Convert.ToString(r["Error"] ?? "0");
            }
            else
            {
                lblKpiTotal.Text = "0";
                lblKpiIniciado.Text = "0";
                lblKpiFinalizado.Text = "0";
                lblKpiError.Text = "0";
            }
        }

        private void BindDetalle(long entidadId)
        {
            pnlDetalle.Visible = true;

            using (var cn = new SqlConnection(Cnn))
            {
                cn.Open();

                // 1) Header + json
                using (var cmd = new SqlCommand(@"
SELECT TOP 1
    EntidadId, TipoEntidad, EstadoActual, InstanciaId, Total, CreadoUtc, ActualizadoUtc, DataJson
FROM dbo.WF_Entidad
WHERE EntidadId=@Id;", cn))
                {
                    cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            string tipo = Convert.ToString(rd["TipoEntidad"]);
                            string est = Convert.ToString(rd["EstadoActual"]);

                            // InstanciaId: null -> "" (no usar "-")
                            string inst = rd["InstanciaId"] == DBNull.Value ? "" : Convert.ToString(rd["InstanciaId"]);

                            decimal total = 0m;
                            try { total = rd["Total"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["Total"]); } catch { }

                            string creado = Convert.ToDateTime(rd["CreadoUtc"]).ToString("dd/MM/yyyy HH:mm");
                            string act = Convert.ToDateTime(rd["ActualizadoUtc"]).ToString("dd/MM/yyyy HH:mm");

                            // Subtítulo (si no hay instancia, mostramos "-")
                            litDetalleSub.Text =
                                $"Id {entidadId} · {tipo} · Estado {est} · Instancia {(string.IsNullOrWhiteSpace(inst) ? "-" : inst)} · Creado {creado} · Actualizado {act}";

                            // Funcional (siempre)
                            litTipo.Text = Server.HtmlEncode(tipo);
                            litEstado.Text = Server.HtmlEncode(string.IsNullOrWhiteSpace(est) ? "-" : est);
                            litTotal.Text = total.ToString("N2");
                            litInst.Text = string.IsNullOrWhiteSpace(inst) ? "-" : inst;

                            // Links (solo si hay instancia real)
                            if (!string.IsNullOrWhiteSpace(inst))
                            {
                                lnkVerInst.Visible = true;
                                lnkVerLogs.Visible = true;

                                lnkVerInst.NavigateUrl = "~/WF_Instancias.aspx?inst=" + inst;
                                lnkVerLogs.NavigateUrl = "~/WF_Instancias.aspx?inst=" + inst + "#logs";
                            }
                            else
                            {
                                lnkVerInst.Visible = false;
                                lnkVerLogs.Visible = false;
                                lnkVerInst.NavigateUrl = "";
                                lnkVerLogs.NavigateUrl = "";
                            }

                            // Técnico ON/OFF
                            pnlTecnico.Visible = chkModoTecnico.Checked;

                            if (chkModoTecnico.Checked)
                            {
                                string json = rd["DataJson"] == DBNull.Value ? "" : Convert.ToString(rd["DataJson"]);
                                litJson.Text = Server.HtmlEncode(PrettyJson(json));
                                hfJsonRaw.Value = json ?? "";
                            }
                            else
                            {
                                // vista usuario: no cargamos json
                                litJson.Text = "";
                                hfJsonRaw.Value = "";
                            }
                        }
                        else
                        {
                            litDetalleSub.Text = "No encontrada.";
                            litJson.Text = "";

                            litTipo.Text = "";
                            litEstado.Text = "";
                            litTotal.Text = "";
                            litInst.Text = "-";

                            lnkVerInst.Visible = false;
                            lnkVerLogs.Visible = false;

                            pnlTecnico.Visible = false;
                        }
                    }
                }

                // 2) Índices + 3) Items (solo modo técnico)
                if (chkModoTecnico.Checked)
                {
                    // 2) Índices
                    var dtIdx = new DataTable();
                    using (var cmd = new SqlCommand(@"
SELECT TOP 50 [Key], [Value], SourcePath
FROM dbo.WF_EntidadIndice
WHERE EntidadId=@Id
ORDER BY [Key];", cn))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                        using (var da = new SqlDataAdapter(cmd))
                            da.Fill(dtIdx);
                    }
                    gvIdx.DataSource = dtIdx;
                    gvIdx.DataBind();

                    // 3) Items
                    var dtIt = new DataTable();
                    using (var cmd = new SqlCommand(@"
SELECT TOP 200 ItemIndex, Descripcion, Cantidad, Importe
FROM dbo.WF_EntidadItem
WHERE EntidadId=@Id
ORDER BY ItemIndex;", cn))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                        using (var da = new SqlDataAdapter(cmd))
                            da.Fill(dtIt);
                    }
                    gvItems.DataSource = dtIt;
                    gvItems.DataBind();
                }
                else
                {
                    gvIdx.DataSource = null;
                    gvIdx.DataBind();

                    gvItems.DataSource = null;
                    gvItems.DataBind();
                }
            }
        }

        private static string PrettyJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                // evita dependencia extra: usa Json.NET ya presente en el proyecto
                var obj = Newtonsoft.Json.Linq.JToken.Parse(json);
                return obj.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }
    }
}