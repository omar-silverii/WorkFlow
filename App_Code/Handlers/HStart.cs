using System;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HStart : IManejadorNodo
    {
        public string TipoNodo => "util.start";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            // Si estamos reanudando (desde human.task), no queremos "simular" un inicio nuevo.
            if (ctx != null && ctx.Estado != null &&
                ctx.Estado.TryGetValue("wf.startNodeIdOverride", out var ov) && ov != null)
            {
                // Log opcional: si querés silencio total en resume, dejá este log comentado.
                ctx.Log("[Start] (resume: omitido)");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }

            ctx.Log("[Start]");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}
