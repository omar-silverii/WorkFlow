using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms

{
    public class HStart : IManejadorNodo
    {
        public string TipoNodo => "util.start";
        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            ctx.Log("[Start]");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}


