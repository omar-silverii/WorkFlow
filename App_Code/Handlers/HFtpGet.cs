using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "ftp.get".
    /// Descarga un archivo desde un servidor FTP a una ruta local.
    ///
    /// Parámetros:
    ///   - host        (string, ej: "ftp.miempresa.com")
    ///   - user        (string)
    ///   - password    (string)
    ///   - remotePath  (string, ej: "/in/archivo.csv")
    ///   - localPath   (string, ruta completa destino)
    ///   - passive     (bool, default = true)
    ///   - ssl         (bool, default = false)
    ///   - overwrite   (bool, default = true)
    ///
    /// Estado:
    ///   - ftp.get.localPath (string)
    ///   - ftp.get.bytes     (int)
    /// </summary>
    public class HFtpGet : IManejadorNodo
    {
        public string TipoNodo => "ftp.get";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string host = GetString(p, "host");
            string user = GetString(p, "user");
            string password = GetString(p, "password");
            string remotePath = GetString(p, "remotePath");
            string localPath = GetString(p, "localPath");

            bool passive = GetBool(p, "passive", true);
            bool ssl = GetBool(p, "ssl", false);
            bool overwrite = GetBool(p, "overwrite", true);

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(remotePath) || string.IsNullOrWhiteSpace(localPath))
            {
                ctx.Log("[ftp.get] Faltan parámetros (host, remotePath, localPath).");
                ctx.Estado["ftp.get.error"] = true;
                ctx.Estado["ftp.get.error.message"] = "Parámetros incompletos (host, remotePath, localPath).";
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            try
            {
                if (File.Exists(localPath) && !overwrite)
                {
                    ctx.Log("[ftp.get] Archivo local ya existe y overwrite=false.");
                    ctx.Estado["ftp.get.error"] = true;
                    ctx.Estado["ftp.get.error.message"] = "El archivo local ya existe (overwrite=false).";
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? ".");

                string uri = BuildFtpUri(host, remotePath);
                ctx.Log($"[ftp.get] GET {uri} -> {localPath}");

                var req = (FtpWebRequest)WebRequest.Create(uri);
                req.Method = WebRequestMethods.Ftp.DownloadFile;
                req.Credentials = new NetworkCredential(user ?? "", password ?? "");
                req.UsePassive = passive;
                req.EnableSsl = ssl;
                req.UseBinary = true;
                req.KeepAlive = false;

                using (var resp = (FtpWebResponse)await req.GetResponseAsync())
                using (var rs = resp.GetResponseStream())
                using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (rs == null) throw new InvalidOperationException("FTP response stream null.");

                    await rs.CopyToAsync(fs, 81920, ct);
                }

                int bytes = 0;
                try
                {
                    var fi = new FileInfo(localPath);
                    bytes = (int)Math.Min(int.MaxValue, fi.Length);
                }
                catch { }

                ctx.Estado["ftp.get.localPath"] = localPath;
                ctx.Estado["ftp.get.bytes"] = bytes;

                ctx.Log($"[ftp.get] OK bytes={bytes}");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Log("[ftp.get] ERROR: " + ex.Message);
                ctx.Estado["ftp.get.error"] = true;
                ctx.Estado["ftp.get.error.message"] = ex.Message;
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        private static string BuildFtpUri(string host, string remotePath)
        {
            host = host.Trim();
            if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                host = host.Substring("ftp://".Length);

            remotePath = remotePath.Trim();
            if (!remotePath.StartsWith("/")) remotePath = "/" + remotePath;

            return "ftp://" + host + remotePath;
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static bool GetBool(Dictionary<string, object> p, string key, bool def)
        {
            if (p == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;
            if (v is bool b) return b;

            var s = Convert.ToString(v);
            if (string.IsNullOrWhiteSpace(s)) return def;

            if (bool.TryParse(s, out var x)) return x;

            // 1/0, yes/no
            if (s == "1") return true;
            if (s == "0") return false;
            if (s.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;

            return def;
        }
    }
}
