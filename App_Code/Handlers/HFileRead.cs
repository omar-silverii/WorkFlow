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

            // ✅ Compatibilidad: aceptar "output" como alias de "salida"
            string salida = GetString(p, "salida") ?? GetString(p, "output") ?? "archivo";

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
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        // (resto del archivo igual al tuyo)
        private static bool LooksLikeZip(byte[] raw)
        {
            if (raw == null || raw.Length < 4) return false;
            return raw[0] == 0x50 && raw[1] == 0x4B && (raw[2] == 0x03 || raw[2] == 0x05 || raw[2] == 0x07) && (raw[3] == 0x04 || raw[3] == 0x06 || raw[3] == 0x08);
        }

        private static bool LooksLikeGzip(byte[] raw)
        {
            if (raw == null || raw.Length < 2) return false;
            return raw[0] == 0x1F && raw[1] == 0x8B;
        }

        private static byte[] ReadZipEntry(byte[] zipBytes, string entryName, ContextoEjecucion ctx)
        {
            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
            {
                ZipArchiveEntry entry = null;

                if (!string.IsNullOrWhiteSpace(entryName))
                    entry = zip.GetEntry(entryName);

                if (entry == null && zip.Entries.Count > 0)
                    entry = zip.Entries[0];

                if (entry == null)
                    throw new InvalidOperationException("ZIP sin entradas.");

                ctx.Log($"[file.read] ZIP entry='{entry.FullName}' size={entry.Length}.");

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
                ctx.Log($"[file.read] GZIP decompressed bytes={outMs.Length}.");
                return outMs.ToArray();
            }
        }
    }
}
