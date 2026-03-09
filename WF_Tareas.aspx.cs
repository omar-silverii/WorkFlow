using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.UI.WebControls;
using Newtonsoft.Json.Linq;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Tareas : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "TAREAS_MIS" };

        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            // Topbar activo siempre (postback o no)
            try
            {
                Topbar1.ActiveSection = "Tareas";
            }
            catch
            {
                // si por alguna razón aún no está el control en el aspx, no rompemos la página
            }


            if (!IsPostBack)
            {
                CargarGrid();
            }
        }

        private void CargarGrid()
        {
            var userKey = (User.Identity.Name ?? "").Trim();

            // Si es admin / verTodo => sin restricción adicional
            bool verTodo = EsAdminOVerTodo(userKey);

            string sql = @"
SELECT
    T.Id,
    T.WF_InstanciaId,
    i.WF_DefinicionId,
    T.NodoId,
    T.NodoTipo,
    T.Titulo,
    T.Descripcion,
    T.RolDestino,
    T.UsuarioAsignado,
    T.AsignadoA,
    T.ScopeKey,
    T.Estado,
    T.Resultado,
    T.Datos,
    T.FechaCreacion,
    T.FechaVencimiento
FROM dbo.WF_Tarea T
JOIN dbo.WF_Instancia i ON i.Id = T.WF_InstanciaId
WHERE 1 = 1
";

            bool soloPend = chkSoloPendientes.Checked;
            string filtro = (txtFiltro.Text ?? "").Trim();

            if (soloPend)
            {
                sql += " AND T.Estado = 'Pendiente'";
            }

            if (!string.IsNullOrEmpty(filtro))
            {
                sql += @"
 AND (
        T.Titulo          LIKE @Filtro
     OR T.Descripcion     LIKE @Filtro
     OR T.RolDestino      LIKE @Filtro
     OR T.UsuarioAsignado LIKE @Filtro
    )";
            }

            // ===== FILTRO POR USUARIO/ROL (si no es verTodo) =====
            if (!verTodo)
            {
                // - Ver tareas asignadas al usuario
                // - y/o tareas cuyo RolDestino esté permitido por WF_UserPermiso (Permiso='ROL')
                // - y/o tasks asignadas explícitas por WF_UserPermiso (Permiso='USER' con ScopeKey=userKey)
                sql += @"
 AND (
        -- 1) Asignadas explícitamente al usuario (usuario puntual)
        T.AsignadoA = @UserKey

        -- 2) O por rol destino (roles activos del usuario)
     OR EXISTS (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = T.RolDestino
        )

        -- 3) Compatibilidad legacy: si todavía usan UsuarioAsignado como asignación puntual
     OR T.UsuarioAsignado = @UserKey
    )
