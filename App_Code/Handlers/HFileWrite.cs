using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json; // <-- NUEVO

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para el nodo "file.write".
    /// Toma un valor del contexto y lo escribe a un archivo en disco.
    ///
    /// Parámetros esperados (nodo.Parameters):
    ///   - path       : ruta del archivo (puede contener ${...})      (OBLIGATORIO)
    ///   - encoding   : nombre de encoding (ej: "utf-8")             (opcional, default utf-8)
    ///   - overwrite  : bool, true para sobrescribir si existe       (opcional, default true)
    ///   - origen     : clave en ctx.Estado de donde tomar los datos (opcional, default "archivo")
    ///   - zipMode    : "none" (default) o "zip"
    ///   - zipEntryName / entryName : nombre de la entrada dentro del ZIP (opcional)
    ///   - content    : plantilla directa (opcional)  <<< ya existía
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

            string contentTpl = GetString(p, "content"); // plantilla directa

            string pathTpl = GetString(p, "path");
            string encodingName = GetString(p, "encoding") ?? "utf-8";
            bool overwrite = GetBool(p, "overwrite", defaultValue: true);
            string origen = GetString(p, "origen") ?? "archivo";

            string zipModeRaw =
                GetString(p, "zipMode") ??
                GetString(p, "zip") ??
                "none";
            string zipMode = zipModeRaw.Trim().ToLowerInvariant();

            string entryNameTpl =
                GetString(p, "zipEntryName") ??
                GetString(p, "entryName");

            if (string.IsNullOrWhiteSpace(pathTpl))
            {
                string msg = "Nodo file.write: falta parámetro 'path'.";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            if (string.IsNullOrWhiteSpace(contentTpl) && string.IsNullOrWhiteSpace(origen))
            {
                string msg = "Nodo file.write: falta parámetro 'origen' (y no vino 'content').";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

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

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            object data = null;

            if (!string.IsNullOrWhiteSpace(contentTpl))
            {
                try
                {
                    data = ctx.ExpandString(contentTpl);
                }
                catch (Exception ex)
                {
                    string msg = $"Nodo file.write: error al expandir 'content': {ex.Message}";
                    ctx.Log("[file.write/error] " + msg);
                    ctx.Estado["file.write.lastError"] = msg;
                    ctx.Estado["wf.error"] = true;

                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }
            }
            else
            {
                if (!ctx.Estado.TryGetValue(origen, out data) || data == null)
                {
                    string msg = $"Nodo file.write: no se encontró ctx.Estado[\"{origen}\"] o es null.";
                    ctx.Log("[file.write/error] " + msg);
                    ctx.Estado["file.write.lastError"] = msg;
                    ctx.Estado["wf.error"] = true;

                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }
            }

            try
            {
                if (File.Exists(path) && !overwrite)
                {
                    ctx.Log($"[file.write] archivo ya existe y overwrite=false, se omite escritura. Path={path}");
                    ctx.Estado["file.write.skipped"] = true;
                    ctx.Estado["file.write.lastPath"] = path;
                    ctx.Estado["file.write.lastZipMode"] = zipMode;
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
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

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (zipMode == "zip")
                {
                    string entryName;
                    if (!string.IsNullOrWhiteSpace(entryNameTpl))
                    {
                        try { entryName = ctx.ExpandString(entryNameTpl) ?? entryNameTpl; }
                        catch { entryName = entryNameTpl; }
                    }
                    else
                    {
                        var fileName = Path.GetFileName(path);
                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        entryName = baseName + ".dmt";
                    }

                    byte[] bytes;
                    if (data is byte[] bytesDirect)
                        bytes = bytesDirect;
                    else
                        bytes = enc.GetBytes(texto ?? string.Empty);

                    ctx.Log($"[file.write] escribiendo ZIP en '{path}' (entry='{entryName}').");

                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (var es = entry.Open())
                        {
                            es.Write(bytes, 0, bytes.Length);
                        }
                    }

                    ctx.Estado["file.write.lastPath"] = path;
                    ctx.Estado["file.write.lastEncoding"] = enc.WebName;
                    ctx.Estado["file.write.lastLength"] = texto?.Length ?? 0;
                    ctx.Estado["file.write.lastZipMode"] = "zip";
                    ctx.Estado["file.write.lastEntryName"] = entryName;

                    ctx.Log($"[file.write] archivo escrito (ZIP) correctamente: {path} (entry='{entryName}', bytes={bytes.Length}).");
                }
                else
                {
                    File.WriteAllText(path, texto, enc);

                    ctx.Log($"[file.write] archivo escrito correctamente: {path}");
                    ctx.Estado["file.write.lastPath"] = path;
                    ctx.Estado["file.write.lastEncoding"] = enc.WebName;
                    ctx.Estado["file.write.lastLength"] = texto?.Length ?? 0;
                    ctx.Estado["file.write.lastZipMode"] = "none";
                }

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                string msg = $"Excepción en file.write: {ex.Message}";
                ctx.Log("[file.write/error] " + msg);
                ctx.Estado["file.write.lastError"] = msg;
                ctx.Estado["wf.error"] = true;

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }

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

            if (data is string s) return s;

            if (data is byte[] bytes)
                return Encoding.UTF8.GetString(bytes);

            // ✅ NUEVO: si es diccionario/objeto complejo, serializar a JSON
            if (data is IDictionary<string, object> || data is System.Collections.IDictionary)
            {
                return JsonConvert.SerializeObject(data, Formatting.Indented);
            }

            return Convert.ToString(data);
        }
    }
}
