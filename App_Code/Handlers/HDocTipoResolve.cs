using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "util.docTipo.resolve":
    /// - Recibe docTipoCodigo (ej: NOTA_PEDIDO)
    /// - Busca en dbo.WF_DocTipo (solo activos)
    /// - Escribe en contexto:
    ///     wf.docTipoCodigo
    ///     wf.docTipoId
    ///     wf.contextPrefix
    /// </summary>
    public class HDocTipoResolve : IManejadorNodo
    {
        public string TipoNodo => "util.docTipo.resolve";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // 1) Parámetro requerido: docTipoCodigo (con soporte ${...})
            string codigoTpl = GetString(p, "docTipoCodigo") ?? "";
            string codigo = ctx.ExpandString(codigoTpl);

            if (string.IsNullOrWhiteSpace(codigo))
            {
                ctx.Log("[util.docTipo.resolve] docTipoCodigo vacío; no se puede resolver.");
                ctx.Estado["docTipo.lastError"] = "Falta docTipoCodigo";
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            // 2) Buscar en SQL
            try
            {
                var (docTipoId, contextPrefix) = await GetDocTipoAsync(codigo.Trim(), ct);

                // 3) Escribir en contexto
                ctx.Estado["wf.docTipoCodigo"] = codigo.Trim();
                ctx.Estado["wf.docTipoId"] = docTipoId;
                ctx.Estado["wf.contextPrefix"] = contextPrefix;

                ctx.Log($"[util.docTipo.resolve] OK Codigo={codigo.Trim()} DocTipoId={docTipoId} Prefix={contextPrefix}");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Log("[util.docTipo.resolve] error: " + ex.Message);
                ctx.Estado["docTipo.lastError"] = ex.Message;
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        // -------- Helpers privados --------

        private static async Task<(int DocTipoId, string ContextPrefix)> GetDocTipoAsync(string codigo, CancellationToken ct)
        {
            var csItem = ConfigurationManager.ConnectionStrings["DefaultConnection"];
            if (csItem == null)
                throw new InvalidOperationException("ConnectionString 'DefaultConnection' no encontrada");

            using (var cn = new SqlConnection(csItem.ConnectionString))
            using (var cmd = new SqlCommand(@"
SELECT TOP (1) DocTipoId, ContextPrefix
FROM dbo.WF_DocTipo WITH (READPAST)
WHERE Codigo = @Codigo
  AND EsActivo = 1;", cn))
            {
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;

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

        private static string GetString(Dictionary<string, object> p, string key)
        {
            object v;
            return (p != null && p.TryGetValue(key, out v) && v != null)
                ? Convert.ToString(v)
                : null;
        }
    }
}
