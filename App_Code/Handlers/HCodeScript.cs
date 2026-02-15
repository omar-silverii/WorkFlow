using System;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// code.script (JS) - por seguridad, por defecto está DESHABILITADO.
    ///
    /// Para habilitarlo a futuro, agregá en Web.config:
    ///   <appSettings>
    ///     <add key="WF_EnableCodeScript" value="true" />
    ///   </appSettings>
    ///
    /// Hoy: si está deshabilitado, el nodo falla con un error claro.
    /// </summary>
    public class HCodeScript : IManejadorNodo
    {
        public string TipoNodo => "code.script";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            bool enabled = string.Equals(ConfigurationManager.AppSettings["WF_EnableCodeScript"], "true", StringComparison.OrdinalIgnoreCase);

            if (!enabled)
                throw new InvalidOperationException("code.script está deshabilitado (WF_EnableCodeScript != true).");

            // Placeholder: no ejecutar código arbitrario.
            throw new InvalidOperationException("code.script aún no está implementado (placeholder).");
        }
    }
}
