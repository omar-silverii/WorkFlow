using Intranet.WorkflowStudio.Runtime;
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

            // ✅ ENTIDAD: snapshot final (si existe entidad.id en el contexto)
            try
            {
                // usuario: si no lo tenés en ctx.Estado, dejamos "app"
                EntidadService.SnapshotFromState(ctx.Estado, usuario: "app");
            }
            catch { }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}