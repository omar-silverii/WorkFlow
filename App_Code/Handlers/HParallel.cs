using System;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo de control "parallel".
    /// Versión inicial: sólo loguea y sigue por la arista "always".
    /// (Más adelante lo podemos hacer realmente paralelo).
    /// </summary>
    public class HParallel : IManejadorNodo
    {
        public string TipoNodo => "control.parallel";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            ctx.Log("[control.parallel] Nodo paralelo alcanzado (implementación simple: continúa por 'always').");

            return Task.FromResult(new ResultadoEjecucion
            {
                Etiqueta = "always"
            });
        }
    }

    /// <summary>
    /// Nodo de unión de ramas paralelas.
    /// Versión inicial: sólo loguea y sigue por "always".
    /// </summary>
    public class HJoin : IManejadorNodo
    {
        public string TipoNodo => "control.join";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            ctx.Log("[control.join] Nodo de unión alcanzado (implementación simple: continúa por 'always').");

            return Task.FromResult(new ResultadoEjecucion
            {
                Etiqueta = "always"
            });
        }
    }
}
