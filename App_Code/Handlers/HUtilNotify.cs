using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo util.notify.
    ///
    /// FIX17:
    /// - La notificación deja de ser solamente un log.
    /// - Si existe dbo.WF_Notificacion, crea una notificación operativa persistente.
    /// - La notificación puede apuntar a usuario, rol o quedar como aviso del usuario ejecutor.
    /// - No envía correo real. Para correo real usar email.send.
    ///
    /// Compatibilidad de parámetros:
    /// - Formato actual: tipo, canal, destinoTipo, usuarioDestino, rolDestino, destino, asunto, mensaje, prioridad, urlAccion.
    /// - Formato anterior: canal, nivel, titulo/title, mensaje/message.
    /// </summary>
    public class HUtilNotify : IManejadorNodo
    {
        public string TipoNodo => "util.notify";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = (IDictionary<string, object>)(nodo.Parameters
                      ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

            string tipo = (GetString(p, "tipo") ?? "sistema").Trim();
            string canal = (GetString(p, "canal") ?? tipo ?? "sistema").Trim();
            string canalSolicitado = canal;
            string nivel = (GetString(p, "nivel") ?? "info").Trim();
            string prioridad = (GetString(p, "prioridad") ?? "normal").Trim();
            string destinoTipo = (GetString(p, "destinoTipo") ?? string.Empty).Trim().ToLowerInvariant();

            string usuarioDestinoTpl = GetString(p, "usuarioDestino") ?? string.Empty;
            string rolDestinoTpl = GetString(p, "rolDestino") ?? string.Empty;
            string destinoTpl = GetString(p, "destino") ?? string.Empty;
            string urlAccionTpl = GetString(p, "urlAccion") ?? string.Empty;

            string tituloTpl =
                GetString(p, "asunto") ??
                GetString(p, "titulo") ??
                GetString(p, "title") ??
                "Notificación";

            string mensajeTpl =
                GetString(p, "mensaje") ??
                GetString(p, "message") ??
                string.Empty;

            string usuarioDestino = ctx.ExpandString(usuarioDestinoTpl);
            string rolDestino = ctx.ExpandString(rolDestinoTpl);
            string destino = ctx.ExpandString(destinoTpl);
            string titulo = ctx.ExpandString(tituloTpl);
            string mensaje = ctx.ExpandString(mensajeTpl);
            string urlAccion = ctx.ExpandString(urlAccionTpl);

            if (string.IsNullOrWhiteSpace(tipo)) tipo = "sistema";
            if (string.IsNullOrWhiteSpace(canal)) canal = "sistema";
            if (string.IsNullOrWhiteSpace(nivel)) nivel = "info";
            if (string.IsNullOrWhiteSpace(prioridad)) prioridad = "normal";

            bool canalExternoNoImplementado = EsCanalExternoNoImplementado(canal);
            if (canalExternoNoImplementado)
            {
                canal = "sistema";
                tipo = "sistema";
            }

            ResolverDestino(destinoTipo, destino, ref usuarioDestino, ref rolDestino);

            string usuarioActual = ObtenerUsuarioActual();

            // Si no indicaron destinatario, la notificación queda dirigida al usuario ejecutor.
            // Si destinoTipo=sistema, queda como aviso general visible para usuarios autenticados.
            if (string.IsNullOrWhiteSpace(usuarioDestino) && string.IsNullOrWhiteSpace(rolDestino)
                && !string.Equals(destinoTipo, "sistema", StringComparison.OrdinalIgnoreCase))
            {
                usuarioDestino = usuarioActual;
            }

            long instanciaId = TryGetEstadoLong(ctx, "wf.instanceId", 0);
            int definicionId = TryGetEstadoInt(ctx, "wf.definicionId", 0);

            if (string.IsNullOrWhiteSpace(urlAccion) && instanciaId > 0)
            {
                if (definicionId > 0)
                    urlAccion = "WF_Instancias.aspx?defId=" + definicionId + "&inst=" + instanciaId;
                else
                    urlAccion = "WF_Instancias.aspx?inst=" + instanciaId;
            }

            string avisoCanal = canalExternoNoImplementado
                ? " [canal solicitado '" + canalSolicitado + "' no envía mensajes reales; para correo usar email.send]"
                : "";

            string destinoLog = BuildDestinoLog(usuarioDestino, rolDestino, destino);

            long notificacionId = 0;
            bool persistida = false;
            string persistError = null;

            try
            {
                if (ExisteTablaNotificacion())
                {
                    notificacionId = await InsertarNotificacionAsync(
                        instanciaId,
                        definicionId,
                        nodo.Id,
                        nodo.Type,
                        tipo,
                        canal,
                        prioridad,
                        titulo,
                        mensaje,
                        usuarioDestino,
                        rolDestino,
                        destino,
                        urlAccion,
                        usuarioActual,
                        BuildDatosJson(p),
                        ct);

                    persistida = true;
                }
                else
                {
                    persistError = "No existe dbo.WF_Notificacion. Ejecutar Sql/FIX17_WF_Notificacion.sql.";
                }
            }
            catch (Exception ex)
            {
                persistError = ex.GetType().Name + ": " + ex.Message;
            }

            string texto;
            if (persistida)
            {
                texto = $"[notify/db/{nivel.ToLowerInvariant()}] #{notificacionId}{avisoCanal}{destinoLog} {titulo}: {mensaje}";
            }
            else
            {
                texto = $"[notify/log/{nivel.ToLowerInvariant()}]{avisoCanal}{destinoLog} {titulo}: {mensaje}";
                if (!string.IsNullOrWhiteSpace(persistError))
                    texto += " [no persistida: " + persistError + "]";
            }

            ctx.Log(texto);

            if (ctx.Estado != null)
            {
                ctx.Estado["notify.last.id"] = notificacionId;
                ctx.Estado["notify.last.persisted"] = persistida;
                ctx.Estado["notify.last.error"] = persistError ?? string.Empty;
                ctx.Estado["notify.last.type"] = tipo;
                ctx.Estado["notify.last.canal"] = canal;
                ctx.Estado["notify.last.requestedCanal"] = canalSolicitado;
                ctx.Estado["notify.last.nivel"] = nivel;
                ctx.Estado["notify.last.prioridad"] = prioridad;
                ctx.Estado["notify.last.destino"] = destino;
                ctx.Estado["notify.last.usuarioDestino"] = usuarioDestino ?? string.Empty;
                ctx.Estado["notify.last.rolDestino"] = rolDestino ?? string.Empty;
                ctx.Estado["notify.last.title"] = titulo;
                ctx.Estado["notify.last.message"] = mensaje;
                ctx.Estado["notify.last.urlAccion"] = urlAccion ?? string.Empty;
                ctx.Estado["wf.currentNodeId"] = nodo.Id;
                ctx.Estado["wf.currentNodeType"] = nodo.Type;
            }

            return new ResultadoEjecucion
            {
                Etiqueta = "always"
            };
        }

        private static void ResolverDestino(string destinoTipo, string destino, ref string usuarioDestino, ref string rolDestino)
        {
            if (!string.IsNullOrWhiteSpace(usuarioDestino) || !string.IsNullOrWhiteSpace(rolDestino))
                return;

            if (string.IsNullOrWhiteSpace(destino))
                return;

            if (destinoTipo == "usuario")
            {
                usuarioDestino = destino.Trim();
                return;
            }

            if (destinoTipo == "rol")
            {
                rolDestino = destino.Trim();
                return;
            }

            // Heurística conservadora para compatibilidad con params viejos.
            // OMARD\USUARIO o texto con @ => usuario. El resto queda como rol/referencia operativa.
            if (destino.Contains("\\") || destino.Contains("@"))
                usuarioDestino = destino.Trim();
            else
                rolDestino = destino.Trim();
        }

        private static string BuildDestinoLog(string usuarioDestino, string rolDestino, string destino)
        {
            if (!string.IsNullOrWhiteSpace(usuarioDestino))
                return " usuario=" + usuarioDestino;

            if (!string.IsNullOrWhiteSpace(rolDestino))
                return " rol=" + rolDestino;

            if (!string.IsNullOrWhiteSpace(destino))
                return " destino=" + destino;

            return string.Empty;
        }

        private static bool EsCanalExternoNoImplementado(string canal)
        {
            if (string.IsNullOrWhiteSpace(canal)) return false;

            return canal.Equals("email", StringComparison.OrdinalIgnoreCase)
                || canal.Equals("mail", StringComparison.OrdinalIgnoreCase)
                || canal.Equals("correo", StringComparison.OrdinalIgnoreCase)
                || canal.Equals("sms", StringComparison.OrdinalIgnoreCase)
                || canal.Equals("webhook", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ExisteTablaNotificacion()
        {
            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand("SELECT OBJECT_ID('dbo.WF_Notificacion', 'U');", cn))
            {
                cn.Open();
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

        private static async Task<long> InsertarNotificacionAsync(
            long instanciaId,
            int definicionId,
            string nodoId,
            string nodoTipo,
            string tipo,
            string canal,
            string prioridad,
            string titulo,
            string mensaje,
            string usuarioDestino,
            string rolDestino,
            string destino,
            string urlAccion,
            string creadoPor,
            string datosJson,
            CancellationToken ct)
        {
            var cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Notificacion
    (WF_InstanciaId, WF_DefinicionId, NodoId, NodoTipo,
     Tipo, Canal, Prioridad,
     Titulo, Mensaje,
     UsuarioDestino, RolDestino, Destino,
     UrlAccion,
     Leido, FechaLeido, LeidoPor,
     CreadoPor, DatosJson)
VALUES
    (@InstanciaId, @DefinicionId, @NodoId, @NodoTipo,
     @Tipo, @Canal, @Prioridad,
     @Titulo, @Mensaje,
     @UsuarioDestino, @RolDestino, @Destino,
     @UrlAccion,
     0, NULL, NULL,
     @CreadoPor, @DatosJson);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId > 0 ? (object)instanciaId : DBNull.Value;
                cmd.Parameters.Add("@DefinicionId", SqlDbType.Int).Value = definicionId > 0 ? (object)definicionId : DBNull.Value;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = (object)nodoId ?? DBNull.Value;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = (object)nodoTipo ?? DBNull.Value;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = tipo ?? "sistema";
                cmd.Parameters.Add("@Canal", SqlDbType.NVarChar, 30).Value = canal ?? "sistema";
                cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 20).Value = prioridad ?? "normal";
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = titulo ?? "Notificación";
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar).Value = (object)mensaje ?? DBNull.Value;
                cmd.Parameters.Add("@UsuarioDestino", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(usuarioDestino) ? (object)DBNull.Value : usuarioDestino;
                cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(rolDestino) ? (object)DBNull.Value : rolDestino;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(destino) ? (object)DBNull.Value : destino;
                cmd.Parameters.Add("@UrlAccion", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(urlAccion) ? (object)DBNull.Value : urlAccion;
                cmd.Parameters.Add("@CreadoPor", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(creadoPor) ? (object)DBNull.Value : creadoPor;
                cmd.Parameters.Add("@DatosJson", SqlDbType.NVarChar).Value = string.IsNullOrWhiteSpace(datosJson) ? (object)DBNull.Value : datosJson;

                await cn.OpenAsync(ct);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                return Convert.ToInt64(scalar);
            }
        }

        private static string BuildDatosJson(IDictionary<string, object> p)
        {
            try
            {
                var js = new JavaScriptSerializer();
                var clean = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                if (p != null)
                {
                    foreach (var kv in p)
                    {
                        // position pertenece al canvas/editor, no a la notificación operativa.
                        if (string.Equals(kv.Key, "position", StringComparison.OrdinalIgnoreCase))
                            continue;

                        clean[kv.Key] = kv.Value;
                    }
                }

                return js.Serialize(clean);
            }
            catch
            {
                return null;
            }
        }

        private static string ObtenerUsuarioActual()
        {
            try
            {
                var identity = HttpContext.Current?.User?.Identity;
                if (identity != null && identity.IsAuthenticated)
                    return (identity.Name ?? string.Empty).Trim();
            }
            catch { }

            return string.Empty;
        }

        private static long TryGetEstadoLong(ContextoEjecucion ctx, string key, long def)
        {
            try
            {
                object v;
                if (ctx != null && ctx.Estado != null && ctx.Estado.TryGetValue(key, out v) && v != null)
                {
                    long n;
                    if (long.TryParse(Convert.ToString(v), out n)) return n;
                }
            }
            catch { }

            return def;
        }

        private static int TryGetEstadoInt(ContextoEjecucion ctx, string key, int def)
        {
            try
            {
                object v;
                if (ctx != null && ctx.Estado != null && ctx.Estado.TryGetValue(key, out v) && v != null)
                {
                    int n;
                    if (int.TryParse(Convert.ToString(v), out n)) return n;
                }
            }
            catch { }

            return def;
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p == null) return null;
            object v;
            if (!p.TryGetValue(key, out v) || v == null) return null;
            return Convert.ToString(v);
        }
    }
}
