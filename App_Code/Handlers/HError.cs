using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HError : IManejadorNodo
    {
        public string TipoNodo { get { return "util.error"; } }

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            var capturar = p.ContainsKey("capturar") && ContextoEjecucion.ToBool(p["capturar"]);
            var notificar = p.ContainsKey("notificar") && ContextoEjecucion.ToBool(p["notificar"]);
            var reintentar = p.ContainsKey("volverAIntentar") && ContextoEjecucion.ToBool(p["volverAIntentar"]);

            ctx.Log("[Error] capturar=" + capturar + " notificar=" + notificar + " retry=" + reintentar);
            // En este motor mínimo no forzamos reintentos globales; dejamos rastro en log/estado para futuras mejoras.
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}