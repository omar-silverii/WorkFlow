using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Web;
using System.Web.Script.Serialization;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WF_TaskCounters : IHttpHandler
    {
        public void ProcessRequest(HttpContext ctx)
        {
            ctx.Response.ContentType = "application/json";

            try
            {
                if (ctx?.User?.Identity == null || !ctx.User.Identity.IsAuthenticated)
                {
                    WriteJson(ctx, new
                    {
                        ok = false,
                        error = "No autenticado",
                        pendientes = 0,
                        back = 0,
                        total = 0
                    });
                    return;
                }

                string userKey = (ctx.User.Identity.Name ?? "").Trim();
                string cnn = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                int pendientes = 0;
                int back = 0;

                using (var cn = new SqlConnection(cnn))
                {
                    cn.Open();

                    string sqlPendientes = @"
SELECT COUNT(*)
FROM dbo.WF_Tarea T
WHERE
    T.Estado = 'Pendiente'
    AND
    (
        T.AsignadoA = @UserKey
        OR T.UsuarioAsignado = @UserKey
        OR EXISTS (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = T.RolDestino
        )
    );";

                    using (var cmd = new SqlCommand(sqlPendientes, cn))
                    {
                        cmd.Parameters.AddWithValue("@UserKey", userKey);
                        pendientes = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    string sqlBack = @"
SELECT COUNT(*)
FROM dbo.WF_Tarea T
WHERE
    T.Estado = 'Pendiente'
    AND
    (
        T.AsignadoA = @UserKey
        OR T.UsuarioAsignado = @UserKey
        OR EXISTS (
            SELECT 1
            FROM dbo.WF_UsuarioRol UR
            WHERE UR.Activo = 1
              AND UR.Usuario = @UserKey
              AND UR.RolKey = T.RolDestino
        )
    )
    AND EXISTS
    (
        SELECT 1
        FROM dbo.WF_Tarea TP
        WHERE TP.WF_InstanciaId = T.WF_InstanciaId
          AND TP.Id =
          (
              SELECT MAX(T2.Id)
              FROM dbo.WF_Tarea T2
              WHERE T2.WF_InstanciaId = T.WF_InstanciaId
                AND T2.Id < T.Id
          )
          AND TP.Estado = 'Completada'
          AND TP.Resultado = 'rechazado'
    );";

                    using (var cmd = new SqlCommand(sqlBack, cn))
                    {
                        cmd.Parameters.AddWithValue("@UserKey", userKey);
                        back = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                WriteJson(ctx, new
                {
                    ok = true,
                    pendientes = pendientes,
                    back = back,
                    total = pendientes
                });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, new
                {
                    ok = false,
                    error = ex.Message,
                    pendientes = 0,
                    back = 0,
                    total = 0
                });
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