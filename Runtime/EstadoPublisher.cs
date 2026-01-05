using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.Runtime
{
    public interface IEstadoPublisher
    {
        void Publish(IDictionary<string, object> estado, string nodoId, string nodoTipo);
    }
}