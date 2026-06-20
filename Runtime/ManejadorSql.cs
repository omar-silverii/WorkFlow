using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
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
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

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

            // ✅ FIX PROFESIONAL:
            // Expandir templates también en el SQL (permite usar transform.map: ${payload.sql.query})
            // Si no hay ${...}, queda igual.
            sql = ctx.ExpandString(sql);

            // 3) parámetros opcionales:
            // Soportar "parameters" (histórico) y "params" (más natural para el JSON)
            Dictionary<string, object> sqlParams = null;

            object pParamsObj = null;
            if (p.ContainsKey("parameters")) pParamsObj = p["parameters"];
            else if (p.ContainsKey("params")) pParamsObj = p["params"];

            if (pParamsObj is JObject jobj)
            {
                sqlParams = jobj.ToObject<Dictionary<string, object>>();
            }
            else if (pParamsObj is Dictionary<string, object> dict)
            {
                sqlParams = dict;
            }
            else if (pParamsObj is IDictionary<string, object> idict)
            {
                sqlParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in idict) sqlParams[kv.Key] = kv.Value;
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

                            // Si es string, expandimos ${...}
                            if (val is string s)
                                val = ctx.ExpandString(s);

                            cmd.Parameters.AddWithValue("@" + kv.Key, val ?? DBNull.Value);
                        }
                    }

                    await cn.OpenAsync(ct);

                    // fix28b: si es una consulta que devuelve filas, leer el resultado
                    // y dejarlo visible en DatosContexto para que pueda verse desde
                    // WF_Instancias -> Datos y usarse luego en IFs.
                    if (EsConsultaConResultado(sql))
                    {
                        int maxRows = LeerEnteroParametro(p, "maxRows", 100);
                        if (maxRows <= 0) maxRows = 100;
                        if (maxRows > 500) maxRows = 500;

                        using (var dr = await cmd.ExecuteReaderAsync(ct))
                        {
                            var rowsJson = new JArray();
                            bool truncated = false;

                            while (await dr.ReadAsync(ct))
                            {
                                if (rowsJson.Count >= maxRows)
                                {
                                    truncated = true;
                                    break;
                                }

                                var row = new JObject();
                                for (int i = 0; i < dr.FieldCount; i++)
                                {
                                    string col = dr.GetName(i);
                                    if (string.IsNullOrWhiteSpace(col)) col = "Col" + (i + 1).ToString();

                                    object val = dr.IsDBNull(i) ? null : dr.GetValue(i);
                                    row[col] = ToJToken(val);
                                }

                                rowsJson.Add(row);
                            }

                            ctx.Estado["sql.rows"] = rowsJson;
                            ctx.Estado["sql.rowCount"] = rowsJson.Count;
                            ctx.Estado["sql.truncated"] = truncated;

                            if (rowsJson.Count > 0)
                            {
                                var first = rowsJson[0] as JObject;
                                ctx.Estado["sql.first"] = first;

                                if (first != null && first.Properties().Any())
                                    ctx.Estado["sql.scalar"] = first.Properties().First().Value;
                            }
                            else
                            {
                                ctx.Estado["sql.first"] = null;
                                ctx.Estado["sql.scalar"] = null;
                            }

                            ctx.Log($"[data.sql] SELECT ejecutado. Filas devueltas: {rowsJson.Count}" + (truncated ? $" (truncado a {maxRows})" : ""));
                        }
                    }
                    else
                    {
                        int rows = await cmd.ExecuteNonQueryAsync(ct);

                        // Mantener compatibilidad: para INSERT/UPDATE/DELETE, sql.rows sigue siendo número.
                        ctx.Estado["sql.rows"] = rows;
                        ctx.Estado["sql.rowsAffected"] = rows;
                        ctx.Estado["sql.rowCount"] = rows;
                        ctx.Log($"[data.sql] SQL ejecutado. Filas afectadas: {rows}");
                    }
                }

                // Éxito: seguimos por "always"
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Log($"[data.sql/error] {ex.GetType().Name}: {ex.Message}");
                ctx.Estado["sql.error"] = ex.Message;

                // importante: devolvemos "error" para que el grafo pueda cablear a util.error
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        private static bool EsConsultaConResultado(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return false;

            string t = sql.TrimStart();

            // Quitar comentarios de línea iniciales simples.
            while (t.StartsWith("--", StringComparison.Ordinal))
            {
                int nl = t.IndexOf('\n');
                if (nl < 0) return false;
                t = t.Substring(nl + 1).TrimStart();
            }

            return t.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        }

        private static int LeerEnteroParametro(Dictionary<string, object> p, string key, int defaultValue)
        {
            if (p == null || !p.ContainsKey(key) || p[key] == null) return defaultValue;
            int v;
            return int.TryParse(Convert.ToString(p[key]), out v) ? v : defaultValue;
        }

        private static JToken ToJToken(object value)
        {
            if (value == null || value == DBNull.Value) return JValue.CreateNull();

            if (value is DateTime dt) return new JValue(dt);
            if (value is Guid gd) return new JValue(gd.ToString());
            if (value is byte[] bytes) return new JValue(Convert.ToBase64String(bytes));

            return JToken.FromObject(value);
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
