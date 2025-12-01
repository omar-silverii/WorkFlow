using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler para "ftp.put".
    /// Sube un archivo local a un servidor FTP.
    /// Parámetros esperados (node.Parameters):
    ///   - host        (string, ej: "ftp.miempresa.com")
    ///   - user        (string)
    ///   - password    (string)
    ///   - localPath   (string, ruta completa del archivo local)
    ///   - remotePath  (string, ruta remota, ej: "/out/archivo.csv")
    ///   - passive     (bool, opcional, default = true)
    ///   - ssl         (bool, opcional, default = false)
    ///   - overwrite   (bool, opcional, default = true)
    /// 
    /// Loguea siempre y devuelve:
    ///   Etiqueta = "always" si pudo subir;
    ///   Etiqueta = "error" si hubo problema.
    /// </summary>
    public class HFtpPut : IManejadorNodo
    {
        public string TipoNodo => "ftp.put";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            string host = GetString(p, "host");
            string user = GetString(p, "user");
            string password = GetString(p, "password");
            string localPath = GetString(p, "localPath");
            string remotePath = GetString(p, "remotePath");

            bool passive = GetBool(p, "passive", defaultValue: true);
            bool useSsl = GetBool(p, "ssl", defaultValue: false);
            bool overwrite = GetBool(p, "overwrite", defaultValue: true);

            // ===== validaciones mínimas =====
            if (string.IsNullOrWhiteSpace(host))
            {
                ctx.Log("[ftp.put/error] Falta parámetro 'host'.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            if (string.IsNullOrWhiteSpace(localPath))
            {
                ctx.Log("[ftp.put/error] Falta parámetro 'localPath'.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            if (!File.Exists(localPath))
            {
                ctx.Log($"[ftp.put/error] Archivo local no encontrado: {localPath}");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }

            if (string.IsNullOrWhiteSpace(remotePath))
            {
                // si no viene remotePath, usamos el nombre del archivo local
                remotePath = "/" + Path.GetFileName(localPath);
            }

            if (!remotePath.StartsWith("/"))
                remotePath = "/" + remotePath;

            string scheme = useSsl ? "ftps" : "ftp";
            var uri = new Uri(string.Format("{0}://{1}{2}", scheme, host, remotePath));

            ctx.Log(string.Format(
                "[ftp.put] Subiendo archivo: {0} → {1} (usuario={2})",
                localPath,
                uri,
                string.IsNullOrEmpty(user) ? "(anónimo)" : user
            ));

            try
            {
                var request = (FtpWebRequest)WebRequest.Create(uri);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.UsePassive = passive;
                request.EnableSsl = useSsl;
                request.KeepAlive = false;

                //request.Credentials = string.IsNullOrEmpty(user)
                //    ? CredentialCache.DefaultNetworkCredentials
                //    : new NetworkCredential(user, password ?? string.Empty);

                if (!string.IsNullOrEmpty(user))
                {
                    // Login explícito
                    request.Credentials = new NetworkCredential(user, password ?? "");
                }
                // Si user está vacío, NO seteamos credenciales:
                // - el servidor puede aceptar anonymous
                // - o rechazar (y veremos un 530, que ya es un error “real” del FTP)



                // ===== CARGA DEL ARCHIVO (SÍNCRONO, COMPATIBLE .NET 4.x) =====
                byte[] buffer = File.ReadAllBytes(localPath);
                request.ContentLength = buffer.Length;

                using (var reqStream = await request.GetRequestStreamAsync())
                {
                    await reqStream.WriteAsync(buffer, 0, buffer.Length, ct);
                }

                using (var resp = (FtpWebResponse)await request.GetResponseAsync())
                {
                    ctx.Log(string.Format(
                        "[ftp.put] Respuesta FTP: {0} - {1}",
                        resp.StatusCode,
                        (resp.StatusDescription ?? string.Empty).Trim()
                    ));
                }

                ctx.Estado["ftp.lastPut"] = new
                {
                    host,
                    remotePath,
                    localPath,
                    fecha = DateTime.Now
                };

                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (OperationCanceledException)
            {
                ctx.Log("[ftp.put/error] Operación cancelada por CancellationToken.");
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
            catch (Exception ex)
            {
                ctx.Log(string.Format(
                    "[ftp.put/error] {0}: {1}",
                    ex.GetType().Name,
                    ex.Message
                ));

                ctx.Estado["ftp.lastError"] = new
                {
                    host,
                    remotePath,
                    localPath,
                    tipo = ex.GetType().FullName,
                    ex.Message,
                    fecha = DateTime.Now
                };

                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        // ===== Helpers =====

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool GetBool(IDictionary<string, object> p, string key, bool defaultValue)
        {
            if (p == null || !p.TryGetValue(key, out var v) || v == null)
                return defaultValue;

            if (v is bool b) return b;
            bool b2;
            if (bool.TryParse(Convert.ToString(v), out b2)) return b2;
            int i;
            if (int.TryParse(Convert.ToString(v), out i)) return i != 0;
            return defaultValue;
        }
    }
}