";
            }

            sql += " ORDER BY T.FechaCreacion DESC";

            var dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;

                if (!string.IsNullOrEmpty(filtro))
                    cmd.Parameters.AddWithValue("@Filtro", "%" + filtro + "%");

                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            gvTareas.DataSource = dt;
            gvTareas.DataBind();
        }

        private bool UsuarioActivo(string userKey)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.WF_User WHERE UserKey=@U AND Activo=1;";
                cmd.Parameters.Add("@U", SqlDbType.NVarChar, 200).Value = userKey;
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private bool TienePermisoBaseTareas(string userKey)
        {
            // Admin siempre puede
            if (EsAdminOVerTodo(userKey)) return true;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_UserPermiso P
WHERE P.Activo=1
  AND P.UserKey=@U
  AND (P.PermisoKey='WF_TAREAS' OR P.PermisoKey='WF_ADMIN' OR P.VerTodo=1);";
                cmd.Parameters.Add("@U", SqlDbType.NVarChar, 200).Value = userKey;
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private bool EsAdminOVerTodo(string userKey)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_UserPermiso P
WHERE P.Activo=1
  AND P.UserKey=@U
  AND (P.PermisoKey='WF_ADMIN' OR P.VerTodo=1 OR P.PermisoKey='WF_TAREAS_VER_TODO');";
                cmd.Parameters.Add("@U", SqlDbType.NVarChar, 200).Value = userKey;
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private void Denegado()
        {
            Response.StatusCode = 403;
            Response.Write("No autorizado.");
            Response.End();
        }

        protected string GetFlujoIconoHtml(object instanciaIdObj, object tareaIdObj)
        {
            long instanciaId;
            long tareaId;

            if (instanciaIdObj == null || !long.TryParse(Convert.ToString(instanciaIdObj), out instanciaId))
                return "";

            if (tareaIdObj == null || !long.TryParse(Convert.ToString(tareaIdObj), out tareaId))
                return "";

            if (!VieneDeRechazo(instanciaId, tareaId))
                return "";

            return "<span class='ws-flow-pill' title='Reproceso'>↺</span>";
        }

        private bool VieneDeRechazo(long instanciaId, long tareaIdActual)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 1
    t.Estado,
    t.Resultado
FROM dbo.WF_Tarea t
WHERE
    t.WF_InstanciaId = @InstanciaId
    AND t.Id < @TareaActual
ORDER BY
    t.Id DESC;";

                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@TareaActual", SqlDbType.BigInt).Value = tareaIdActual;

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return false;

                    string estado = dr["Estado"] == DBNull.Value ? "" : Convert.ToString(dr["Estado"]);
                    string resultado = dr["Resultado"] == DBNull.Value ? "" : Convert.ToString(dr["Resultado"]);

                    return string.Equals(estado, "Completada", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(resultado, "rechazado", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private string ExtraerFrameIdDesdeDatos(string datosJson)
        {
            if (string.IsNullOrWhiteSpace(datosJson)) return null;

            try
            {
                var o = JObject.Parse(datosJson);

                JToken tok =
                    o.SelectToken("meta.wfBack.frameId") ??
                    o.SelectToken("wfBack.frameId");

                return tok == null ? null : Convert.ToString(tok);
            }
            catch
            {
                return null;
            }
        }

        private bool ExisteRechazoPrevioMismoFrame(long instanciaId, long tareaIdActual, string frameId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_Tarea t
WHERE
    t.WF_InstanciaId = @InstanciaId
    AND t.Id <> @TareaActual
    AND t.Resultado = @ResRech
    AND t.Datos LIKE @LikeFrame;";

                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@TareaActual", SqlDbType.BigInt).Value = tareaIdActual;
                cmd.Parameters.Add("@ResRech", SqlDbType.VarChar, 50).Value = "rechazado";
                cmd.Parameters.Add("@LikeFrame", SqlDbType.NVarChar, 200).Value = "%" + frameId + "%";

                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private int ExtraerCycleDesdeDatos(string datosJson)
        {
            if (string.IsNullOrWhiteSpace(datosJson)) return 0;

            try
            {
                var o = JObject.Parse(datosJson);

                JToken tok =
                    o.SelectToken("meta.wfBack.cycle") ??
                    o.SelectToken("wfBack.cycle");

                if (tok == null) return 0;

                int cycle;
                return int.TryParse(Convert.ToString(tok), out cycle) ? cycle : 0;
            }
            catch
            {
                return 0;
            }
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvTareas.PageIndex = 0;
            CargarGrid();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtFiltro.Text = string.Empty;
            chkSoloPendientes.Checked = true;
            gvTareas.PageIndex = 0;
            CargarGrid();
        }

        protected void gvTareas_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvTareas.PageIndex = e.NewPageIndex;
            CargarGrid();
        }

        protected void gvTareas_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            // Si usás CommandName="Detalle" en algún lado
            if (e.CommandName == "Detalle")
            {
                long tareaId = Convert.ToInt64(e.CommandArgument);
                Response.Redirect("WF_Tarea_Detalle.aspx?id=" + tareaId + "&src=tareas");
                return;
            }

            // ===== Ir a la instancia (CORREGIDO) =====
            if (e.CommandName == "VerInstancia")
            {
                // CommandArgument: "WF_InstanciaId|WF_DefinicionId"
                var arg = Convert.ToString(e.CommandArgument) ?? string.Empty;

                long instId = 0;
                int defId = 0;

                var parts = arg.Split('|');
                if (parts.Length >= 2)
                {
                    long.TryParse(parts[0], out instId);
                    int.TryParse(parts[1], out defId);
                }

                if (defId > 0 && instId > 0)
                {
                    Response.Redirect("WF_Instancias.aspx?defId=" + defId + "&inst=" + instId);
                    return;
                }

                if (instId > 0)
                {
                    Response.Redirect("WF_Instancias.aspx?inst=" + instId);
                    return;
                }

                Response.Redirect("WF_Instancias.aspx");
                return;
            }

            // ...otros comandos...
        }
    }
}