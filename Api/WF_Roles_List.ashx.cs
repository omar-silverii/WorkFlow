using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WF_Roles_List : IHttpHandler
    {
        private static string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var dt = new DataTable();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT DISTINCT
    r.RolKey,
    r.Nombre
FROM dbo.WF_Rol r
INNER JOIN dbo.WF_UsuarioRol ur
    ON ur.RolKey = r.RolKey
   AND ur.Activo = 1
INNER JOIN dbo.WF_User u
    ON u.UserKey = ur.Usuario
   AND u.Activo = 1
WHERE r.Activo = 1
ORDER BY r.Nombre, r.RolKey;", cn))
            using (var da = new SqlDataAdapter(cmd))
            {
                cn.Open();
                da.Fill(dt);
            }

            context.Response.Write(JsonConvert.SerializeObject(dt));
        }

        public bool IsReusable
        {
            get { return false; }
        }
    }
}