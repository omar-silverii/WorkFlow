using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "util.error".
    /// - Loguea un mensaje de error expandiendo variables (${...}).
    /// - Marca wf.error / wf.error.message en el contexto para que el Runtime
    ///   pueda dejar la instancia en Estado = 'Error'.
    /// - Si notificar = true, deja una pseudo-notificación en logs y en ctx.Estado.
    /// - Si capturar = true, la etiqueta es "always" (el error se considera manejado).
    /// - Si capturar = false, la etiqueta es "error" (para flujos más avanzados).
    /// </summary>
    public class HUtilError : IManejadorNodo
    {
        public string TipoNodo => "util.error";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            bool capturar = GetBool(p, "capturar");
            bool volverIntentar = GetBool(p, "volverAIntentar");
            bool notificar = GetBool(p, "notificar");

            // Mensaje parametrizable desde el inspector
            string mensajeTpl = GetString(p, "mensaje");

            // Default amigable si no se configuró nada
            if (string.IsNullOrWhiteSpace(mensajeTpl))
            {
                mensajeTpl = "Fallo en prueba para instancia ${wf.instanceId}";
            }

            // Expandimos ${...} usando el ContextoEjecucion
            string mensajeExp = ctx.ExpandString(mensajeTpl) ?? string.Empty;

            // Log principal
            ctx.Log($"[util.error] mensaje = \"{mensajeExp}\"");

            // === NUEVO: marcar error a nivel de instancia ===
            if (ctx.Estado != null)
            {
                ctx.Estado["wf.error"] = true;
                ctx.Estado["wf.error.message"] = mensajeExp;
                ctx.Estado["wf.error.nodeId"] = nodo.Id;
                ctx.Estado["wf.error.nodeType"] = nodo.Type;
                ctx.Estado["wf.error.timestamp"] = DateTime.Now;
            }
            ctx.Log("[util.error] wf.error = true en contexto.");

            // Si se marcó "notificar", generamos una pseudo-notificación
            if (notificar)
            {
                ctx.Log($"[util.error/notify] Notificación por error: {mensajeExp}");

                ctx.Estado["util.error.lastNotify"] = new
                {
                    mensaje = mensajeExp,
                    nodoId = nodo.Id,
                    nodoTipo = nodo.Type,
                    fecha = DateTime.Now
                };
            }

            // Por ahora no implementamos lógica de reintento automático,
            // pero dejamos el flag disponible para el futuro.
            if (volverIntentar)
            {
                ctx.Log("[util.error] volverAIntentar = true (sin lógica de retry implementada todavía).");
            }

            // Si capturar = true, consideramos el error manejado y seguimos por "always".
            // Si capturar = false, dejamos la etiqueta "error".
            string etiqueta = capturar ? "always" : "error";

            var res = new ResultadoEjecucion
            {
                Etiqueta = etiqueta
            };

            return Task.FromResult(res);
        }

        // === Helpers ===

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool GetBool(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(Convert.ToString(v), out var b2)) return b2;
                if (int.TryParse(Convert.ToString(v), out var i)) return i != 0;
            }
            return false;
        }
    }
}
