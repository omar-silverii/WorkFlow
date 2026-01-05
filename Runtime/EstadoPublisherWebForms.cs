using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.Runtime
{
    public sealed class EstadoPublisherWebForms : IEstadoPublisher
    {
        public void Publish(IDictionary<string, object> estado, string nodoId, string nodoTipo)
        {
            try
            {
                var items = System.Web.HttpContext.Current?.Items;
                if (items == null || estado == null) return;

                if (!string.IsNullOrEmpty(nodoId))
                    estado["wf.currentNodeId"] = nodoId;

                if (!string.IsNullOrEmpty(nodoTipo))
                    estado["wf.currentNodeType"] = nodoTipo;

                items["WF_CTX_ESTADO"] = estado;
            }
            catch
            {
                // nunca romper por HttpContext
            }
        }
    }
}
