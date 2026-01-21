using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
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

            var action = (context.Request["action"] ?? "").Trim().ToLowerInvariant();

            if (action == "create")
            {
                HandleCreate(context);
                return;
            }

            // default: list
            HandleList(context);
        }

        private static void HandleList(HttpContext context)
        {
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

        private static void HandleCreate(HttpContext context)
        {
            // Acepta POST JSON: { nombre: "...", prefix: "WF-" (opcional) }
            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 405;
                context.Response.Write("{\"ok\":false,\"error\":\"Method not allowed\"}");
                return;
            }

            string body;
            using (var sr = new StreamReader(context.Request.InputStream))
                body = sr.ReadToEnd();

            var ser = new JavaScriptSerializer();
            Dictionary<string, object> dto = null;
            try { dto = ser.Deserialize<Dictionary<string, object>>(body); }
            catch { dto = new Dictionary<string, object>(); }

            string nombre = (dto != null && dto.ContainsKey("nombre")) ? Convert.ToString(dto["nombre"]) : null;
            nombre = (nombre ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nombre)) nombre = "Subflow";

            string prefix = (dto != null && dto.ContainsKey("prefix")) ? Convert.ToString(dto["prefix"]) : null;
            prefix = (prefix ?? "WF-").Trim();
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "WF-";

            // Key autogenerado (estable)
            string key = prefix + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            // JsonDef template minimal: start -> logger -> end
            string jsonDef = GetTemplateJson(nombre);

            int newId;
            int version = 1;
            string creadoPor = (context.User != null && context.User.Identity != null && context.User.Identity.IsAuthenticated)
                                ? context.User.Identity.Name
                                : "workflow.api";

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO dbo.WF_Definicion
    ([Key], Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef)
VALUES
    (@Key, @Codigo, @Nombre, @Version, 1, GETDATE(), @CreadoPor, @JsonDef);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 80).Value = key;
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = key;
                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                cmd.Parameters.Add("@Version", SqlDbType.Int).Value = version;
                cmd.Parameters.Add("@CreadoPor", SqlDbType.NVarChar, 100).Value = (object)creadoPor ?? DBNull.Value;
                cmd.Parameters.Add("@JsonDef", SqlDbType.NVarChar).Value = jsonDef;

                cn.Open();
                newId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            context.Response.Write(ser.Serialize(new
            {
                ok = true,
                id = newId,
                key = key,
                nombre = nombre,
                version = version
            }));
        }

        private static string GetTemplateJson(string nombre)
        {
            // Minimal, compatible con tu editor: Start + Logger + End
            var safeName = (nombre ?? "Subflow").Trim();

            return @"{
  ""StartNodeId"": ""n1"",
  ""Nodes"": {
    ""n1"": { ""Id"": ""n1"", ""Type"": ""util.start"", ""Label"": ""Inicio"", ""Parameters"": { ""position"": { ""x"": 240, ""y"": 80 } } },
    ""n2"": { ""Id"": ""n2"", ""Type"": ""util.logger"", ""Label"": ""Logger"", ""Parameters"": { ""level"": ""Info"", ""message"": ""SUBFLOW: " + EscapeForJson(safeName) + @" input=${input}"", ""position"": { ""x"": 240, ""y"": 170 } } },
    ""n3"": { ""Id"": ""n3"", ""Type"": ""util.end"", ""Label"": ""Fin"", ""Parameters"": { ""position"": { ""x"": 240, ""y"": 260 } } }
  },
  ""Edges"": [
    { ""Id"": ""e1"", ""From"": ""n1"", ""To"": ""n2"", ""Condition"": ""always"" },
    { ""Id"": ""e2"", ""From"": ""n2"", ""To"": ""n3"", ""Condition"": ""always"" }
  ],
  ""Meta"": { ""Name"": """ + EscapeForJson(safeName) + @""", ""Template"": ""SUBFLOW"" }
}";
        }

        private static string EscapeForJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
