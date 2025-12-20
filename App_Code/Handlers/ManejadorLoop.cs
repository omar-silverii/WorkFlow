using Intranet.WorkflowStudio.WebForms;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Intranet.WorkflowStudio.Runtime
{
    // ====== NUEVO: Handler LOOP (foreach) ======
    public class ManejadorLoop : IManejadorNodo
    {
        public string TipoNodo => "control.loop";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();
            string id = nodo.Id ?? "loop";
            string baseKey = $"loop.{id}";
            string itemsKey = $"{baseKey}.items";
            string indexKey = $"{baseKey}.index";

            // Cargar o inicializar items
            if (!ctx.Estado.TryGetValue(itemsKey, out var itemsObj))
            {
                IList<object> items = ObtenerItemsIniciales(ctx, p);
                ctx.Estado[itemsKey] = items;
                ctx.Estado[indexKey] = 0;
            }

            var list = NormalizarALista(ctx.Estado[itemsKey]);
            int idx = Convert.ToInt32(ctx.Estado[indexKey]);

            // Límite opcional
            int max = int.MaxValue;
            if (p.TryGetValue("max", out var maxObj))
            {
                if (int.TryParse(Convert.ToString(maxObj), out var m) && m > 0) max = m;
            }

            if (list == null || list.Count == 0 || idx >= list.Count || idx >= max)
            {
                ctx.Log($"[Loop] fin (count={(list?.Count ?? 0)}, idx={idx})");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "false" });
            }

            // Exponer item actual
            string itemVar = "item";
            if (p.TryGetValue("itemVar", out var itemVarObj) && !string.IsNullOrWhiteSpace(Convert.ToString(itemVarObj)))
                itemVar = Convert.ToString(itemVarObj);

            var actual = list[idx];
            ctx.Estado[itemVar] = actual;
            ctx.Estado[indexKey] = idx + 1;

            ctx.Log($"[Loop] idx={idx} → var '{itemVar}' seteada");
            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "true" });
        }

        private static IList<object> ObtenerItemsIniciales(ContextoEjecucion ctx, Dictionary<string, object> p)
        {
            if (p.TryGetValue("forEach", out var fe))
            {
                // "${algo.ruta}" o arreglo
                if (fe is string s)
                {
                    var trimmed = s.Trim();
                    if (trimmed.StartsWith("${") && trimmed.EndsWith("}"))
                    {
                        var path = trimmed.Substring(2, trimmed.Length - 3);
                        var resolved = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                        return NormalizarALista(resolved);
                    }
                    // string literal → un solo item
                    return new List<object> { s };
                }
                if (fe is Newtonsoft.Json.Linq.JArray ja) return ja.ToObject<List<object>>();
                if (fe is IEnumerable enumerable) return enumerable.Cast<object>().ToList();
            }
            // default: lista vacía
            return new List<object>();
        }

        private static IList<object> NormalizarALista(object obj)
        {
            if (obj == null) return new List<object>();
            if (obj is IList<object> lo) return lo;
            if (obj is Newtonsoft.Json.Linq.JArray ja) return ja.ToObject<List<object>>();
            if (obj is IEnumerable enumerable) return enumerable.Cast<object>().ToList();
            return new List<object> { obj };
        }
    }

}