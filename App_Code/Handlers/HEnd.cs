using Intranet.WorkflowStudio.Runtime;
using System;
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

            try
            {
                string estNeg = null;

                if (nodo?.Parameters != null &&
                    nodo.Parameters.TryGetValue("estadoNegocio", out var tmp) &&
                    tmp != null)
                {
                    estNeg = Convert.ToString(tmp);
                }

                if (!string.IsNullOrWhiteSpace(estNeg))
                {
                    ctx.Estado["wf.estadoNegocio"] = estNeg;
                }

                EntidadService.SnapshotFromState(ctx.Estado, usuario: "app");
            }
            catch { }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }
    }
}