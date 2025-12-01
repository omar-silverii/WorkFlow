using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para el nodo "file.write".
    /// Toma un valor del contexto y lo escribe a un archivo en disco.
    ///
    /// Parámetros esperados (nodo.Parameters):
    ///   - path     : ruta del archivo (puede contener ${...})      (OBLIGATORIO)
    ///   - encoding : nombre de encoding (ej: "utf-8")             (opcional, default utf-8)
    ///   - overwrite: bool, true para sobrescribir si existe       (opcional, default true)
    ///   - origen   : clave en ctx.Estado de donde tomar los datos (opcional, default "archivo")
    ///
    /// Comportamiento:
    ///   - Si overwrite = false y el archivo existe → NO escribe, lo deja como está,
    ///     loguea "[file.write] archivo ya existe, overwrite=false" y sigue por "always".
    ///   - Si falta path/origen o no se encuentra el valor en el contexto → error.
    ///   - Si hay una excepción de IO → error.
    ///
    /// En caso de error:
    ///   - Etiqueta = "error"
    ///   - ctx.Estado["file.write.lastError"] (mensaje)
    ///   - ctx.Estado["wf.error"] = true
    /// </summary>
    public class HFileWrite : IManejadorNodo
    {
        public string TipoNodo => "file.write";

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
            bool overwrite = GetBool(p, "overwrite", defaultValue: true);
            string origen = GetString(p, "origen") ?? "archivo";

            if (string.IsNullOrWhiteSpace(pathTpl))
            {
                string msg = "Nodo file.write: falta parámetro 'path'.";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            if (string.IsNullOrWhiteSpace(origen))
            {
                string msg = "Nodo file.write: falta parámetro 'origen'.";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            // Expandir ${...} en el path
            string path;
            try
            {
                path = ctx.ExpandString(pathTpl) ?? pathTpl;
            }
            catch (Exception ex)
            {
                string msg = $"Error al expandir path en file.write: {ex.Message}";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            // Obtener los datos a escribir desde el contexto
            if (!ctx.Estado.TryGetValue(origen, out var data) || data == null)
            {
                string msg = $"Nodo file.write: no se encontró ctx.Estado[\"{origen}\"] o es null.";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }

            try
            {
                if (File.Exists(path) && !overwrite)
                {
                    ctx.Log($"[file.write] archivo ya existe y overwrite=false, se omite escritura. Path={path}");
                    ctx.Estado["file.write.skipped"] = true;
                    ctx.Estado["file.write.lastPath"] = path;
                    return Task.FromResult(new ResultadoEjecucion
                    {
                        Etiqueta = "always"
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
                    ctx.Log($"[file.write] encoding desconocido '{encodingName}', usando UTF-8.");
                }

                string texto = ConvertirAString(data);

                // Asegurar carpeta
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, texto, enc);

                ctx.Log($"[file.write] archivo escrito correctamente: {path}");
                ctx.Estado["file.write.lastPath"] = path;
                ctx.Estado["file.write.lastEncoding"] = enc.WebName;
                ctx.Estado["file.write.lastLength"] = texto?.Length ?? 0;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "always"
                });
            }
            catch (Exception ex)
            {
                string msg = $"Excepción en file.write: {ex.Message}";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion
                {
                    Etiqueta = "error"
                });
            }
        }

        // === Helpers ===

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool GetBool(IDictionary<string, object> p, string key, bool defaultValue)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (v is bool b) return b;
                if (bool.TryParse(Convert.ToString(v), out var b2)) return b2;
                if (int.TryParse(Convert.ToString(v), out var i)) return i != 0;
            }
            return defaultValue;
        }

        private static string ConvertirAString(object data)
        {
            if (data == null) return string.Empty;

            // Si ya es string, devolver directo
            if (data is string s) return s;

            // Si es un array de bytes -> lo convertimos como texto "crudo" (UTF8)
            if (data is byte[] bytes)
                return Encoding.UTF8.GetString(bytes);

            // Para todo lo demás usamos ToString()
            return Convert.ToString(data);
        }
    }
}
