using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.Runtime
{
    public class WorkflowFieldRegistry
    {
        public List<FieldNamespace> Namespaces { get; set; } = new List<FieldNamespace>();
    }

    public class FieldNamespace
    {
        public string Name { get; set; }
        public List<FieldDefinition> Fields { get; set; } = new List<FieldDefinition>();
    }

    public class FieldDefinition
    {
        public string Path { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
    }

    public static class WorkflowFieldRegistryBuilder
    {
        public static WorkflowFieldRegistry Build(string jsonDef, string connectionString)
        {
            var registry = new WorkflowFieldRegistry();

            if (string.IsNullOrWhiteSpace(jsonDef))
                return registry;

            var j = JObject.Parse(jsonDef);

            var nodes = j["Nodes"] as JObject;
            if (nodes == null)
                return registry;

            var docTiposUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in nodes.Properties())
            {
                var node = prop.Value as JObject;
                if (node == null) continue;

                var type = node["Type"]?.ToString();
                var parameters = node["Parameters"] as JObject;

                if (parameters == null) continue;

                // Detectar DocTipo
                if (type == "doc.entrada" || type == "doc.search" || type == "util.docTipo.resolve")
                {
                    var codigo = parameters["docTipoCodigo"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(codigo))
                        docTiposUsados.Add(codigo);
                }

                // Detectar outputPrefix
                var op = parameters["outputPrefix"]?.ToString();
                if (!string.IsNullOrWhiteSpace(op))
                    outputPrefixes.Add(op);
            }

            // 1) wf.*
            registry.Namespaces.Add(new FieldNamespace
            {
                Name = "wf",
                Fields = new List<FieldDefinition>
                {
                    new FieldDefinition { Path = "wf.instanceId", Type = "number", Source = "System" },
                    new FieldDefinition { Path = "wf.definitionKey", Type = "string", Source = "System" },
                    new FieldDefinition { Path = "wf.now", Type = "datetime", Source = "System" }
                }
            });

            // 2) DocTipos → biz.doc.*
            foreach (var codigo in docTiposUsados)
            {
                var fields = LoadDocTipoFields(codigo, connectionString);

                registry.Namespaces.Add(new FieldNamespace
                {
                    Name = "biz.doc",
                    Fields = fields
                });
            }

            // 3) outputPrefix dinámicos
            foreach (var prefix in outputPrefixes)
            {
                registry.Namespaces.Add(new FieldNamespace
                {
                    Name = prefix,
                    Fields = new List<FieldDefinition>
                    {
                        new FieldDefinition { Path = prefix + ".rows", Type = "number", Source = "NodeOutput" },
                        new FieldDefinition { Path = prefix + ".data", Type = "object", Source = "NodeOutput" }
                    }
                });
            }

            return registry;
        }

        private static List<FieldDefinition> LoadDocTipoFields(string codigo, string connectionString)
        {
            var list = new List<FieldDefinition>();

            using (var cn = new SqlConnection(connectionString))
            using (var cmd = new SqlCommand(@"
                SELECT CampoNombre, TipoDato
                FROM WF_DocTipoReglaExtract
                WHERE DocTipoCodigo = @codigo", cn))
            {
                cmd.Parameters.AddWithValue("@codigo", codigo);

                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        var campo = dr["CampoNombre"].ToString();
                        var tipo = dr["TipoDato"].ToString();

                        list.Add(new FieldDefinition
                        {
                            Path = "biz.doc." + campo,
                            Type = tipo,
                            Source = "DocTipo:" + codigo
                        });
                    }
                }
            }

            return list;
        }
    }
}
