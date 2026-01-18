using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.Script.Serialization;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WfDefiniciones : IHttpHandler
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            // Si tu sitio usa autenticación Windows, esto ya queda protegido por IIS.
            // Si querés endurecerlo, podés exigir IsAuthenticated.
            // if (context.User == null || !context.User.Identity.IsAuthenticated) { context.Response.StatusCode = 401; return; }

            bool soloActivas = ToBool(context.Request["activo"], true);

            var items = new List<object>();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT  [Key], Nombre, Version, Id, Activo
FROM    dbo.WF_Definicion
WHERE   (@SoloActivas = 0 OR Activo = 1)
  AND   ISNULL([Key], '') <> ''
ORDER BY [Key], Version DESC, Id DESC;";

                cmd.Parameters.Add("@SoloActivas", SqlDbType.Bit).Value = soloActivas ? 1 : 0;

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        items.Add(new
                        {
                            key = dr.IsDBNull(0) ? "" : dr.GetString(0),
                            nombre = dr.IsDBNull(1) ? "" : dr.GetString(1),
                            version = dr.IsDBNull(2) ? 0 : dr.GetInt32(2),
                            id = dr.IsDBNull(3) ? 0 : dr.GetInt32(3),
                            activo = !dr.IsDBNull(4) && dr.GetBoolean(4)
                        });
                    }
                }
            }

            var json = new JavaScriptSerializer().Serialize(items);
            context.Response.Write(json);
        }

        public bool IsReusable => false;

        private static bool ToBool(string s, bool def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }
    }
}
