using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.Script.Serialization;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WF_NotificationCounters : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";

            try
            {
                if (ctx?.User?.Identity == null || !ctx.User.Identity.IsAuthenticated)
                {
                    WriteJson(ctx, new { ok = false, error = "No autenticado", unread = 0, total = 0 });
                    return;
                }

                string userKey = (ctx.User.Identity.Name ?? "").Trim();
                string cnn = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                int unread = 0;
                int total = 0;

                using (var cn = new SqlConnection(cnn))
                {
                    cn.Open();

                    if (!ExisteTablaNotificacion(cn))
                    {
                        WriteJson(ctx, new { ok = true, unread = 0, total = 0, tableMissing = true });
                        return;
                    }

                    if (!ExisteTablaNotificacionLectura(cn))
                    {
                        WriteJson(ctx, new { ok = true, unread = 0, total = 0, tableMissingRead = true });
                        return;
                    }

                    using (var cmd = new SqlCommand(@"
SELECT COUNT(*)
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
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.WF_NotificacionLectura L
        WHERE L.NotificacionId = N.Id
          AND L.Usuario = @UserKey
    );", cn))
                    {
                        cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                        unread = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    using (var cmd = new SqlCommand(@"
SELECT COUNT(*)
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
    );", cn))
                    {
                        cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                        total = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                WriteJson(ctx, new { ok = true, unread = unread, total = total });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, new { ok = false, error = ex.Message, unread = 0, total = 0 });
            }
        }

        private static bool ExisteTablaNotificacion(SqlConnection cn)
        {
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.WF_Notificacion', 'U');", cn))
            {
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

        private static bool ExisteTablaNotificacionLectura(SqlConnection cn)
        {
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.WF_NotificacionLectura', 'U');", cn))
            {
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

        private static void WriteJson(HttpContext ctx, object obj)
        {
            var js = new JavaScriptSerializer();
            ctx.Response.Write(js.Serialize(obj));
        }

        public bool IsReusable { get { return true; } }
    }
}
