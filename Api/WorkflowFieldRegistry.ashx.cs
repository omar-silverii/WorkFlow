using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Web;
using System.Web.Script.Serialization;
using System.Configuration;

namespace Intranet.WorkflowStudio.WebForms.Api
{
    public class WorkflowFieldRegistry : IHttpHandler
    {
        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            try
            {
                // Nombre de cadena (default: DefaultConnection)
                string cnnName = context.Request["connectionStringName"];
                if (string.IsNullOrWhiteSpace(cnnName)) cnnName = "DefaultConnection";

                var data = BuildRegistry(cnnName);

                var ser = new JavaScriptSerializer();
                context.Response.Write(ser.Serialize(data));
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.Write("{\"error\":\"" + JsSafe(ex.Message) + "\"}");
            }
        }

        private object BuildRegistry(string cnnName)
        {
            var namespaces = new List<object>();

            // ---- básicos genéricos
            namespaces.Add(new
            {
                name = "wf",
                fields = new[]
                {
                    new { path = "wf.instanceId", label = "ID de instancia" },
                    new { path = "wf.definitionKey", label = "Clave de definición" },
                    new { path = "wf.now", label = "Fecha/hora" },
                    new { path = "wf.user", label = "Usuario" }
                }
            });

            namespaces.Add(new
            {
                name = "input",
                fields = new[]
                {
                    new { path = "input.filePath", label = "Ruta archivo" },
                    new { path = "input.docTipo", label = "Tipo de documento" },
                    new { path = "input.docId", label = "ID documento" }
                }
            });

            namespaces.Add(new
            {
                name = "payload",
                fields = new[]
                {
                    new { path = "payload.status", label = "HTTP status" },
                    new { path = "payload.message", label = "Mensaje" },
                    new { path = "payload.data", label = "Data" }
                }
            });

            namespaces.Add(new
            {
                name = "sql",
                fields = new[]
                {
                    new { path = "sql.rows", label = "Filas" },
                    new { path = "sql.data", label = "Data" }
                }
            });

            namespaces.Add(new
            {
                name = "queue",
                fields = new[]
                {
                    new { path = "queue.hasMessage", label = "Hay mensaje" },
                    new { path = "queue.messageId", label = "ID mensaje" },
                    new { path = "queue.message", label = "Mensaje" }
                }
            });

            namespaces.Add(new
            {
                name = "subflow",
                fields = new[]
                {
                    new { path = "subflow.instanceId", label = "ID subflujo" },
                    new { path = "subflow.childState", label = "Estado hijo" },
                    new { path = "subflow.estado", label = "Estado" }
                }
            });

            // ---- dinámico desde DocTipos + Reglas: biz.<ContextPrefix>.<campo>
            var bizFields = new List<object>();

            using (SqlConnection cn = GetCnn(cnnName))
            {
                cn.Open();

                string sql = @"
SELECT d.DocTipoId, d.Codigo, d.ContextPrefix, r.Campo
FROM WF_DocTipo d
JOIN WF_DocTipoReglaExtract r ON r.DocTipoId = d.DocTipoId
WHERE d.EsActivo = 1 AND r.Activo = 1
ORDER BY d.DocTipoId, r.Orden, r.Id;";

                using (SqlCommand cmd = new SqlCommand(sql, cn))
                using (SqlDataReader dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string codigo = Convert.ToString(dr["Codigo"] ?? "");
                        string prefix = Convert.ToString(dr["ContextPrefix"] ?? "");
                        string campo = Convert.ToString(dr["Campo"] ?? "");

                        prefix = (prefix ?? "").Trim();
                        campo = (campo ?? "").Trim();

                        if (prefix.Length == 0 || campo.Length == 0) continue;

                        string path = "biz." + prefix + "." + campo;

                        bizFields.Add(new
                        {
                            path = path,
                            label = campo,
                            docTipo = codigo
                        });
                    }
                }
            }

            namespaces.Add(new
            {
                name = "biz",
                fields = bizFields
            });

            return new { namespaces = namespaces };
        }

        private SqlConnection GetCnn(string cnnName)
        {
            var cs = ConfigurationManager.ConnectionStrings[cnnName];
            if (cs == null || string.IsNullOrWhiteSpace(cs.ConnectionString))
                throw new Exception("No existe connectionString '" + cnnName + "' en Web.config.");

            return new SqlConnection(cs.ConnectionString);
        }

        private static string JsSafe(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}