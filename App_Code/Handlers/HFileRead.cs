using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para el nodo "file.read".
    /// Lee un archivo del disco del servidor y lo deja en ctx.Estado[salida].
    /// Parámetros esperados (nodo.Parameters):
    ///   - path    : ruta del archivo (puede contener ${...})
    ///   - encoding: nombre de encoding (ej: "utf-8", "latin1") (opcional, default utf-8)
    ///   - salida  : nombre de la clave en el contexto donde guardar el contenido (opcional, default "archivo")
    ///
    /// En caso de error serio:
    ///   - loguea [file.read/error] ...
    ///   - setea ctx.Estado["file.read.lastError"]
    ///   - setea ctx.Estado["wf.error"] = true
    ///   - retorna Etiqueta = "error" (para enganchar con util.error).
    /// </summary>
    public class HFileRead : IManejadorNodo
    {
        public string TipoNodo => "file.read";

        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string pathTpl = GetString(p, "path");
            string encodingName = GetString(p, "encoding") ?? "utf-8";
            string salida = GetString(p, "salida") ?? "archivo";

            if (string.IsNullOrWhiteSpace(pathTpl))
            {
                string msg = "Nodo file.read: falta parámetro 'path'.";
                ctx.Log("[file.read/error] " + msg);
                ctx.Estado["file.read.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            // Expandir variables ${...} en el path
            string path;
            try
            {
                path = ctx.ExpandString(pathTpl) ?? pathTpl;
            }
            catch (Exception ex)
            {
                string msg = $"Error al expandir path en file.read: {ex.Message}";
                ctx.Log("[file.read/error] " + msg);
                ctx.Estado["file.read.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            try
            {
                if (!File.Exists(path))
                {
                    string msg = $"Archivo no encontrado: {path}";
                    ctx.Log("[file.read/error] " + msg);
                    ctx.Estado["file.read.lastError"] = msg;
                    ctx.Estado["file.read.exists"] = false;
                    ctx.Estado["wf.error"] = true;

                    return Task.FromResult(new ResultadoEjecucion
                    {
                        Etiqueta = "error"
                    });
                }

                Encoding enc;
                try
                {
                    enc = Encoding.GetEncoding(encodingName);
                }
                catch
                {
                    enc = Encoding.UTF8;
                    ctx.Log($"[file.read] encoding desconocido '{encodingName}', usando UTF-8.");
                }

                string contenido = File.ReadAllText(path, enc);

                ctx.Estado[salida] = contenido;
                ctx.Estado["file.read.lastPath"] = path;
                ctx.Estado["file.read.lastEncoding"] = enc.WebName;
                ctx.Estado["file.read.lastLength"] = contenido?.Length ?? 0;

                ctx.Log($"[file.read] archivo leído: {path} (caracteres={contenido?.Length ?? 0}).");

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }
            catch (Exception ex)
            {
                string msg = $"Excepción en file.read: {ex.Message}";
                ctx.Log("[file.read/error] " + msg);
                ctx.Estado["file.read.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }
        }

        // === Helpers internos (igual estilo que HUtilError) ===

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }
    }
}
