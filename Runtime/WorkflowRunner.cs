using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;   // <-- NUEVO (HttpContext)

namespace Intranet.WorkflowStudio.Runtime
{
    /// <summary>
    /// Runner productivo: deserializa el workflow y lo ejecuta con el motor.
    /// Reemplaza a MotorDemo (demo/UX).
    /// </summary>
    public static class WorkflowRunner
    {
        public static WorkflowDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON de workflow vacío", nameof(json));

            var wf = JsonConvert.DeserializeObject<WorkflowDef>(json);
            if (wf == null)
                throw new InvalidOperationException("No se pudo deserializar el workflow (wf=null).");

            if (wf.Nodes == null || wf.Nodes.Count == 0)
                throw new InvalidOperationException("Workflow inválido: Nodes vacío o null.");

            if (string.IsNullOrWhiteSpace(wf.StartNodeId))
                throw new InvalidOperationException("Workflow inválido: StartNodeId vacío.");

            return wf;
        }

        private static List<IManejadorNodo> CrearHandlersBase()
        {
            // OBLIGATORIOS para cualquier workflow
            return new List<IManejadorNodo>
            {
                new HStart(),
                new HEnd(),
                new HLogger(),
                new HIf(),
                new HSubflow(),

                // básicos que ya usás
                new HNotify(),
                new HError(),
                new HHumanTask(),

                // si los tenés como base
                new HHttpRequest(),
                new HDocEntrada(),
                new HFileWrite()
            };
        }

        /// <summary>
        /// Ejecuta el workflow con handlers BASE + handlers extra provistos.
        /// </summary>
        public static async Task EjecutarAsync(
            WorkflowDef wf,
            Action<string> log,
            IEnumerable<IManejadorNodo> handlers,
            CancellationToken ct)
        {
            if (wf == null) throw new ArgumentNullException(nameof(wf));

           
            // ✅ Siempre incluir BASE (util.start/util.end/control.if/etc.)
            var all = CrearHandlersBase();

            // ✅ Agregar extras (si vienen)
            if (handlers != null)
                all.AddRange(handlers.Where(h => h != null));

            var motor = new MotorFlujo(
                handlers: all,
                estadoPublisher: new EstadoPublisherWebForms() // mantiene WF_CTX_ESTADO para logging/runtime
            );

            await motor.EjecutarAsync(wf, log ?? (_ => { }), ct);
        }
    }
}
