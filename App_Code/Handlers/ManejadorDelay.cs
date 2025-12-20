using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
     //====== NUEVO: Handler DELAY ======
        public class ManejadorDelay : IManejadorNodo
    {
        public string TipoNodo => "control.delay";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            int ms = 1000;
            if (p.TryGetValue("ms", out var msObj))
            {
                int.TryParse(Convert.ToString(msObj), out ms);
                if (ms < 0) ms = 0;
            }
            ctx.Log($"[Delay] {ms} ms");
            await Task.Delay(ms, ct);
            return new ResultadoEjecucion { Etiqueta = "always" };
        }
    }
}