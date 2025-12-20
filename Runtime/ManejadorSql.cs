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

                            // Si es string, usamos la misma interpolación que en otros handlers:
                            // esto expande cualquier ${...} dentro del texto, no solo si es todo el valor.
                            if (val is string s)
                            {
                                // ctx.ExpandString ya sabe leer ctx.Estado["doc"], "wf.*", "tarea.*", etc.
                                val = ctx.ExpandString(s);
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

        // ============================================================
        // Helpers reutilizables (para otros handlers)
        // ============================================================

        /// <summary>
        /// Obtiene DocTipoId y ContextPrefix desde dbo.WF_DocTipo por Codigo (solo activos).
        /// Usa la misma connectionStringName que el nodo (default: DefaultConnection).
        /// </summary>
        public async Task<(int DocTipoId, string ContextPrefix)> GetDocTipoByCodigoAsync(
            string codigo,
            string connectionStringName = "DefaultConnection",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(codigo))
                throw new ArgumentException("codigo requerido", nameof(codigo));

            // 1) resolver cadena de conexión (igual que en EjecutarAsync)
            var csItem = ConfigurationManager.ConnectionStrings[connectionStringName];
            if (csItem == null)
                throw new InvalidOperationException($"ConnectionString '{connectionStringName}' no encontrada");

            string cnnString = csItem.ConnectionString;

            // 2) query
            const string sql = @"
SELECT TOP (1) DocTipoId, ContextPrefix
FROM dbo.WF_DocTipo WITH (READPAST)
WHERE Codigo = @Codigo AND EsActivo = 1;";

            using (var cn = new SqlConnection(cnnString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo.Trim();

                await cn.OpenAsync(ct);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct))
                {
                    if (!await dr.ReadAsync(ct))
                        throw new InvalidOperationException($"DocTipo inexistente o inactivo: '{codigo}'");

                    int docTipoId = dr.GetInt32(0);
                    string prefix = dr.GetString(1);

                    return (docTipoId, prefix);
                }
            }
        }

    }
}
