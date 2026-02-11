using Intranet.WorkflowStudio.WebForms.App_Code.Handlers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Handler: doc.attach
    /// - Adjunta referencias documentales al caso (no archivos)
    /// - Persistencia/Auditoría: inserta un registro en dbo.WF_InstanciaDocumento
    ///
    /// Params:
    ///   mode: "root" | "attachment" (default: attachment)
    ///   attachToCurrentTask: bool (default false) -> doc.tareaId = ${wf.tarea.id} si existe
    ///   taskId: string|number (opcional) si querés forzar tareaId
    ///   connectionStringName: (opcional) default "DefaultConnection"
    ///   usuario: (opcional) default ${wf.creadoPor}
    ///   docJson: string JSON (preferido) (se expande ${...})
    ///   doc: object (Dictionary/JObject) fallback
    /// </summary>
    public class HDocAttach : IManejadorNodo
    {
        public string TipoNodo => "doc.attach";

        public Task<ResultadoEjecucion> EjecutarAsync(ContextoEjecucion ctx, NodeDef nodo, CancellationToken ct)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (nodo == null) throw new ArgumentNullException(nameof(nodo));

            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var mode = (TemplateUtil.Expand(ctx, GetString(p, "mode")) ?? "attachment").Trim().ToLowerInvariant();
            var attachToCurrentTask = GetBool(p, "attachToCurrentTask", false);

            var cnnName = (GetString(p, "connectionStringName") ?? "DefaultConnection").Trim();
            var usuario = TemplateUtil.Expand(ctx, GetString(p, "usuario")) ?? TemplateUtil.Expand(ctx, "${wf.creadoPor}") ?? "app";

            // doc param
            Dictionary<string, object> doc = ReadDoc(ctx, p);

            if (doc == null)
            {
                ctx.Log("[doc.attach] doc=null (no se adjunta nada)");
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }

            // Normalizar contrato mínimo (referencia documental)
            EnsureKey(doc, "documentoId");
            EnsureKey(doc, "carpetaId");
            EnsureKey(doc, "ficheroId");
            EnsureKey(doc, "tipo");
            EnsureKey(doc, "indices");
            EnsureKey(doc, "viewerUrl");

            // Scope tarea
            var forcedTaskId = TemplateUtil.Expand(ctx, GetString(p, "taskId"));
            if (!string.IsNullOrWhiteSpace(forcedTaskId))
            {
                doc["tareaId"] = forcedTaskId;
            }
            else if (attachToCurrentTask)
            {
                var tid = ContextoEjecucion.ResolverPath(ctx.Estado, "wf.tarea.id");
                if (tid != null) doc["tareaId"] = Convert.ToString(tid);
            }

            // Asegurar biz.case
            var biz = EnsureDict(ctx.Estado, "biz");
            var @case = EnsureDict(biz, "case");

            bool esRoot = (mode == "root");

            if (esRoot)
            {
                @case["rootDoc"] = doc;
                ctx.Log("[doc.attach] rootDoc seteado");
            }
            else
            {
                if (!@case.TryGetValue("attachments", out var a) || !(a is List<object> listObj))
                {
                    listObj = new List<object>();
                    @case["attachments"] = listObj;
                }

                listObj.Add(doc);
                ctx.Log("[doc.attach] attachment agregado (total=" + listObj.Count + ")");
            }

            // Persistencia/Auditoría (no bloqueante)
            try
            {
                PersistirAuditoria(ctx, cnnName, usuario, doc, esRoot);
            }
            catch (Exception ex)
            {
                // Importante: no frenamos el workflow por auditoría, solo log.
                ctx.Log("[doc.attach/audit] ERROR: " + ex.GetType().Name + " - " + ex.Message);
            }

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        private static void PersistirAuditoria(ContextoEjecucion ctx, string cnnName, string usuario, Dictionary<string, object> doc, bool esRoot)
        {
            // Instancia (fuente de verdad)
            var instIdObj = ContextoEjecucion.ResolverPath(ctx.Estado, "wf.instanceId");
            if (instIdObj == null) throw new InvalidOperationException("wf.instanceId no disponible en contexto");
            if (!long.TryParse(Convert.ToString(instIdObj), out var instId)) throw new InvalidOperationException("wf.instanceId inválido");

            // Nodo (si está publicado)
            var nodoId = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, "wf.currentNodeId") ?? "");
            var nodoTipo = Convert.ToString(ContextoEjecucion.ResolverPath(ctx.Estado, "wf.currentNodeType") ?? "");

            // Doc fields
            string documentoId = Convert.ToString(Get(doc, "documentoId"));
            string carpetaId = Convert.ToString(Get(doc, "carpetaId"));
            string ficheroId = Convert.ToString(Get(doc, "ficheroId"));
            string tipo = Convert.ToString(Get(doc, "tipo"));
            string viewerUrl = Convert.ToString(Get(doc, "viewerUrl"));

            // indices -> JSON
            string indicesJson = null;
            var indices = Get(doc, "indices");
            if (indices != null)
            {
                if (indices is string s) indicesJson = s;
                else indicesJson = JsonConvert.SerializeObject(indices, Formatting.None);
            }

            string tareaId = Convert.ToString(Get(doc, "tareaId"));

            // Connection string
            var csItem = ConfigurationManager.ConnectionStrings[cnnName];
            if (csItem == null) throw new InvalidOperationException($"ConnectionString '{cnnName}' no encontrada");
            string cnn = csItem.ConnectionString;

            using (var cn = new SqlConnection(cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_InstanciaDocumento
    (WF_InstanciaId, DocumentoId, CarpetaId, FicheroId, Tipo, IndicesJson, ViewerUrl,
     EsRoot, TareaId, NodoId, NodoTipo, Accion, Usuario)
VALUES
    (@InstId, @DocumentoId, @CarpetaId, @FicheroId, @Tipo, @IndicesJson, @ViewerUrl,
     @EsRoot, @TareaId, @NodoId, @NodoTipo, 'ATTACH', @Usuario);", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instId;

                cmd.Parameters.Add("@DocumentoId", SqlDbType.NVarChar, 100).Value = (object)documentoId ?? DBNull.Value;
                cmd.Parameters.Add("@CarpetaId", SqlDbType.NVarChar, 100).Value = (object)carpetaId ?? DBNull.Value;
                cmd.Parameters.Add("@FicheroId", SqlDbType.NVarChar, 100).Value = (object)ficheroId ?? DBNull.Value;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 100).Value = (object)tipo ?? DBNull.Value;

                cmd.Parameters.Add("@IndicesJson", SqlDbType.NVarChar).Value = (object)indicesJson ?? DBNull.Value;
                cmd.Parameters.Add("@ViewerUrl", SqlDbType.NVarChar, 1000).Value = (object)viewerUrl ?? DBNull.Value;

                cmd.Parameters.Add("@EsRoot", SqlDbType.Bit).Value = esRoot ? 1 : 0;
                cmd.Parameters.Add("@TareaId", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(tareaId) ? (object)DBNull.Value : tareaId;

                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(nodoId) ? (object)DBNull.Value : nodoId;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(nodoTipo) ? (object)DBNull.Value : nodoTipo;

                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 100).Value = (object)usuario ?? DBNull.Value;

                cn.Open();
                cmd.ExecuteNonQuery();
            }

            ctx.Log("[doc.attach/audit] OK -> WF_InstanciaDocumento (instId=" + instId + ")");
        }

        private static object Get(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            if (d.TryGetValue(k, out var v)) return v;

            // case-insensitive fallback (por si viene distinto)
            foreach (var kv in d)
                if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;

            return null;
        }

        private static Dictionary<string, object> ReadDoc(ContextoEjecucion ctx, IDictionary<string, object> p)
        {
            if (p == null) return null;

            // Preferimos docJson (string JSON)
            if (p.TryGetValue("docJson", out var dj) && dj is string sj && !string.IsNullOrWhiteSpace(sj))
            {
                var expanded = TemplateUtil.Expand(ctx, sj) ?? sj;
                try
                {
                    var tok = JToken.Parse(expanded);
                    if (tok is JObject obj)
                        return obj.ToObject<Dictionary<string, object>>();
                }
                catch { return null; }
            }

            if (!p.TryGetValue("doc", out var raw) || raw == null) return null;

            if (raw is JObject jo)
                return jo.ToObject<Dictionary<string, object>>();

            if (raw is Dictionary<string, object> dd)
                return new Dictionary<string, object>(dd, StringComparer.OrdinalIgnoreCase);

            if (raw is string s)
            {
                var expanded = TemplateUtil.Expand(ctx, s) ?? s;
                try
                {
                    var tok = JToken.Parse(expanded);
                    if (tok is JObject obj)
                        return obj.ToObject<Dictionary<string, object>>();
                }
                catch { return null; }
            }

            try
            {
                var tok = JToken.FromObject(raw);
                if (tok is JObject jobj)
                    return jobj.ToObject<Dictionary<string, object>>();
            }
            catch { }

            return null;
        }

        private static void EnsureKey(Dictionary<string, object> d, string k)
        {
            if (d == null) return;
            if (!d.ContainsKey(k)) d[k] = null;
        }

        private static Dictionary<string, object> EnsureDict(Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out var v) || !(v is Dictionary<string, object> dd))
            {
                dd = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                root[key] = dd;
            }
            return dd;
        }

        private static string GetString(IDictionary<string, object> p, string key)
        {
            if (p != null && p.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v);
            return null;
        }

        private static bool GetBool(IDictionary<string, object> p, string key, bool def = false)
        {
            if (p == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;
            if (v is bool b) return b;
            if (bool.TryParse(Convert.ToString(v), out var bb)) return bb;
            if (int.TryParse(Convert.ToString(v), out var ii)) return ii != 0;
            return def;
        }
    }
}
