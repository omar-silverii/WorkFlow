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
    /// Handler para el nodo "file.read".
    /// Lee un archivo del disco del servidor y lo deja en ctx.Estado[salida].
    /// Parámetros esperados (nodo.Parameters):
    ///   - path     : ruta del archivo (puede contener ${...})
    ///   - encoding : nombre de encoding (ej: "utf-8", "latin1") (opcional, default utf-8)
    ///   - salida   : nombre de la clave en el contexto donde guardar el contenido (opcional, default "archivo")
    ///   - asJson   : "true" para parsear JSON y guardar objeto en ctx.Estado[salida] (opcional, default false)
    ///   - zipMode  : "auto" (default), "none", "zip", "gzip"
    ///   - zipEntry : nombre de la entrada dentro del ZIP (opcional; si no se indica, toma la primera)
    ///   - useCache : "true" (default) para evitar releer en resume si ctx.Estado[salida] ya existe
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

            ct.ThrowIfCancellationRequested();

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string pathTpl = GetString(p, "path");
            string encodingName = GetString(p, "encoding") ?? "utf-8";
            string salida = GetString(p, "salida") ?? "archivo";

            // NUEVO: parsear JSON
            bool asJson = false;
            var asJsonRaw = GetString(p, "asJson");
            if (!string.IsNullOrWhiteSpace(asJsonRaw) && bool.TryParse(asJsonRaw, out var bAsJson))
                asJson = bAsJson;

            // NUEVO: cache (default true)
            bool useCache = true;
            var useCacheRaw = GetString(p, "useCache");
            if (!string.IsNullOrWhiteSpace(useCacheRaw) && bool.TryParse(useCacheRaw, out var bCache))
                useCache = bCache;

            // NUEVO: modo de compresión y entrada de ZIP
            string zipModeRaw = GetString(p, "zipMode") ?? "auto";
            string zipMode = zipModeRaw.Trim().ToLowerInvariant();
            string zipEntryName = GetString(p, "zipEntry");

            if (string.IsNullOrWhiteSpace(pathTpl))
            {
                string msg = "Nodo file.read: falta parámetro 'path'.";
                ctx.Log("[file.read/error] " + msg);
                ctx.Estado["file.read.lastError"] = msg;
                ctx.Estado["wf.error"] = true;
                ctx.Estado["wf.error.message"] = msg;
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            // ==== cache en resume ====
            bool isResume =
                ctx.Estado != null &&
                ctx.Estado.TryGetValue("wf.startNodeIdOverride", out var ov) &&
                ov != null;

            if (useCache && isResume && ctx.Estado != null && ctx.Estado.ContainsKey(salida))
            {
                var cached = ctx.Estado[salida];
                int len = (cached is string s) ? s.Length : 0;
                ctx.Log($"[file.read] cache hit: '{salida}' ya existe en contexto (len={len}). Se omite lectura.");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            // =========================

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
                ctx.Estado["wf.error.message"] = msg;
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(path))
                {
                    string msg = $"Archivo no encontrado: {path}";
                    ctx.Log("[file.read/error] " + msg);
                    ctx.Estado["file.read.lastError"] = msg;
                    ctx.Estado["file.read.exists"] = false;
                    ctx.Estado["wf.error"] = true;
                    ctx.Estado["wf.error.message"] = msg;
                    return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                }

                Encoding enc;
                try { enc = Encoding.GetEncoding(encodingName); }
                catch
                {
                    enc = Encoding.UTF8;
                    ctx.Log($"[file.read] encoding desconocido '{encodingName}', usando UTF-8.");
                }

                ctx.Log($"[file.read] leyendo bytes de '{path}' (zipMode={zipModeRaw}).");

                byte[] rawBytes = File.ReadAllBytes(path);
                byte[] dataBytes = rawBytes;
                string usedCompression = "none";

                if (zipMode == "zip" || (zipMode == "auto" && LooksLikeZip(rawBytes)))
                {
                    usedCompression = "zip";
                    dataBytes = ReadZipEntry(rawBytes, zipEntryName, ctx);
                }
                else if (zipMode == "gzip" || (zipMode == "auto" && LooksLikeGzip(rawBytes)))
                {
                    usedCompression = "gzip";
                    dataBytes = ReadGzip(rawBytes, ctx);
                }
                else
                {
                    ctx.Log("[file.read] Tratando archivo como texto plano (sin compresión).");
                }

                string contenido = enc.GetString(dataBytes);

                object valor = contenido;

                // ==== NUEVO: si asJson=true, parsear y guardar objeto ====
                if (asJson)
                {
                    try
                    {
                        // Puede ser objeto, array, string, número, etc.
                        //valor = JsonConvert.DeserializeObject(contenido);
                        var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                        valor = ser.DeserializeObject(contenido);
                        ctx.Log($"[file.read] JSON parseado OK en salida='{salida}'.");
                    }
                    catch (Exception jex)
                    {
                        string msg = $"file.read: asJson=true pero JSON inválido. {jex.Message}";
                        ctx.Log("[file.read/error] " + msg);
                        ctx.Estado["file.read.lastError"] = msg;
                        ctx.Estado["wf.error"] = true;
                        ctx.Estado["wf.error.message"] = msg;
                        return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
                    }
                }

                ctx.Estado[salida] = valor;
                ctx.Estado["file.read.lastPath"] = path;
                ctx.Estado["file.read.lastEncoding"] = enc.WebName;
                ctx.Estado["file.read.lastLength"] = contenido?.Length ?? 0;
                ctx.Estado["file.read.lastZipMode"] = usedCompression;
                ctx.Estado["file.read.lastAsJson"] = asJson;

                ctx.Log($"[file.read] archivo leído: {path} (caracteres={contenido?.Length ?? 0}, compresión={usedCompression}, asJson={asJson}).");

                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }
            catch (Exception ex)
            {
                string msg = $"Excepción en file.read: {ex.Message}";
                ctx.Log("[file.read/error] " + msg);
                ctx.Estado["file.read.lastError"] = msg;
                ctx.Estado["wf.error"] = true;
                ctx.Estado["wf.error.message"] = msg;
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "error" });
            }
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool LooksLikeZip(byte[] bytes)
        {
            return bytes != null &&
                   bytes.Length >= 4 &&
                   bytes[0] == 0x50 &&
                   bytes[1] == 0x4B;
        }

        private static bool LooksLikeGzip(byte[] bytes)
        {
            return bytes != null &&
                   bytes.Length >= 2 &&
                   bytes[0] == 0x1F &&
                   bytes[1] == 0x8B;
        }

        private static byte[] ReadZipEntry(byte[] zipBytes, string entryName, ContextoEjecucion ctx)
        {
            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
            {
                if (zip.Entries.Count == 0)
                    throw new InvalidOperationException("file.read: el ZIP no contiene entradas.");

                ZipArchiveEntry entry;

                if (!string.IsNullOrWhiteSpace(entryName))
                {
                    entry = zip.GetEntry(entryName);
                    if (entry == null)
                        throw new InvalidOperationException($"file.read: la entrada '{entryName}' no existe en el ZIP.");
                }
                else
                {
                    entry = zip.Entries[0];
                }

                ctx.Log($"[file.read] Leyendo entrada ZIP '{entry.FullName}' ({entry.Length} bytes).");

                using (var es = entry.Open())
                using (var outMs = new MemoryStream())
                {
                    es.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
        }


        private static byte[] ReadGzip(byte[] gzBytes, ContextoEjecucion ctx)
        {
            using (var ms = new MemoryStream(gzBytes))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gz.CopyTo(outMs);
                ctx.Log($"[file.read] GZIP descomprimido a {outMs.Length} bytes.");
                return outMs.ToArray();
            }
        }
    }
}
