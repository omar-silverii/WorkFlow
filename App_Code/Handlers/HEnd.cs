using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    public class HEnd : IManejadorNodo
    {
        public string TipoNodo => "util.end";
        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            ctx.Log("[End]");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}