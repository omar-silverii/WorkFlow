using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// config.secrets
    /// Lee secretos/config desde web.config (AppSettings / ConnectionStrings) o Environment.
    ///
    /// Parameters:
    ///   - source: "appSettings" | "connectionStrings" | "env" (default "appSettings")
    ///   - key:    string (required)
    ///   - output: string path donde guardar (default "secrets.<key>")
    ///   - required: bool (default true) si no existe => error
    ///   - defaultValue: string (opcional) si required=false y no existe
    ///   - trim: bool (default true)
    ///   - maskInfo: bool (default true) guarda output+".masked" y output+".len" (NO loguea el valor real)
    ///
    /// Salidas:
    ///   - always
    ///   - error
    /// </summary>
    public class HConfigSecrets : IManejadorNodo
    {
        public string TipoNodo => "config.secrets";

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            try
            {
                ct.ThrowIfCancellationRequested();

                var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                var source = LeerString(p, "source");
                if (string.IsNullOrWhiteSpace(source)) source = "appSettings";

                var key = LeerString(p, "key");
                if (string.IsNullOrWhiteSpace(key))
                    return SetError(ctx, "config.secrets: falta Parameters.key");

                var output = LeerString(p, "output");
                if (string.IsNullOrWhiteSpace(output))
                    output = "secrets." + key;

                var required = LeerBool(p, "required", defaultValue: true);
                var defaultValue = LeerString(p, "defaultValue");
                var trim = LeerBool(p, "trim", defaultValue: true);
                var maskInfo = LeerBool(p, "maskInfo", defaultValue: true);

                string value = null;
                bool found = false;

                if (source.Equals("appSettings", StringComparison.OrdinalIgnoreCase))
                {
                    value = ConfigurationManager.AppSettings[key];
                    found = (value != null);
                }
                else if (source.Equals("connectionStrings", StringComparison.OrdinalIgnoreCase))
                {
                    var cs = ConfigurationManager.ConnectionStrings[key];
                    value = cs?.ConnectionString;
                    found = (value != null);
                }
                else if (source.Equals("env", StringComparison.OrdinalIgnoreCase))
                {
                    value = Environment.GetEnvironmentVariable(key);
                    found = (value != null);
                }
                else
                {
                    return SetError(ctx, "config.secrets: source inválido (use appSettings/connectionStrings/env)");
                }

                if (!found)
                {
                    if (required)
                        return SetError(ctx, $"config.secrets: no existe key='{key}' en source='{source}'");

                    value = defaultValue; // puede ser null
                    ctx.Log($"[config.secrets] key='{key}' no encontrada (source='{source}'). required=false => defaultValue {(value == null ? "NULL" : "SET")}.");
                }
                else
                {
                    ctx.Log($"[config.secrets] OK key='{key}' source='{source}' output='{output}' (valor NO logueado).");
                }

                if (value != null && trim)
                    value = value.Trim();

                // Guardar el secreto en el output
                ContextoEjecucion.SetPath(ctx.Estado, output, value);

                // Info segura para demo/diagnóstico (sin exponer)
                if (maskInfo)
                {
                    ContextoEjecucion.SetPath(ctx.Estado, output + ".len", value == null ? 0 : value.Length);
                    ContextoEjecucion.SetPath(ctx.Estado, output + ".masked", Mask(value));
                }

                // Mantengo async coherente (aunque no await-eemos nada acá)
                await Task.Yield();

                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (OperationCanceledException)
            {
                return SetError(ctx, "config.secrets: cancelado");
            }
            catch (Exception ex)
            {
                return SetError(ctx, "config.secrets: " + ex.Message);
            }
        }

        // ===================== helpers =====================

        private static ResultadoEjecucion SetError(ContextoEjecucion ctx, string msg)
        {
            try
            {
                ContextoEjecucion.SetPath(ctx.Estado, "wf.error", true);
                ContextoEjecucion.SetPath(ctx.Estado, "wf.error.message", msg);
            }
            catch { }

            ctx.Log("[config.secrets] ERROR " + msg);
            return new ResultadoEjecucion { Etiqueta = "error" };
        }

        private static string Mask(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Length <= 4) return new string('*', v.Length);
            return v.Substring(0, 2) + new string('*', v.Length - 4) + v.Substring(v.Length - 2, 2);
        }

        private static string LeerString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            if (!p.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool LeerBool(Dictionary<string, object> p, string key, bool defaultValue)
        {
            if (p == null) return defaultValue;
            if (!p.TryGetValue(key, out var v) || v == null) return defaultValue;

            if (v is bool b) return b;

            var s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (bool.TryParse(s, out var bb)) return bb;
            if (int.TryParse(s, out var i)) return i != 0;

            return defaultValue;
        }
    }
}
