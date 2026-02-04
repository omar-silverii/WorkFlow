using System;
using System.Collections;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Intranet.WorkflowStudio.Runtime
{
    /// <summary>
    /// Contexto "ambiente" para escenarios fuera de ASP.NET (Console/Worker),
    /// donde HttpContext.Current puede perderse por async/await.
    /// </summary>
    public static class WorkflowAmbient
    {
        // Usamos IDictionary para poder guardar lo mismo que HttpContext.Items
        public static readonly AsyncLocal<IDictionary> Items = new AsyncLocal<IDictionary>();
    }
}