using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.Runtime
{
    public static class EntidadService
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        /// <summary>
        /// Crea (si no existe) la WF_Entidad vinculada a una WF_Instancia.
        /// TipoEntidad por defecto: WF_Definicion.Codigo.
        /// Devuelve EntidadId.
        /// </summary>
        public static long EnsureEntidadForInstance(long instanciaId, int definicionId, string usuario)
        {
            if (instanciaId <= 0) throw new ArgumentOutOfRangeException(nameof(instanciaId));

            using (var cn = new SqlConnection(Cnn))
            {
                cn.Open();

                // TipoEntidad = Codigo de la definición
                string tipoEntidad = null;
                using (var cmdTipo = new SqlCommand(
                    "SELECT Codigo FROM dbo.WF_Definicion WHERE Id=@Id", cn))
                {
                    cmdTipo.Parameters.Add("@Id", SqlDbType.Int).Value = definicionId;
                    var x = cmdTipo.ExecuteScalar();
                    tipoEntidad = (x == null || x == DBNull.Value) ? null : Convert.ToString(x);
                }
                if (string.IsNullOrWhiteSpace(tipoEntidad))
                    tipoEntidad = "WF_DEF_" + definicionId;

                // Si ya existe, devolver
                using (var cmdGet = new SqlCommand(
                    "SELECT EntidadId FROM dbo.WF_Entidad WHERE InstanciaId=@I", cn))
                {
                    cmdGet.Parameters.Add("@I", SqlDbType.BigInt).Value = instanciaId;
                    var x = cmdGet.ExecuteScalar();
                    if (x != null && x != DBNull.Value)
                        return Convert.ToInt64(x);
                }

                // Crear
                using (var cmdIns = new SqlCommand(@"
INSERT INTO dbo.WF_Entidad (TipoEntidad, EstadoActual, InstanciaId, CreadoPor, ActualizadoPor)
VALUES (@Tipo, @Estado, @InstanciaId, @User, @User);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
                {
                    cmdIns.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = tipoEntidad;
                    cmdIns.Parameters.Add("@Estado", SqlDbType.NVarChar, 50).Value = "Pendiente";
                    cmdIns.Parameters.Add("@InstanciaId", SqlDbType.BigInt).Value = instanciaId;
                    cmdIns.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = (object)(usuario ?? "app") ?? DBNull.Value;

                    return Convert.ToInt64(cmdIns.ExecuteScalar());
                }
            }
        }

        /// <summary>
        /// Guarda snapshot (DataJson), materializa items[] y recalcula índices básicos.
        /// Es seguro llamar múltiples veces.
        /// </summary>
        public static void SnapshotFromState(System.Collections.Generic.IDictionary<string, object> estado, string usuario)
        {
            if (estado == null) return;

            if (!TryGetLong(estado, "entidad.id", out long entidadId) || entidadId <= 0)
                return;

            // biz puede ser Dictionary / JObject / anónimo: lo normalizamos a JToken
            JToken bizTok = null;
            if (estado.TryGetValue("biz", out var bizObj) && bizObj != null)
            {
                try
                {
                    bizTok = bizObj as JToken ?? JToken.FromObject(bizObj);
                }
                catch { bizTok = null; }
            }

            // payload/wf meta mínimo (para trazabilidad)
            object wfInstanceId = null;
            estado.TryGetValue("wf.instanceId", out wfInstanceId);

            decimal? totalBiz = null;
            try
            {
                if (bizTok != null)
                {
                    var tTok = bizTok.SelectToken("total");
                    if (tTok != null)
                    {
                        if (tTok.Type == JTokenType.Float || tTok.Type == JTokenType.Integer)
                            totalBiz = tTok.Value<decimal>();
                        else
                        {
                            var s = tTok.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(s))
                            {
                                // soporta "1712,50" o "1712.50"
                                s = s.Replace(".", "").Replace(",", "."); // simple y práctico
                                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var dec))
                                    totalBiz = dec;
                            }
                        }
                    }
                }
            }
            catch { totalBiz = null; }

            // ✅ EstadoActual NORMALIZADO (solo: Iniciado / Finalizado / Error)
            string estadoActual = ResolveEstadoActual(estado);

            var snapshotObj = new
            {
                wf = new
                {
                    instanceId = wfInstanceId,
                    entidadId = entidadId,
                    estado = estadoActual // ✅ trazabilidad
                },
                biz = bizTok
            };

            string dataJson = JsonConvert.SerializeObject(snapshotObj, Formatting.None);

            using (var cn = new SqlConnection(Cnn))
            {
                cn.Open();

                // 1) Update snapshot
                using (var cmdUp = new SqlCommand(@"
UPDATE dbo.WF_Entidad
SET DataJson = @Json,
    EstadoActual = COALESCE(@EstadoActual, EstadoActual),
    EstadoNegocio = COALESCE(@EstadoNegocio, EstadoNegocio),
    Total = COALESCE(@Total, Total),
    ActualizadoUtc = SYSUTCDATETIME(),
    ActualizadoPor = @User
WHERE EntidadId = @Id;", cn))
                {
                    cmdUp.Parameters.Add("@Json", SqlDbType.NVarChar).Value = (object)dataJson ?? DBNull.Value;

                    // ✅ si no se pudo resolver, NO pisa el estado existente
                    cmdUp.Parameters.Add("@EstadoActual", SqlDbType.NVarChar, 50).Value =
                        string.IsNullOrWhiteSpace(estadoActual) ? (object)DBNull.Value : (object)estadoActual;
                    var estadoNegocio = ResolveEstadoNegocio(estado);
                    cmdUp.Parameters.Add("@EstadoNegocio", SqlDbType.NVarChar, 80).Value =
                                    string.IsNullOrWhiteSpace(estadoNegocio) ? (object)DBNull.Value : (object)estadoNegocio;

                    var pTot = cmdUp.Parameters.Add("@Total", SqlDbType.Decimal);
                    pTot.Precision = 18;
                    pTot.Scale = 2;
                    pTot.Value = (object)totalBiz ?? DBNull.Value;

                    cmdUp.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = (object)(usuario ?? "app") ?? DBNull.Value;
                    cmdUp.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                    
                    cmdUp.ExecuteNonQuery();
                }

                // 2) Items: re-materializar
                using (var cmdDel = new SqlCommand(
                    "DELETE FROM dbo.WF_EntidadItem WHERE EntidadId=@Id;", cn))
                {
                    cmdDel.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                    cmdDel.ExecuteNonQuery();
                }

                if (bizTok != null)
                {
                    var items = bizTok["items"] as JArray;
                    if (items != null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            var it = items[i];
                            string itJson = it.ToString(Formatting.None);

                            string desc = TryGetString(it, "descripcion") ?? TryGetString(it, "Descripción") ?? TryGetString(it, "desc");
                            decimal? cant = TryGetDecimal(it, "cantidad") ?? TryGetDecimal(it, "Cantidad");
                            decimal? imp = TryGetDecimal(it, "importe") ?? TryGetDecimal(it, "Importe") ?? TryGetDecimal(it, "total");

                            using (var cmdInsItem = new SqlCommand(@"
INSERT INTO dbo.WF_EntidadItem (EntidadId, ItemIndex, Path, DataJson, Descripcion, Cantidad, Importe)
VALUES (@E, @Idx, @Path, @Json, @Desc, @Cant, @Imp);", cn))
                            {
                                cmdInsItem.Parameters.Add("@E", SqlDbType.BigInt).Value = entidadId;
                                cmdInsItem.Parameters.Add("@Idx", SqlDbType.Int).Value = i;
                                cmdInsItem.Parameters.Add("@Path", SqlDbType.NVarChar, 200).Value = "biz.items[" + i + "]";
                                cmdInsItem.Parameters.Add("@Json", SqlDbType.NVarChar).Value = itJson;
                                cmdInsItem.Parameters.Add("@Desc", SqlDbType.NVarChar, 400).Value = (object)desc ?? DBNull.Value;

                                var pCant = cmdInsItem.Parameters.Add("@Cant", SqlDbType.Decimal);
                                pCant.Value = (object)cant ?? DBNull.Value;
                                pCant.Precision = 18;
                                pCant.Scale = 4;

                                var pImp = cmdInsItem.Parameters.Add("@Imp", SqlDbType.Decimal);
                                pImp.Value = (object)imp ?? DBNull.Value;
                                pImp.Precision = 18;
                                pImp.Scale = 2;

                                cmdInsItem.ExecuteNonQuery();
                            }
                        }
                    }
                }

                // 3) Índices: limpiar y recalcular
                using (var cmdDelIdx = new SqlCommand(
                    "DELETE FROM dbo.WF_EntidadIndice WHERE EntidadId=@Id;", cn))
                {
                    cmdDelIdx.Parameters.Add("@Id", SqlDbType.BigInt).Value = entidadId;
                    cmdDelIdx.ExecuteNonQuery();
                }

                var paths = new (string key, string path, string tipo)[]
                {
            ("numero", "numero", "string"),
            ("fecha", "fecha", "fecha"),
            ("total", "total", "decimal"),
            ("empresa", "empresa.nombre", "string"),
            ("empresaCuit", "empresa.cuit", "cuit"),
            ("proveedor", "proveedor.nombre", "string"),
            ("proveedorCuit", "proveedor.cuit", "cuit")
                };

                if (bizTok != null)
                {
                    foreach (var p in paths)
                    {
                        var vTok = bizTok.SelectToken(p.path);
                        if (vTok == null) continue;

                        string v = vTok.Type == JTokenType.String ? (string)vTok : vTok.ToString(Formatting.None);
                        if (string.IsNullOrWhiteSpace(v)) continue;

                        string vNorm = Normalize(v);
                        if (vNorm.Length > 400) vNorm = vNorm.Substring(0, 400);
                        if (v.Length > 400) v = v.Substring(0, 400);

                        InsertIndice(cn, entidadId, p.key, v, vNorm, p.tipo, "biz." + p.path);
                    }
                }
            }
        }

        private static string ResolveEstadoActual(System.Collections.Generic.IDictionary<string, object> estado)
        {
            string raw = null;

            if (estado.TryGetValue("wf.estado", out var a) && a != null) raw = Convert.ToString(a);
            if (string.IsNullOrWhiteSpace(raw) && estado.TryGetValue("wf.state", out var b) && b != null) raw = Convert.ToString(b);
            if (string.IsNullOrWhiteSpace(raw) && estado.TryGetValue("wf.status", out var c) && c != null) raw = Convert.ToString(c);

            if (string.IsNullOrWhiteSpace(raw)) return null;

            var s = raw.Trim().ToLowerInvariant();

            // ✅ mapear estados posibles a los 3 del producto
            if (s == "finalizado" || s == "finalizada" || s == "completed" || s == "completada" || s == "done" || s == "ok")
                return "Finalizado";

            if (s == "error" || s == "failed" || s == "fallo" || s == "fallido" || s == "exception")
                return "Error";

            if (s == "iniciado" || s == "encurso" || s == "en_curso" || s == "running" || s == "pendiente" || s == "pending" || s == "activo" || s == "activa")
                return "Iniciado";

            // Si viene otra cosa rara, no pisamos EstadoActual
            return null;
        }

        private static string ResolveEstadoNegocio(System.Collections.Generic.IDictionary<string, object> estado)
        {
            // 1️⃣ prioridad: si el workflow define explícitamente wf.estadoNegocio
            if (estado.TryGetValue("wf.estadoNegocio", out var a) && a != null)
            {
                var s = Convert.ToString(a)?.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            // 2️⃣ fallback: usar wf.estado normalizado (lo actual)
            var est = ResolveEstadoActual(estado);
            return est;
        }

        private static void InsertIndice(SqlConnection cn, long entidadId, string key, string value, string valueNorm, string tipoDato, string sourcePath)
        {
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_EntidadIndice (EntidadId, [Key], [Value], ValueNorm, TipoDato, SourcePath)
VALUES (@E, @K, @V, @VN, @T, @P);", cn))
            {
                cmd.Parameters.Add("@E", SqlDbType.BigInt).Value = entidadId;
                cmd.Parameters.Add("@K", SqlDbType.NVarChar, 100).Value = key ?? "";
                cmd.Parameters.Add("@V", SqlDbType.NVarChar, 400).Value = (object)value ?? DBNull.Value;
                cmd.Parameters.Add("@VN", SqlDbType.NVarChar, 400).Value = (object)valueNorm ?? DBNull.Value;
                cmd.Parameters.Add("@T", SqlDbType.NVarChar, 30).Value = (object)tipoDato ?? DBNull.Value;
                cmd.Parameters.Add("@P", SqlDbType.NVarChar, 200).Value = (object)sourcePath ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }
        }

        private static string Normalize(string s)
        {
            if (s == null) return null;
            return s.Trim().ToLowerInvariant();
        }

        private static bool TryGetLong(IDictionary<string, object> dic, string key, out long val)
        {
            val = 0;
            if (dic == null || key == null) return false;
            if (!dic.TryGetValue(key, out var o) || o == null) return false;

            try
            {
                if (o is long l) { val = l; return true; }
                if (o is int i) { val = i; return true; }
                if (o is string s && long.TryParse(s, out var x)) { val = x; return true; }
                val = Convert.ToInt64(o);
                return true;
            }
            catch { return false; }
        }

        private static string TryGetString(JToken tok, string prop)
        {
            try
            {
                var t = tok?[prop];
                if (t == null) return null;
                if (t.Type == JTokenType.String) return (string)t;
                return t.ToString(Formatting.None);
            }
            catch { return null; }
        }

        private static decimal? TryGetDecimal(JToken tok, string prop)
        {
            try
            {
                var t = tok?[prop];
                if (t == null) return null;

                if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                    return t.Value<decimal>();

                var s = t.ToString(Formatting.None);
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                    return d;

                if (decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.CurrentCulture, out d))
                    return d;

                return null;
            }
            catch { return null; }
        }
    }
}