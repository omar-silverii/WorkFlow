using Intranet.WorkflowStudio.WebForms;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.Runtime
{
    // este nombre tiene que coincidir con el del JSON: "data.sql"
    public class ManejadorSql : IManejadorNodo
    {
        public string TipoNodo => "data.sql";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            // 1) tomar parámetros
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // nombre de la cadena en web.config
            string cnnName = p.ContainsKey("connectionStringName")
                ? Convert.ToString(p["connectionStringName"])
                : "DefaultConnection";

            // SQL a ejecutar
            string sql = p.ContainsKey("commandText")   
                ? Convert.ToString(p["commandText"])
                : null;

            if (string.IsNullOrWhiteSpace(sql))
            {
                ctx.Log("SQL: no hay commandText");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }

            // parámetros opcionales
            Dictionary<string, object> sqlParams = null;
            if (p.ContainsKey("parameters") && p["parameters"] is Newtonsoft.Json.Linq.JObject jobj)
            {
                sqlParams = jobj.ToObject<Dictionary<string, object>>();
            }

            string cnnString = ConfigurationManager.ConnectionStrings[cnnName].ConnectionString;

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                if (sqlParams != null)
                {
                    foreach (var kv in sqlParams)
                    {
                        object val = kv.Value;

                        // si es del tipo "${path}" => resolver contra ctx.Estado
                        if (val is string sv && sv.StartsWith("${") && sv.EndsWith("}"))
                        {
                            var path = sv.Substring(2, sv.Length - 3); // sin ${}
                            val = Intranet.WorkflowStudio.WebForms.ContextoEjecucion.ResolverPath(ctx.Estado, path);
                        }
                        cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);
                    }
                }

                await cn.OpenAsync(ct);
                int rows = await cmd.ExecuteNonQueryAsync(ct);
                ctx.Log($"SQL ejecutado. Filas afectadas: {rows}");
            }

            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }
}
