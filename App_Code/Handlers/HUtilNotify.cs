using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo util.notify:
    /// - Expande plantillas en título / mensaje
    /// - Loguea la "notificación"
    /// Por ahora es sólo log; más adelante se puede enchufar a mail/chat/etc.
    /// </summary>
    public class HUtilNotify : IManejadorNodo
    {
        public string TipoNodo => "util.notify";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = (IDictionary<string, object>)(nodo.Parameters
                      ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

            string tituloTpl =
                GetString(p, "titulo") ??
                GetString(p, "title") ??
                "Notificación";

            string mensajeTpl =
                GetString(p, "mensaje") ??
                GetString(p, "message") ??
                string.Empty;

            string canal = (GetString(p, "canal") ?? "log").ToLowerInvariant();
            string nivel = (GetString(p, "nivel") ?? "info").ToLowerInvariant();

            // Soporte de ${...}
            string titulo = ctx.ExpandString(tituloTpl);
            string mensaje = ctx.ExpandString(mensajeTpl);

            string texto = $"[notify/{canal}/{nivel}] {titulo}: {mensaje}";
            ctx.Log(texto);

            // Guardamos último notify en el estado por si un nodo posterior lo quiere usar
            if (ctx.Estado != null)
            {
                ctx.Estado["notify.last.title"] = titulo;
                ctx.Estado["notify.last.message"] = mensaje;
                ctx.Estado["notify.last.canal"] = canal;
                ctx.Estado["notify.last.nivel"] = nivel;
                ctx.Estado["wf.currentNodeId"] = nodo.Id;
                ctx.Estado["wf.currentNodeType"] = nodo.Type;
            }

            var result = new ResultadoEjecucion
            {
                // Notificación no detiene el flujo: sigue por 'always'
                Etiqueta = "always"
            };

            return Task.FromResult(result);
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }
    }
}
