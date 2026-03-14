using System;
using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;

namespace Intranet.WorkflowStudio.WebForms.DocumentProcessing
{
    public static class FacturaElectronicaArExtractor
    {
        public static void Apply(ContextoEjecucion ctx, string prefix, string text)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "factura";
            if (text == null) text = "";

            // TODO: implementación real
            // Por ahora dejamos trazabilidad mínima para verificar que entró por el motor correcto.
            ctx.Estado["biz." + prefix + ".rawTextLen"] = text.Length;
            ctx.Estado["biz." + prefix + ".extractor"] = "FACTURA_AR";
            ctx.Estado["biz." + prefix + ".pendienteImplementacion"] = true;

            ctx.Log("[FacturaElectronicaArExtractor] Ejecutado. Implementación pendiente.");
        }
    }
}