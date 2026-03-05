using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public static class RbacService
    {
        private static string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public static bool HasAnyPermiso(string userKey, params string[] permisos)
        {
            if (permisos == null || permisos.Length == 0) return true;
            foreach (var p in permisos)
                if (HasPermiso(userKey, p)) return true;
            return false;
        }

        // Alias retrocompatible (para que no se rompa código viejo)
        public static bool UserHasAnyPermission(string userKey, params string[] permisos)
        {
            return HasAnyPermiso(userKey, permisos);
        }

        public static bool HasPermiso(string userKey, string permisoKey)
        {
            userKey = (userKey ?? "").Trim();
            permisoKey = (permisoKey ?? "").Trim();

            if (userKey.Length == 0 || permisoKey.Length == 0) return false;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
IF EXISTS (
    -- Override directo por usuario
    SELECT 1
    FROM dbo.WF_UserPermiso up
    JOIN dbo.WF_Permiso p ON p.PermisoKey = up.PermisoKey AND p.Activo = 1
    WHERE up.Activo = 1
      AND up.UserKey = @UserKey
      AND up.PermisoKey = @PermisoKey
)
    SELECT 1
ELSE IF EXISTS (
    -- Por roles
    SELECT 1
    FROM dbo.WF_UsuarioRol ur
    JOIN dbo.WF_RolPermiso rp ON rp.RolKey = ur.RolKey AND rp.Activo = 1
    JOIN dbo.WF_Permiso p ON p.PermisoKey = rp.PermisoKey AND p.Activo = 1
    WHERE ur.Activo = 1
      AND ur.Usuario = @UserKey
      AND rp.PermisoKey = @PermisoKey
)
    SELECT 1
ELSE
    SELECT 0;";
                cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                cmd.Parameters.Add("@PermisoKey", SqlDbType.NVarChar, 100).Value = permisoKey;

                cn.Open();
                var o = cmd.ExecuteScalar();
                return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
            }
        }
    }
}