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

            // nombre de la cadena en web.config (por defecto: DefaultConnection)
            string cnnName = p.ContainsKey("connectionStringName")
                ? Convert.ToString(p["connectionStringName"])
                : "DefaultConnection";

            // 2) SQL a ejecutar: commandText o, si no viene, query
            string sql = null;

            if (p.ContainsKey("commandText") && p["commandText"] != null)
                sql = Convert.ToString(p["commandText"]);

            if (string.IsNullOrWhiteSpace(sql) &&
                p.ContainsKey("query") && p["query"] != null)
                sql = Convert.ToString(p["query"]);

            // Si sigue vacío, consideramos que es un ERROR de configuración del nodo
            if (string.IsNullOrWhiteSpace(sql))
            {
                ctx.Log("[data.sql] no hay SQL (commandText/query)");
                ctx.Estado["sql.error"] = "Falta commandText/query en nodo data.sql";
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // 3) parámetros opcionales
            Dictionary<string, object> sqlParams = null;
            if (p.ContainsKey("parameters") && p["parameters"] is Newtonsoft.Json.Linq.JObject jobj)
            {
                sqlParams = jobj.ToObject<Dictionary<string, object>>();
            }

            // 4) resolver cadena de conexión
            var csItem = ConfigurationManager.ConnectionStrings[cnnName];
            if (csItem == null)
            {
                ctx.Log($"[data.sql/error] connectionString '{cnnName}' no encontrada en Web.config");
                ctx.Estado["sql.error"] = $"ConnectionString '{cnnName}' no encontrada";
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            string cnnString = csItem.ConnectionString;
            ctx.Log($"[data.sql] usando connectionStringName='{cnnName}'");

            try
            {
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
                                val = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                            }

                            cmd.Parameters.AddWithValue("@" + kv.Key, val ?? DBNull.Value);
                        }
                    }

                    await cn.OpenAsync(ct);
                    int rows = await cmd.ExecuteNonQueryAsync(ct);

                    ctx.Estado["sql.rows"] = rows;
                    ctx.Log($"[data.sql] SQL ejecutado. Filas afectadas: {rows}");
                }

                // Éxito: seguimos por "always"
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                // Acá caés cuando la tabla no existe, constraint, timeout, etc.
                ctx.Log($"[data.sql/error] {ex.GetType().Name}: {ex.Message}");
                ctx.Estado["sql.error"] = ex.Message;

                // importante: devolvemos "error" para que el grafo pueda cablear a util.error
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }
    }
}
