using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Notificaciones : BasePage
    {
        // Por ahora cualquier usuario autenticado puede ver sus notificaciones.
        protected override string[] RequiredPermissions => null;

        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Tareas"; } catch { }

            if (!IsPostBack)
                CargarGrid();
        }

        private void CargarGrid()
        {
            pnlAviso.Visible = false;
            litAviso.Text = string.Empty;

            if (!ExisteTablaNotificacion())
            {
                gvNotificaciones.DataSource = new DataTable();
                gvNotificaciones.DataBind();
                pnlAviso.Visible = true;
                litAviso.Text = "No existe la tabla dbo.WF_Notificacion. Ejecutá el script de creación de notificaciones sobre la base DefaultConnection.";
                return;
            }

            if (!ExisteTablaNotificacionLectura())
            {
                gvNotificaciones.DataSource = new DataTable();
                gvNotificaciones.DataBind();
                pnlAviso.Visible = true;
                litAviso.Text = "No existe la tabla dbo.WF_NotificacionLectura. Ejecutá Sql/FIX19_WF_NotificacionLectura.sql sobre la base DefaultConnection.";
                return;
            }

            string userKey = (Context.User.Identity.Name ?? "").Trim();
            string filtro = (txtFiltro.Text ?? "").Trim();
            bool soloNoLeidas = chkSoloNoLeidas.Checked;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 500
    N.Id,
    N.FechaCreacion,
    N.WF_InstanciaId,
    N.WF_DefinicionId,
    N.NodoId,
    N.NodoTipo,
    N.Tipo,
    N.Canal,
    N.Prioridad,
    N.Titulo,
    N.Mensaje,
    N.UsuarioDestino,
    N.RolDestino,
    N.Destino,
    N.UrlAccion,
    CAST(CASE WHEN L.Id IS NULL THEN 0 ELSE 1 END AS BIT) AS Leido,
    L.FechaLeido,
    L.Usuario AS LeidoPor
FROM dbo.WF_Notificacion N
LEFT JOIN dbo.WF_NotificacionLectura L
    ON L.NotificacionId = N.Id
   AND L.Usuario = @UserKey
WHERE
    (@SoloNoLeidas = 0 OR L.Id IS NULL)
    AND (
        (ISNULL(N.UsuarioDestino, '') = '' AND ISNULL(N.RolDestino, '') = '')
        OR N.UsuarioDestino = @UserKey
        OR EXISTS
        (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = N.RolDestino
        )
    )
    AND
    (
        @Filtro = ''
        OR N.Titulo LIKE @Like
        OR N.Mensaje LIKE @Like
        OR N.UsuarioDestino LIKE @Like
        OR N.RolDestino LIKE @Like
        OR CONVERT(NVARCHAR(30), N.WF_InstanciaId) LIKE @Like
    )
ORDER BY
    CASE WHEN L.Id IS NULL THEN 0 ELSE 1 END ASC,
    N.FechaCreacion DESC,
    N.Id DESC;";

                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                cmd.Parameters.Add("@SoloNoLeidas", SqlDbType.Bit).Value = soloNoLeidas;
                cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value = filtro;
                cmd.Parameters.Add("@Like", SqlDbType.NVarChar, 240).Value = "%" + filtro + "%";

                cn.Open();

                using (var da = new SqlDataAdapter(cmd))
                {
                    var dt = new DataTable();
                    da.Fill(dt);
                    gvNotificaciones.DataSource = dt;
                    gvNotificaciones.DataBind();
                }
            }
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvNotificaciones.PageIndex = 0;
            CargarGrid();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtFiltro.Text = string.Empty;
            chkSoloNoLeidas.Checked = true;
            gvNotificaciones.PageIndex = 0;
            CargarGrid();
        }

        protected void btnMarcarTodas_Click(object sender, EventArgs e)
        {
            if (!ExisteTablaNotificacion() || !ExisteTablaNotificacionLectura())
            {
                CargarGrid();
                return;
            }

            string userKey = (Context.User.Identity.Name ?? "").Trim();
            string filtro = (txtFiltro.Text ?? "").Trim();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cn.Open();

                cmd.CommandText = @"
INSERT INTO dbo.WF_NotificacionLectura (NotificacionId, Usuario, FechaLeido)
SELECT
    N.Id,
    @UserKey,
    GETDATE()
FROM dbo.WF_Notificacion N
WHERE
    (
        (ISNULL(N.UsuarioDestino, '') = '' AND ISNULL(N.RolDestino, '') = '')
        OR N.UsuarioDestino = @UserKey
        OR EXISTS
        (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = N.RolDestino
        )
    )
    AND
    (
        @Filtro = ''
        OR N.Titulo LIKE @Like
        OR N.Mensaje LIKE @Like
        OR N.UsuarioDestino LIKE @Like
        OR N.RolDestino LIKE @Like
        OR CONVERT(NVARCHAR(30), N.WF_InstanciaId) LIKE @Like
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.WF_NotificacionLectura L
        WHERE L.NotificacionId = N.Id
          AND L.Usuario = @UserKey
    );";

                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value = filtro;
                cmd.Parameters.Add("@Like", SqlDbType.NVarChar, 240).Value = "%" + filtro + "%";
                cmd.ExecuteNonQuery();
            }

            CargarGrid();
        }

        protected void gvNotificaciones_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvNotificaciones.PageIndex = e.NewPageIndex;
            CargarGrid();
        }

        protected void gvNotificaciones_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (e.CommandName == "MarcarLeida")
            {
                long id;
                if (long.TryParse(Convert.ToString(e.CommandArgument), out id))
                    MarcarLeida(id);

                CargarGrid();
            }
        }

        protected void gvNotificaciones_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;

            var drv = e.Row.DataItem as DataRowView;
            if (drv == null) return;

            bool leido = drv["Leido"] != DBNull.Value && Convert.ToBoolean(drv["Leido"]);
            if (!leido)
                e.Row.CssClass = (e.Row.CssClass + " ws-unread").Trim();
        }

        private void MarcarLeida(long id)
        {
            if (!ExisteTablaNotificacion() || !ExisteTablaNotificacionLectura()) return;

            string userKey = (Context.User.Identity.Name ?? "").Trim();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cn.Open();

                cmd.CommandText = @"
INSERT INTO dbo.WF_NotificacionLectura (NotificacionId, Usuario, FechaLeido)
SELECT
    N.Id,
    @UserKey,
    GETDATE()
FROM dbo.WF_Notificacion N
WHERE
    N.Id = @Id
    AND (
        (ISNULL(N.UsuarioDestino, '') = '' AND ISNULL(N.RolDestino, '') = '')
        OR N.UsuarioDestino = @UserKey
        OR EXISTS
        (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = N.RolDestino
        )
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.WF_NotificacionLectura L
        WHERE L.NotificacionId = N.Id
          AND L.Usuario = @UserKey
    );";

                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                cmd.ExecuteNonQuery();
            }
        }

        protected string GetEstadoHtml(object leidoObj)
        {
            bool leido = leidoObj != null && leidoObj != DBNull.Value && Convert.ToBoolean(leidoObj);
            return leido
                ? "<span class='badge bg-secondary'>Leída</span>"
                : "<span class='badge bg-primary'>Nueva</span>";
        }

        protected string GetPrioridadHtml(object prioridadObj)
        {
            string p = (prioridadObj == null || prioridadObj == DBNull.Value) ? "normal" : Convert.ToString(prioridadObj).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(p)) p = "normal";

            string cls = "ws-prio-normal";
            if (p == "alta") cls = "ws-prio-alta";
            else if (p == "critica" || p == "crítica") cls = "ws-prio-critica";
            else if (p == "baja") cls = "ws-prio-baja";

            return "<span class='ws-prio " + cls + "'>" + HttpUtility.HtmlEncode(p.ToUpperInvariant()) + "</span>";
        }

        protected bool TieneUrlAccion(object urlObj, object defObj, object instObj)
        {
            return !string.IsNullOrWhiteSpace(GetUrlAccion(urlObj, defObj, instObj));
        }

        protected string GetUrlAccion(object urlObj, object defObj, object instObj)
        {
            string url = (urlObj == null || urlObj == DBNull.Value) ? "" : Convert.ToString(urlObj).Trim();
            if (!string.IsNullOrWhiteSpace(url)) return url;

            long instId = 0;
            int defId = 0;
            bool hasInst = instObj != null && instObj != DBNull.Value && long.TryParse(Convert.ToString(instObj), out instId) && instId > 0;
            bool hasDef = defObj != null && defObj != DBNull.Value && int.TryParse(Convert.ToString(defObj), out defId) && defId > 0;

            if (hasInst && hasDef)
                return "WF_Instancias.aspx?defId=" + defId + "&inst=" + instId;

            if (hasInst)
                return "WF_Instancias.aspx?inst=" + instId;

            return string.Empty;
        }

        private bool ExisteTablaNotificacion()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.WF_Notificacion', 'U');", cn))
            {
                cn.Open();
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

        private bool ExisteTablaNotificacionLectura()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.WF_NotificacionLectura', 'U');", cn))
            {
                cn.Open();
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

    }
}
