using System;
using System.Configuration;
using System.Data;
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
            ctx.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            ctx.Response.Cache.SetNoStore();
            ctx.Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));

            try
            {
                if (ctx?.User?.Identity == null || !ctx.User.Identity.IsAuthenticated)
                {
                    WriteJson(ctx, new
                    {
                        ok = false,
                        error = "No autenticado",
                        pendientes = 0,
                        directas = 0,
                        porRol = 0,
                        back = 0,
                        total = 0
                    });
                    return;
                }

                string userKey = (ctx.User.Identity.Name ?? "").Trim();
                string cnn = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                int total = 0;
                int directas = 0;
                int porRol = 0;
                int back = 0;

                using (var cn = new SqlConnection(cnn))
                {
                    cn.Open();

                    string sqlResumen = @"
;WITH Pendientes AS
(
    SELECT
        T.Id,
        T.WF_InstanciaId,
        CASE
            WHEN T.AsignadoA = @UserKey OR T.UsuarioAsignado = @UserKey THEN 1
            ELSE 0
        END AS EsDirecta,
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.WF_UsuarioRol UR
                WHERE UR.Activo = 1
                  AND UR.Usuario = @UserKey
                  AND UR.RolKey = T.RolDestino
            ) THEN 1
            ELSE 0
        END AS EsRol
    FROM dbo.WF_Tarea T
    INNER JOIN dbo.WF_Instancia I ON I.Id = T.WF_InstanciaId
    WHERE T.Estado = 'Pendiente'
)
SELECT
    ISNULL(SUM(CASE WHEN EsDirecta = 1 OR EsRol = 1 THEN 1 ELSE 0 END), 0) AS Total,
    ISNULL(SUM(CASE WHEN EsDirecta = 1 THEN 1 ELSE 0 END), 0) AS Directas,
    ISNULL(SUM(CASE WHEN EsDirecta = 0 AND EsRol = 1 THEN 1 ELSE 0 END), 0) AS PorRol
FROM Pendientes;";

                    using (var cmd = new SqlCommand(sqlResumen, cn))
                    {
                        cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;

                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                total = Convert.ToInt32(rd["Total"]);
                                directas = Convert.ToInt32(rd["Directas"]);
                                porRol = Convert.ToInt32(rd["PorRol"]);
                            }
                        }
                    }

                    string sqlBack = @"
SELECT COUNT(*)
FROM dbo.WF_Tarea T
INNER JOIN dbo.WF_Instancia I ON I.Id = T.WF_InstanciaId
WHERE
    T.Estado = 'Pendiente'
    AND
    (
        T.AsignadoA = @UserKey
        OR T.UsuarioAsignado = @UserKey
        OR EXISTS
        (
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
                        cmd.Parameters.Add("@UserKey", SqlDbType.NVarChar, 200).Value = userKey;
                        back = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                WriteJson(ctx, new
                {
                    ok = true,
                    pendientes = total,
                    directas = directas,
                    porRol = porRol,
                    back = back,
                    total = total
                });
            }
            catch (Exception ex)
            {
                WriteJson(ctx, new
                {
                    ok = false,
                    error = ex.Message,
                    pendientes = 0,
                    directas = 0,
                    porRol = 0,
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
