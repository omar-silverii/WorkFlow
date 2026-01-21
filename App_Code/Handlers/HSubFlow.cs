// App_Code/Handlers/HSubflow.cs
using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// util.subflow: ejecuta una definición hija creando SIEMPRE una nueva WF_Instancia.
    /// Identifica el subflujo por WF_Definicion.Key (clave estable).
    ///
    /// Parámetros:
    ///   - ref: string (recomendado) => WF_Definicion.Key (ej: "DEMO.SUBFLOW")
    ///   - definicionId: int (opcional, modo técnico)
    ///   - input: object o JSON string (opcional) => DatosEntrada del subflow
    ///   - usuario: string (opcional) => CreadoPor en instancia hija (si no, usa wf.creadoPor / "app")
    ///   - maxDepth: int (opcional, default 10) => corte anti-recursión
    ///   - as: string (opcional) => alias para outputs: subflows.<alias>.*
    ///
    /// Outputs en ctx.Estado:
    ///   - subflow.instanceId
    ///   - subflow.estado   (snapshot estado del hijo, si existe)
    ///   - subflow.logs     (logs del hijo, si existe)
    ///   - subflow.childState ("Finalizado"/"EnCurso"/"Error" de WF_Instancia hija)
    ///   - subflow.ref      (ref ejecutado)
    ///
    /// Extras (si viene as):
    ///   - subflows.<as>.instanceId / childState / ref / estado / logs
    ///
    /// Etiquetas:
    ///   - always / error
    /// </summary>
    public class HSubflow : IManejadorNodo
    {
        public string TipoNodo => "util.subflow";

        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public async Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // --- 1) Resolver ref/defId ---
                int defId = GetInt(p, "definicionId", 0);

                string refKey = ctx.ExpandString(GetString(p, "ref") ?? "");
                if (defId <= 0 && !string.IsNullOrWhiteSpace(refKey))
                    defId = BuscarDefIdPorKey(refKey);

                if (defId <= 0)
                {
                    ctx.Log("[util.subflow/error] Falta 'ref' (WF_Definicion.Key) o 'definicionId'.");
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                // --- 2) Anti-loop / anti-infinito ---
                int maxDepth = GetInt(p, "maxDepth", 10);
                int currentDepth = TryGetEstadoInt(ctx, "wf.depth", 0);

                if (currentDepth >= maxDepth)
                {
                    ctx.Log($"[util.subflow/error] MaxDepth alcanzado (wf.depth={currentDepth}, maxDepth={maxDepth}).");
                    return new ResultadoEjecucion { Etiqueta = "error" };
                }

                // callStack: lista de refs ya llamados en la rama actual
                // la guardamos como string[] en estado: wf.callStack
                var stack = TryGetEstadoStringList(ctx, "wf.callStack");
                if (!string.IsNullOrWhiteSpace(refKey))
                {
                    if (stack.Any(x => string.Equals(x, refKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        ctx.Log($"[util.subflow/error] Ciclo detectado: ref '{refKey}' ya está en wf.callStack.");
                        return new ResultadoEjecucion { Etiqueta = "error" };
                    }
                }

                // --- 3) DatosEntrada del subflow ---
                string inputJson = BuildInputJson(ctx, p);

                // --- 4) Usuario ---
                string usuario = ctx.ExpandString(GetString(p, "usuario") ?? "");
                if (string.IsNullOrWhiteSpace(usuario))
                    usuario = TryGetEstadoString(ctx, "wf.creadoPor") ?? "app";

                // --- 5) Parenting: leer parent info del estado ---
                long parentInstId = TryGetEstadoLong(ctx, "wf.instanceId", 0);
                long rootInstId = TryGetEstadoLong(ctx, "wf.rootInstanceId", 0);
                if (rootInstId <= 0) rootInstId = parentInstId; // si no estaba seteado, root=padre

                int childDepth = currentDepth + 1;

                // --- 6) Crear instancia hija con metadata + ejecutar ---
                ctx.Log($"[util.subflow] Ejecutando subflow ref={refKey ?? ""} defId={defId} depth={childDepth} ...");

                long childInstId = CrearInstanciaHija(defId, inputJson, usuario,
                    parentInstId: parentInstId > 0 ? (long?)parentInstId : null,
                    parentNodoId: nodo.Id,
                    rootInstId: rootInstId > 0 ? (long?)rootInstId : null,
                    depth: childDepth
                );

                await WorkflowRuntime.EjecutarInstanciaExistenteAsync(childInstId, usuario, ct);

                // --- 7) Leer resultado del hijo ---
                string childEstado;
                string childDatosContexto;
                LeerInstancia(childInstId, out childEstado, out childDatosContexto);

                // Alias (opcional) para outputs subflows.<alias>.*
                string alias = ctx.ExpandString(GetString(p, "as") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    alias = alias.Replace(" ", "");

                    // Validación mínima profesional: identifier seguro para path
                    // - empieza con letra o _
                    // - luego letras/números/_
                    // (evita '.', '/', '\', espacios, etc.)
                    if (!(alias.Length > 0 &&
                          (char.IsLetter(alias[0]) || alias[0] == '_') &&
                          alias.All(ch => char.IsLetterOrDigit(ch) || ch == '_')))
                    {
                        ctx.Log("[util.subflow/error] Alias inválido. Use letras/números/_ y que no empiece con número.");
                        return new ResultadoEjecucion { Etiqueta = "error" };
                    }
                }

                // --- 8) Outputs al padre (compatibilidad + múltiples subflows) ---
                var payloadEstado = ExtractEstado(childDatosContexto);
                var payloadLogs = ExtractLogs(childDatosContexto);

                // Compatibilidad: último subflow ejecutado
                ContextoEjecucion.SetPath(ctx.Estado, "subflow.instanceId", childInstId);
                ContextoEjecucion.SetPath(ctx.Estado, "subflow.childState", childEstado);
                ContextoEjecucion.SetPath(ctx.Estado, "subflow.ref", refKey ?? "");
                ContextoEjecucion.SetPath(ctx.Estado, "subflow.estado", payloadEstado);
                ContextoEjecucion.SetPath(ctx.Estado, "subflow.logs", payloadLogs);

                // Múltiples subflows: si viene alias, también escribir en subflows.<alias>.*
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    string basePath = "subflows." + alias;

                    ContextoEjecucion.SetPath(ctx.Estado, basePath + ".instanceId", childInstId);
                    ContextoEjecucion.SetPath(ctx.Estado, basePath + ".childState", childEstado);
                    ContextoEjecucion.SetPath(ctx.Estado, basePath + ".ref", refKey ?? "");
                    ContextoEjecucion.SetPath(ctx.Estado, basePath + ".estado", payloadEstado);
                    ContextoEjecucion.SetPath(ctx.Estado, basePath + ".logs", payloadLogs);
                }

                // --- 9) actualizar callStack y depth del padre (solo en memoria del ctx) ---
                if (!string.IsNullOrWhiteSpace(refKey))
                {
                    stack.Add(refKey);
                    ContextoEjecucion.SetPath(ctx.Estado, "wf.callStack", stack.ToArray());
                }
                ContextoEjecucion.SetPath(ctx.Estado, "wf.depth", currentDepth); // el padre sigue con su depth

                ctx.Log($"[util.subflow] OK childInstId={childInstId}, estado={childEstado}");
                return new ResultadoEjecucion { Etiqueta = "always" };
            }
            catch (Exception ex)
            {
                ctx.Log("[util.subflow/error] " + ex.GetType().Name + ": " + ex.Message);
                return new ResultadoEjecucion { Etiqueta = "error" };
            }
        }

        // ===================== DB helpers =====================

        private static int BuscarDefIdPorKey(string key)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
                SELECT TOP 1 Id
                FROM dbo.WF_Definicion
                WHERE [Key] = @Key
                  AND Activo = 1
                ORDER BY Version DESC, Id DESC;", cn))
            {
                cmd.Parameters.Add("@Key", SqlDbType.NVarChar, 80).Value = key;
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
            }
        }

        private static long CrearInstanciaHija(
            int defId,
            string datosEntradaJson,
            string usuario,
            long? parentInstId,
            string parentNodoId,
            long? rootInstId,
            int depth)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
                INSERT INTO dbo.WF_Instancia
                    (WF_DefinicionId, Estado, FechaInicio, DatosEntrada, CreadoPor,
                     ParentInstanciaId, ParentNodoId, RootInstanciaId, Depth)
                VALUES
                    (@DefId, 'EnCurso', GETDATE(), @Datos, @User,
                     @ParentInstId, @ParentNodoId, @RootInstId, @Depth);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosEntradaJson ?? DBNull.Value;
                cmd.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = (object)usuario ?? "app";

                cmd.Parameters.Add("@ParentInstId", SqlDbType.BigInt).Value = (object)parentInstId ?? DBNull.Value;
                cmd.Parameters.Add("@ParentNodoId", SqlDbType.NVarChar, 50).Value = (object)parentNodoId ?? DBNull.Value;
                cmd.Parameters.Add("@RootInstId", SqlDbType.BigInt).Value = (object)rootInstId ?? DBNull.Value;
                cmd.Parameters.Add("@Depth", SqlDbType.Int).Value = depth;

                cn.Open();
                return (long)cmd.ExecuteScalar();
            }
        }

        private static void LeerInstancia(long instId, out string estado, out string datosContexto)
        {
            estado = null;
            datosContexto = null;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
                SELECT Estado, DatosContexto
                FROM dbo.WF_Instancia
                WHERE Id = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = instId;
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new InvalidOperationException("WF_Instancia no encontrada: " + instId);

                    estado = dr.IsDBNull(0) ? null : dr.GetString(0);
                    datosContexto = dr.IsDBNull(1) ? null : dr.GetString(1);
                }
            }
        }

        private static object ExtractLogs(string datosContextoJson)
        {
            if (string.IsNullOrWhiteSpace(datosContextoJson)) return new object[0];
            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(datosContextoJson);
                var logs = root?["logs"];
                if (logs == null) return new object[0];
                return logs.ToObject<object[]>() ?? new object[0];
            }
            catch { return new object[0]; }
        }

        private static object ExtractEstado(string datosContextoJson)
        {
            if (string.IsNullOrWhiteSpace(datosContextoJson))
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(datosContextoJson);
                var est = root?["estado"];
                if (est == null)
                    return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                return est.ToObject<Dictionary<string, object>>() ??
                       new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }

        // ===================== Param helpers =====================

        private static string BuildInputJson(ContextoEjecucion ctx, IDictionary<string, object> p)
        {
            if (p == null) return null;

            if (p.TryGetValue("input", out var inputObj) && inputObj != null)
            {
                // si es string, lo tratamos como JSON o texto (y lo expandimos)
                if (inputObj is string s)
                {
                    var exp = ctx.ExpandString(s);
                    return string.IsNullOrWhiteSpace(exp) ? null : exp;
                }

                // Si es objeto, lo convertimos a JToken, expandimos strings recursivamente y serializamos.
                var tok = JToken.FromObject(inputObj);
                ExpandJTokenStrings(ctx, tok);
                return tok.ToString(Formatting.None);
            }

            return null;
        }

        private static void ExpandJTokenStrings(ContextoEjecucion ctx, JToken tok)
        {
            if (tok == null) return;

            if (tok.Type == JTokenType.String)
            {
                var v = tok.Value<string>();
                var exp = ctx.ExpandString(v ?? "");
                ((JValue)tok).Value = exp;
                return;
            }

            if (tok is JObject o)
            {
                foreach (var prop in o.Properties())
                    ExpandJTokenStrings(ctx, prop.Value);
                return;
            }

            if (tok is JArray a)
            {
                foreach (var it in a)
                    ExpandJTokenStrings(ctx, it);
                return;
            }
        }

        private static string TryGetEstadoString(ContextoEjecucion ctx, string path)
        {
            try
            {
                var o = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                return o == null ? null : Convert.ToString(o);
            }
            catch { return null; }
        }

        private static int TryGetEstadoInt(ContextoEjecucion ctx, string path, int def)
        {
            try
            {
                var o = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                if (o == null) return def;
                if (o is int i) return i;
                if (int.TryParse(Convert.ToString(o), out var x)) return x;
                return def;
            }
            catch { return def; }
        }

        private static long TryGetEstadoLong(ContextoEjecucion ctx, string path, long def)
        {
            try
            {
                var o = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                if (o == null) return def;
                if (o is long l) return l;
                if (long.TryParse(Convert.ToString(o), out var x)) return x;
                return def;
            }
            catch { return def; }
        }

        private static List<string> TryGetEstadoStringList(ContextoEjecucion ctx, string path)
        {
            var list = new List<string>();
            try
            {
                var o = ContextoEjecucion.ResolverPath(ctx.Estado, path);
                if (o == null) return list;

                if (o is IEnumerable<object> oe && !(o is string))
                {
                    foreach (var it in oe)
                    {
                        var s = Convert.ToString(it);
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                    }
                    return list;
                }

                // si vino como string CSV
                var csv = Convert.ToString(o);
                if (!string.IsNullOrWhiteSpace(csv))
                {
                    foreach (var s in csv.Split(','))
                    {
                        var t = s.Trim();
                        if (!string.IsNullOrWhiteSpace(t)) list.Add(t);
                    }
                }
                return list;
            }
            catch { return list; }
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static int GetInt(IDictionary<string, object> p, string key, int def)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
            {
                if (int.TryParse(Convert.ToString(v), out var i)) return i;
            }
            return def;
        }
    }
}
