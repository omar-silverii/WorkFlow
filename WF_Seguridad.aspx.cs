using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Seguridad : BasePage
    {
        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected override string[] RequiredPermissions => new[] { "ADMIN", "SEGURIDAD_ABM" };

        protected void Page_Load(object sender, EventArgs e)
        {
            // Topbar siempre
            try { Topbar1.ActiveSection = "Administración"; } catch { }
           
            if (!IsPostBack)
            {
                BindAll();
            }
        }

        private void BindAll()
        {
            lblMsg.Visible = false;
            BindUsers();
            BindRoles();
            BindPermisos();

            BindDropdownsAsignaciones();

            // precargar todo sincronizado
            SyncAsignacionesDesdeUsuario(ddlUserRoles.SelectedValue);
        }

        private void SyncAsignacionesDesdeUsuario(string userKey, string preferredRolKey = null)
        {
            userKey = (userKey ?? "").Trim();
            if (userKey.Length == 0) return;

            // Sincronizar ambos combos de usuario
            TrySelectDropDown(ddlUserRoles, userKey);
            TrySelectDropDown(ddlUserPerms, userKey);

            // Recargar checks de usuario
            LoadUserRoles();
            LoadUserPermsOverride();

            // Resolver rol a mostrar en "Rol -> Permisos"
            string rolKey = (preferredRolKey ?? "").Trim();

            if (rolKey.Length == 0)
            {
                var dtRol = Q(@"
SELECT TOP 1 ur.RolKey
FROM dbo.WF_UsuarioRol ur
INNER JOIN dbo.WF_Rol r ON r.RolKey = ur.RolKey AND r.Activo = 1
WHERE ur.Usuario = @U
  AND ur.Activo = 1
ORDER BY r.Nombre, ur.RolKey;",
                    p("@U", SqlDbType.NVarChar, 200, userKey));

                if (dtRol.Rows.Count > 0)
                    rolKey = Convert.ToString(dtRol.Rows[0]["RolKey"]);
            }

            if (rolKey.Length > 0 && TrySelectDropDown(ddlRolPerms, rolKey))
            {
                LoadRolPerms();
            }
            else
            {
                ddlRolPerms.ClearSelection();
                ClearChecks(cblPermsPorRol);
            }
        }
        private void SyncRolPermsDesdeRolesSeleccionadosDelUsuario()
        {
            string rolKey = cblRoles.Items.Cast<System.Web.UI.WebControls.ListItem>()
                .Where(x => x.Selected)
                .Select(x => x.Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(rolKey))
            {
                var u = ddlUserRoles.SelectedValue ?? "";
                if (u.Length > 0)
                {
                    var dtRol = Q(@"
SELECT TOP 1 ur.RolKey
FROM dbo.WF_UsuarioRol ur
INNER JOIN dbo.WF_Rol r ON r.RolKey = ur.RolKey AND r.Activo = 1
WHERE ur.Usuario = @U
  AND ur.Activo = 1
ORDER BY r.Nombre, ur.RolKey;",
                        p("@U", SqlDbType.NVarChar, 200, u));

                    if (dtRol.Rows.Count > 0)
                        rolKey = Convert.ToString(dtRol.Rows[0]["RolKey"]);
                }
            }

            if (!string.IsNullOrWhiteSpace(rolKey))
            {
                ddlRolPerms.ClearSelection();
                var it = ddlRolPerms.Items.FindByValue(rolKey);
                if (it != null)
                {
                    it.Selected = true;
                    LoadRolPerms();
                    return;
                }
            }

            ddlRolPerms.ClearSelection();
            ClearChecks(cblPermsPorRol);
        }

        private bool TrySelectDropDown(System.Web.UI.WebControls.DropDownList ddl, string value)
        {
            if (ddl == null) return false;
            ddl.ClearSelection();

            var it = ddl.Items.FindByValue(value ?? "");
            if (it == null) return false;

            it.Selected = true;
            return true;
        }

        private void ShowMsg(string text, string css = "alert alert-success")
        {
            lblMsg.Visible = true;
            lblMsg.CssClass = css;
            lblMsg.Text = Server.HtmlEncode(text);
        }

        // ===== USERS =====
        private void BindUsers()
        {
            gvUsers.DataSource = Q(@"
SELECT UserKey, DisplayName, Activo, FechaAlta
FROM dbo.WF_User
ORDER BY DisplayName, UserKey;");
            gvUsers.DataBind();
        }

        protected void btnUserAdd_Click(object sender, EventArgs e)
        {
            var uk = (txtUserKey.Text ?? "").Trim();
            var dn = (txtDisplayName.Text ?? "").Trim();

            if (uk.Length == 0) { ShowMsg("UserKey requerido.", "alert alert-danger"); return; }

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.WF_User WHERE UserKey=@U)
BEGIN
    UPDATE dbo.WF_User SET DisplayName=@D WHERE UserKey=@U;
END
ELSE
BEGIN
    INSERT INTO dbo.WF_User(UserKey, DisplayName, Activo, FechaAlta)
    VALUES(@U, @D, 1, getdate());

    IF EXISTS (SELECT 1 FROM dbo.WF_Permiso WHERE PermisoKey='DASH')
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.WF_UserPermiso WHERE UserKey=@U AND PermisoKey='DASH')
            UPDATE dbo.WF_UserPermiso SET Activo=1 WHERE UserKey=@U AND PermisoKey='DASH';
        ELSE
            INSERT INTO dbo.WF_UserPermiso(UserKey, PermisoKey, VerTodo, Activo)
            VALUES(@U, 'DASH', 0, 1);
    END
END";
                cmd.Parameters.Add("@U", SqlDbType.NVarChar, 200).Value = uk;
                cmd.Parameters.Add("@D", SqlDbType.NVarChar, 200).Value = (object)dn ?? DBNull.Value;

                cn.Open();
                cmd.ExecuteNonQuery();
            }

            BindUsers();
            BindDropdownsAsignaciones();
            ShowMsg("Usuario guardado.");
        }

        protected void btnUserClear_Click(object sender, EventArgs e)
        {
            txtUserKey.Text = "";
            txtDisplayName.Text = "";
        }

        protected void gvUsers_RowCommand(object sender, System.Web.UI.WebControls.GridViewCommandEventArgs e)
        {
            var uk = Convert.ToString(e.CommandArgument) ?? "";
            if (uk.Length == 0) return;

            if (string.Equals(e.CommandName, "EditUser", StringComparison.OrdinalIgnoreCase))
            {
                var dt = Q("SELECT UserKey, DisplayName FROM dbo.WF_User WHERE UserKey=@U;",
                    p("@U", SqlDbType.NVarChar, 200, uk));
                if (dt.Rows.Count == 1)
                {
                    txtUserKey.Text = Convert.ToString(dt.Rows[0]["UserKey"]);
                    txtDisplayName.Text = Convert.ToString(dt.Rows[0]["DisplayName"]);
                }
            }
            else if (string.Equals(e.CommandName, "ToggleUser", StringComparison.OrdinalIgnoreCase))
            {
                X(@"
UPDATE dbo.WF_User
SET Activo = CASE WHEN Activo=1 THEN 0 ELSE 1 END
WHERE UserKey=@U;", p("@U", SqlDbType.NVarChar, 200, uk));

                BindUsers();
                BindDropdownsAsignaciones();
                ShowMsg("Usuario actualizado.");
            }
        }

        // ===== ROLES =====
        private void BindRoles()
        {
            gvRoles.DataSource = Q(@"
SELECT RolKey, Nombre, Activo
FROM dbo.WF_Rol
ORDER BY Nombre, RolKey;");
            gvRoles.DataBind();
        }

        protected void btnRolAdd_Click(object sender, EventArgs e)
        {
            var rk = (txtRolKey.Text ?? "").Trim();
            var nm = (txtRolNombre.Text ?? "").Trim();

            if (rk.Length == 0 || nm.Length == 0)
            { ShowMsg("RolKey y Nombre requeridos.", "alert alert-danger"); return; }

            X(@"
IF EXISTS (SELECT 1 FROM dbo.WF_Rol WHERE RolKey=@R)
    UPDATE dbo.WF_Rol SET Nombre=@N WHERE RolKey=@R;
ELSE
    INSERT INTO dbo.WF_Rol(RolKey, Nombre, Activo) VALUES(@R,@N,1);",
                p("@R", SqlDbType.NVarChar, 50, rk),
                p("@N", SqlDbType.NVarChar, 200, nm));

            BindRoles();
            BindDropdownsAsignaciones();
            BindCheckLists();
            ShowMsg("Rol guardado.");
        }

        protected void btnRolClear_Click(object sender, EventArgs e)
        {
            txtRolKey.Text = "";
            txtRolNombre.Text = "";
        }

        protected void gvRoles_RowCommand(object sender, System.Web.UI.WebControls.GridViewCommandEventArgs e)
        {
            var rk = Convert.ToString(e.CommandArgument) ?? "";
            if (rk.Length == 0) return;

            if (string.Equals(e.CommandName, "EditRol", StringComparison.OrdinalIgnoreCase))
            {
                var dt = Q("SELECT RolKey, Nombre FROM dbo.WF_Rol WHERE RolKey=@R;",
                    p("@R", SqlDbType.NVarChar, 50, rk));
                if (dt.Rows.Count == 1)
                {
                    txtRolKey.Text = Convert.ToString(dt.Rows[0]["RolKey"]);
                    txtRolNombre.Text = Convert.ToString(dt.Rows[0]["Nombre"]);
                }
            }
            else if (string.Equals(e.CommandName, "ToggleRol", StringComparison.OrdinalIgnoreCase))
            {
                X(@"UPDATE dbo.WF_Rol SET Activo = CASE WHEN Activo=1 THEN 0 ELSE 1 END WHERE RolKey=@R;",
                    p("@R", SqlDbType.NVarChar, 50, rk));

                BindRoles();
                BindDropdownsAsignaciones();
                BindCheckLists();
                ShowMsg("Rol actualizado.");
            }
        }

        // ===== PERMISOS =====
        private void BindPermisos()
        {
            gvPermisos.DataSource = Q(@"
SELECT PermisoKey, Nombre, Descripcion, Activo
FROM dbo.WF_Permiso
ORDER BY Nombre, PermisoKey;");
            gvPermisos.DataBind();
        }

        protected void btnPermAdd_Click(object sender, EventArgs e)
        {
            var pk = (txtPermKey.Text ?? "").Trim();
            var nm = (txtPermNombre.Text ?? "").Trim();
            var ds = (txtPermDesc.Text ?? "").Trim();

            if (pk.Length == 0 || nm.Length == 0)
            { ShowMsg("PermisoKey y Nombre requeridos.", "alert alert-danger"); return; }

            X(@"
IF EXISTS (SELECT 1 FROM dbo.WF_Permiso WHERE PermisoKey=@P)
    UPDATE dbo.WF_Permiso SET Nombre=@N, Descripcion=@D WHERE PermisoKey=@P;
ELSE
    INSERT INTO dbo.WF_Permiso(PermisoKey, Nombre, Descripcion, Activo) VALUES(@P,@N,@D,1);",
                p("@P", SqlDbType.NVarChar, 80, pk),
                p("@N", SqlDbType.NVarChar, 200, nm),
                p("@D", SqlDbType.NVarChar, 400, (object)ds ?? DBNull.Value));

            BindPermisos();
            BindDropdownsAsignaciones();
            BindCheckLists();
            ShowMsg("Permiso guardado.");
        }

        protected void btnPermClear_Click(object sender, EventArgs e)
        {
            txtPermKey.Text = "";
            txtPermNombre.Text = "";
            txtPermDesc.Text = "";
        }

        protected void gvPermisos_RowCommand(object sender, System.Web.UI.WebControls.GridViewCommandEventArgs e)
        {
            var pk = Convert.ToString(e.CommandArgument) ?? "";
            if (pk.Length == 0) return;

            if (string.Equals(e.CommandName, "EditPerm", StringComparison.OrdinalIgnoreCase))
            {
                var dt = Q("SELECT PermisoKey, Nombre, Descripcion FROM dbo.WF_Permiso WHERE PermisoKey=@P;",
                    p("@P", SqlDbType.NVarChar, 80, pk));
                if (dt.Rows.Count == 1)
                {
                    txtPermKey.Text = Convert.ToString(dt.Rows[0]["PermisoKey"]);
                    txtPermNombre.Text = Convert.ToString(dt.Rows[0]["Nombre"]);
                    txtPermDesc.Text = Convert.ToString(dt.Rows[0]["Descripcion"]);
                }
            }
            else if (string.Equals(e.CommandName, "TogglePerm", StringComparison.OrdinalIgnoreCase))
            {
                X(@"UPDATE dbo.WF_Permiso SET Activo = CASE WHEN Activo=1 THEN 0 ELSE 1 END WHERE PermisoKey=@P;",
                    p("@P", SqlDbType.NVarChar, 80, pk));

                BindPermisos();
                BindDropdownsAsignaciones();
                BindCheckLists();
                ShowMsg("Permiso actualizado.");
            }
        }

        // ===== ASIGNACIONES =====
        private void BindDropdownsAsignaciones()
        {
            ddlUserRoles.DataSource = Q("SELECT UserKey, ISNULL(NULLIF(DisplayName,''),UserKey) Nom FROM dbo.WF_User WHERE Activo=1 ORDER BY Nom;");
            ddlUserRoles.DataTextField = "Nom";
            ddlUserRoles.DataValueField = "UserKey";
            ddlUserRoles.DataBind();

            ddlUserPerms.DataSource = Q("SELECT UserKey, ISNULL(NULLIF(DisplayName,''),UserKey) Nom FROM dbo.WF_User WHERE Activo=1 ORDER BY Nom;");
            ddlUserPerms.DataTextField = "Nom";
            ddlUserPerms.DataValueField = "UserKey";
            ddlUserPerms.DataBind();

            ddlRolPerms.DataSource = Q("SELECT RolKey, Nombre FROM dbo.WF_Rol WHERE Activo=1 ORDER BY Nombre;");
            ddlRolPerms.DataTextField = "Nombre";
            ddlRolPerms.DataValueField = "RolKey";
            ddlRolPerms.DataBind();

            BindCheckLists();
        }

        private void BindCheckLists()
        {
            cblRoles.DataSource = Q("SELECT RolKey, Nombre FROM dbo.WF_Rol WHERE Activo=1 ORDER BY Nombre;");
            cblRoles.DataTextField = "Nombre";
            cblRoles.DataValueField = "RolKey";
            cblRoles.DataBind();

            var dtPerm = Q("SELECT PermisoKey, Nombre FROM dbo.WF_Permiso WHERE Activo=1 ORDER BY Nombre;");
            cblPermsPorRol.DataSource = dtPerm;
            cblPermsPorRol.DataTextField = "Nombre";
            cblPermsPorRol.DataValueField = "PermisoKey";
            cblPermsPorRol.DataBind();

            cblPermsPorUser.DataSource = dtPerm;
            cblPermsPorUser.DataTextField = "Nombre";
            cblPermsPorUser.DataValueField = "PermisoKey";
            cblPermsPorUser.DataBind();
        }

        protected void ddlUserRoles_SelectedIndexChanged(object sender, EventArgs e)
        {
            SyncAsignacionesDesdeUsuario(ddlUserRoles.SelectedValue);
        }

        protected void ddlRolPerms_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadRolPerms();
        }

        protected void ddlUserPerms_SelectedIndexChanged(object sender, EventArgs e)
        {
            SyncAsignacionesDesdeUsuario(ddlUserPerms.SelectedValue);
        }

        protected void btnReloadUserRoles_Click(object sender, EventArgs e) => LoadUserRoles();
        protected void btnReloadRolPerms_Click(object sender, EventArgs e) => LoadRolPerms();
        protected void btnReloadUserPerms_Click(object sender, EventArgs e) => LoadUserPermsOverride();

        private void LoadUserRoles()
        {
            ClearChecks(cblRoles);

            var u = ddlUserRoles.SelectedValue ?? "";
            if (u.Length == 0)
            {
                ddlRolPerms.ClearSelection();
                ClearChecks(cblPermsPorRol);
                return;
            }

            var dt = Q("SELECT RolKey FROM dbo.WF_UsuarioRol WHERE Usuario=@U AND Activo=1;",
                p("@U", SqlDbType.NVarChar, 200, u));

            foreach (DataRow r in dt.Rows)
            {
                var rk = Convert.ToString(r["RolKey"]);
                var it = cblRoles.Items.FindByValue(rk);
                if (it != null) it.Selected = true;
            }

            // ✅ además de cargar roles del usuario, sincroniza y recarga permisos del rol visible
            SyncRolPermsDesdeRolesSeleccionadosDelUsuario();
        }

        protected void btnSaveUserRoles_Click(object sender, EventArgs e)
        {
            var u = ddlUserRoles.SelectedValue ?? "";
            if (u.Length == 0) return;

            // set completo: desactivo todos y activo seleccionados (simple y confiable)
            X("UPDATE dbo.WF_UsuarioRol SET Activo=0 WHERE Usuario=@U;", p("@U", SqlDbType.NVarChar, 200, u));

            foreach (System.Web.UI.WebControls.ListItem it in cblRoles.Items)
            {
                if (!it.Selected) continue;
                X(@"
IF EXISTS (SELECT 1 FROM dbo.WF_UsuarioRol WHERE Usuario=@U AND RolKey=@R)
    UPDATE dbo.WF_UsuarioRol SET Activo=1 WHERE Usuario=@U AND RolKey=@R;
ELSE
    INSERT INTO dbo.WF_UsuarioRol(Usuario,RolKey,Activo) VALUES(@U,@R,1);",
                    p("@U", SqlDbType.NVarChar, 200, u),
                    p("@R", SqlDbType.NVarChar, 50, it.Value));
            }

            ShowMsg("Roles del usuario guardados.");

            string rolPreferido = cblRoles.Items.Cast<System.Web.UI.WebControls.ListItem>()
                .Where(x => x.Selected)
                .Select(x => x.Value)
                .FirstOrDefault();

            SyncAsignacionesDesdeUsuario(u, rolPreferido);
        }

        private void LoadRolPerms()
        {
            ClearChecks(cblPermsPorRol);

            var rkey = ddlRolPerms.SelectedValue ?? "";
            if (rkey.Length == 0) return;

            var dt = Q("SELECT PermisoKey FROM dbo.WF_RolPermiso WHERE RolKey=@R AND Activo=1;",
                p("@R", SqlDbType.NVarChar, 50, rkey));

            foreach (DataRow r in dt.Rows)
            {
                var pk = Convert.ToString(r["PermisoKey"]);
                var it = cblPermsPorRol.Items.FindByValue(pk);
                if (it != null) it.Selected = true;
            }
        }

        protected void btnSaveRolPerms_Click(object sender, EventArgs e)
        {
            var rkey = ddlRolPerms.SelectedValue ?? "";
            if (rkey.Length == 0) return;

            X("UPDATE dbo.WF_RolPermiso SET Activo=0 WHERE RolKey=@R;", p("@R", SqlDbType.NVarChar, 50, rkey));

            foreach (System.Web.UI.WebControls.ListItem it in cblPermsPorRol.Items)
            {
                if (!it.Selected) continue;
                X(@"
IF EXISTS (SELECT 1 FROM dbo.WF_RolPermiso WHERE RolKey=@R AND PermisoKey=@P)
    UPDATE dbo.WF_RolPermiso SET Activo=1 WHERE RolKey=@R AND PermisoKey=@P;
ELSE
    INSERT INTO dbo.WF_RolPermiso(RolKey,PermisoKey,Activo) VALUES(@R,@P,1);",
                    p("@R", SqlDbType.NVarChar, 50, rkey),
                    p("@P", SqlDbType.NVarChar, 80, it.Value));
            }

            ShowMsg("Permisos del rol guardados.");
            LoadRolPerms();
        }

        private void LoadUserPermsOverride()
        {
            ClearChecks(cblPermsPorUser);

            var u = ddlUserPerms.SelectedValue ?? "";
            if (u.Length == 0) return;

            var dt = Q("SELECT PermisoKey FROM dbo.WF_UserPermiso WHERE UserKey=@U AND Activo=1;",
                p("@U", SqlDbType.NVarChar, 200, u));

            foreach (DataRow r in dt.Rows)
            {
                var pk = Convert.ToString(r["PermisoKey"]);
                var it = cblPermsPorUser.Items.FindByValue(pk);
                if (it != null) it.Selected = true;
            }
        }

        protected void btnSaveUserPerms_Click(object sender, EventArgs e)
        {
            var u = ddlUserPerms.SelectedValue ?? "";
            if (u.Length == 0) return;

            X("UPDATE dbo.WF_UserPermiso SET Activo=0 WHERE UserKey=@U;", p("@U", SqlDbType.NVarChar, 200, u));

            foreach (System.Web.UI.WebControls.ListItem it in cblPermsPorUser.Items)
            {
                if (!it.Selected) continue;
                X(@"
IF EXISTS (SELECT 1 FROM dbo.WF_UserPermiso WHERE UserKey=@U AND PermisoKey=@P)
    UPDATE dbo.WF_UserPermiso SET Activo=1 WHERE UserKey=@U AND PermisoKey=@P;
ELSE
    INSERT INTO dbo.WF_UserPermiso(UserKey,PermisoKey,VerTodo,Activo) VALUES(@U,@P,0,1);",
                    p("@U", SqlDbType.NVarChar, 200, u),
                    p("@P", SqlDbType.NVarChar, 80, it.Value));
            }

            ShowMsg("Permisos override del usuario guardados.");
            LoadUserPermsOverride();
        }

        private static void ClearChecks(System.Web.UI.WebControls.CheckBoxList cbl)
        {
            foreach (System.Web.UI.WebControls.ListItem it in cbl.Items) it.Selected = false;
        }

        // ===== SQL helpers =====
        private DataTable Q(string sql, params SqlParameter[] pars)
        {
            var dt = new DataTable();
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                if (pars != null && pars.Length > 0) cmd.Parameters.AddRange(pars);
                using (var da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            return dt;
        }

        private void X(string sql, params SqlParameter[] pars)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(sql, cn))
            {
                if (pars != null && pars.Length > 0) cmd.Parameters.AddRange(pars);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private SqlParameter p(string name, SqlDbType t, int size, object val)
        {
            var sp = new SqlParameter(name, t, size);
            sp.Value = val ?? DBNull.Value;
            return sp;
        }
    }
}