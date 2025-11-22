using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    // Handler para "doc.entrada": toma parametros de entrada (form) y los vuelca al contexto
    public class HDocEntrada : IManejadorNodo
    {
        public string TipoNodo => "doc.entrada";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            // parameters.entrada: Dictionary<string, object> con los valores del form
            Dictionary<string, object> entrada = null;
            if (nodo.Parameters != null && nodo.Parameters.TryGetValue("entrada", out var v) && v is Dictionary<string, object> dic)
            {
                entrada = dic;
            }

            // Si no hay entrada, crear un placeholder mínimo
            if (entrada == null) entrada = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Construir objeto "documento" a partir de la entrada
            var documento = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            void put(string k, object value)
            {
                if (value != null) documento[k] = value;
            }

            // Mapear campos comunes
            put("nombre", Take(entrada, "documento.nombre"));
            put("ext", Take(entrada, "documento.ext"));
            put("tamMB", Take(entrada, "documento.tamMB"));
            put("clienteNombre", Take(entrada, "cliente.nombre"));
            put("tieneFirma", ToBool(Take(entrada, "documento.tieneFirma")));

            // Guardar jerárquico y plano para compatibilidad
            ctx.Estado["documento"] = documento;
            if (documento.TryGetValue("tieneFirma", out var tf))
                ctx.Estado["documento.tieneFirma"] = tf;

            ctx.Log("[DocEntrada] documento=" + (documento.TryGetValue("nombre", out var n) ? Convert.ToString(n) : "(sin nombre)") +
                    " firma=" + (documento.TryGetValue("tieneFirma", out var f) ? Convert.ToString(f) : "null"));

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        static object Take(Dictionary<string, object> d, string key)
        {
            return (d != null && d.TryGetValue(key, out var v)) ? v : null;
        }

        static bool ToBool(object o)
        {
            if (o is bool b) return b;
            if (o is string s && bool.TryParse(s, out var bb)) return bb;
            if (o is int i) return i != 0;
            if (o is long l) return l != 0L;
            return false;
        }
    }
}
