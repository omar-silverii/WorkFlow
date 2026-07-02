using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.WebForms
{
    public class WfAiCatalogProvider
    {
        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        public WfAiCatalog Build()
        {
            var catalog = new WfAiCatalog();
            AddAllowedNodes(catalog);
            AddBaseFields(catalog);
            TryLoadDocTypes(catalog);
            TryLoadRoles(catalog);
            TryLoadUsers(catalog);
            return catalog;
        }

        private static void AddAllowedNodes(WfAiCatalog catalog)
        {
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.start", Label = "Inicio", Params = new List<string>() });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.end", Label = "Fin", Params = new List<string>() });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "doc.load", Label = "Documento: Cargar archivo", Params = new List<string> { "path", "mode", "docTipoCodigo", "outputPrefix" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "control.if", Label = "Condición (If)", Params = new List<string> { "field", "op", "value", "expression", "transform", "rulesMode", "rules" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "human.task", Label = "Tarea humana", Params = new List<string> { "rol", "usuarioAsignado", "titulo", "descripcion", "scopeKey", "deadlineMinutes", "estadoNegocioPendiente" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "email.send", Label = "Correo: Enviar", Params = new List<string> { "from", "to", "cc", "bcc", "subject", "body", "html", "modo", "useWebConfig", "isHtml" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.notify", Label = "Notificar", Params = new List<string> { "tipo", "canal", "nivel", "destinoTipo", "usuarioDestino", "rolDestino", "destino", "prioridad", "asunto", "mensaje", "urlAccion" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "http.request", Label = "Solicitud HTTP", Params = new List<string> { "method", "url", "headers", "query", "body", "contentType", "timeoutMs", "failOnStatus", "failStatusMin" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "data.sql", Label = "Consulta SQL", Params = new List<string> { "connectionStringName", "query", "commandText", "parameters" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "file.read", Label = "Archivo: Leer", Params = new List<string> { "path", "salida", "output", "asJson", "encoding", "zipMode", "zipEntry", "useCache" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "file.write", Label = "Archivo: Escribir", Params = new List<string> { "path", "content", "origen", "encoding", "overwrite", "zipMode", "entryName", "zipEntryName" } });
            // fix64b: el catálogo usado por el validador funcional también debe permitir los nodos de cola
            // que ya existen en workflow.catalog.js, WfAiNodeCapabilityMap.cs y handlers runtime.
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "queue.publish", Label = "Cola: Publicar", Params = new List<string> { "broker", "queue", "payload", "connectionStringName", "correlationId", "dueAt", "priority" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "queue.consume", Label = "Cola: Consumir", Params = new List<string> { "broker", "queue", "take", "prefetch", "connectionStringName", "outputPrefix", "debug" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.subflow", Label = "Ejecutar otro workflow", Params = new List<string> { "ref", "input", "as", "maxDepth", "usuario" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "state.vars", Label = "Variables", Params = new List<string> { "set", "remove" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "control.delay", Label = "Demora (Delay)", Params = new List<string> { "ms", "seconds", "message" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "control.retry", Label = "Reintentar", Params = new List<string> { "reintentos", "backoffMs", "message" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.error", Label = "Manejador de Error", Params = new List<string> { "mensaje", "capturar", "capturarErrores", "notificar", "volverAIntentar", "reintentar" } });
            catalog.Nodes.Add(new WfAiNodeInfo { Type = "util.logger", Label = "Logger", Params = new List<string> { "message", "level" } });
        }

        private static void AddBaseFields(WfAiCatalog catalog)
        {
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.instanceId", Label = "ID de instancia" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.estado", Label = "Estado del workflow" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "input.filePath", Label = "Ruta de archivo" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "input.text", Label = "Texto extraído" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "input.hasText", Label = "Tiene texto" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "payload.status", Label = "Status HTTP" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "payload.body", Label = "Respuesta HTTP texto" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "payload.json", Label = "Respuesta HTTP JSON" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "sql.rows", Label = "Filas SQL" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "sql.rowCount", Label = "Cantidad de filas SQL" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "sql.first", Label = "Primera fila SQL" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "sql.scalar", Label = "Primer valor SQL" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "sql.rowsAffected", Label = "Filas afectadas SQL" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.error", Label = "Error marcado" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.error.message", Label = "Mensaje de error" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.error.nodeId", Label = "Nodo que marcó error" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "wf.error.nodeType", Label = "Tipo de nodo de error" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "archivo", Label = "Contenido leído por archivo" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.read.lastPath", Label = "Última ruta leída" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.read.lastLength", Label = "Longitud del archivo leído" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.read.lastEncoding", Label = "Encoding de lectura" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.read.lastZipMode", Label = "Compresión detectada" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.read.lastError", Label = "Último error de lectura" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.write.lastPath", Label = "Última ruta escrita" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.write.lastLength", Label = "Longitud escrita" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.write.lastEncoding", Label = "Encoding de escritura" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.write.lastZipMode", Label = "Modo ZIP de escritura" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "file.write.lastError", Label = "Último error de escritura" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "queue.last", Label = "Última operación de cola" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "queue.message", Label = "Mensaje consumido de cola" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "queue.messageId", Label = "ID de mensaje de cola" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "queue.hasMessage", Label = "La cola devolvió mensaje" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "queue.error", Label = "Último error de cola" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "subflow.instanceId", Label = "Instancia hija del subflujo" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "subflow.childState", Label = "Estado de instancia hija" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "subflow.ref", Label = "Workflow hijo ejecutado" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "subflow.estado", Label = "Estado/Datos del subflujo" });
            catalog.Fields.Add(new WfAiFieldInfo { Path = "subflow.logs", Label = "Logs del subflujo" });
        }

        private void TryLoadDocTypes(WfAiCatalog catalog)
        {
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand(@"
SELECT Codigo, Nombre, ContextPrefix, MotorExtraccion
FROM dbo.WF_DocTipo
WHERE EsActivo = 1
ORDER BY Codigo;", cn))
                {
                    cn.Open();
                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            string codigo = Convert.ToString(dr["Codigo"] ?? "").Trim();
                            string prefix = Convert.ToString(dr["ContextPrefix"] ?? "").Trim();

                            catalog.DocTypes.Add(new WfAiDocTypeInfo
                            {
                                Codigo = codigo,
                                Nombre = Convert.ToString(dr["Nombre"] ?? "").Trim(),
                                ContextPrefix = prefix,
                                MotorExtraccion = Convert.ToString(dr["MotorExtraccion"] ?? "").Trim()
                            });

                            AddKnownExtractorFields(catalog, codigo, prefix);
                        }
                    }
                }

                TryLoadRuleFields(catalog);
            }
            catch (Exception ex)
            {
                catalog.Warnings.Add("No se pudo cargar WF_DocTipo: " + ex.Message);
                AddFallbackDocTypes(catalog);
            }
        }

        private void TryLoadRuleFields(WfAiCatalog catalog)
        {
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand(@"
SELECT d.Codigo, d.ContextPrefix, r.Campo
FROM dbo.WF_DocTipo d
INNER JOIN dbo.WF_DocTipoReglaExtract r ON r.DocTipoId = d.DocTipoId
WHERE d.EsActivo = 1 AND r.Activo = 1
ORDER BY d.Codigo, r.Orden, r.Id;", cn))
                {
                    cn.Open();
                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            string codigo = Convert.ToString(dr["Codigo"] ?? "").Trim();
                            string prefix = Convert.ToString(dr["ContextPrefix"] ?? "").Trim();
                            string campo = Convert.ToString(dr["Campo"] ?? "").Trim();

                            if (prefix.Length == 0 || campo.Length == 0) continue;
                            if (campo == "items[].__block") continue;

                            string fieldName = campo.Replace("items[].", "items[].");
                            catalog.Fields.Add(new WfAiFieldInfo
                            {
                                Path = "biz." + prefix + "." + fieldName,
                                Label = campo,
                                DocTipo = codigo
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                catalog.Warnings.Add("No se pudieron cargar reglas de extracción: " + ex.Message);
            }
        }

        private static void AddFallbackDocTypes(WfAiCatalog catalog)
        {
            catalog.DocTypes.Add(new WfAiDocTypeInfo { Codigo = "FACTURA_ELECTRONICA_AR", Nombre = "Factura electrónica AFIP", ContextPrefix = "factura", MotorExtraccion = "FACTURA_AR" });
            catalog.DocTypes.Add(new WfAiDocTypeInfo { Codigo = "NOTA_CREDITO_ELECTRONICA_AR", Nombre = "Nota de crédito electrónica AFIP", ContextPrefix = "notaCredito", MotorExtraccion = "NC_AR" });
            AddKnownExtractorFields(catalog, "FACTURA_ELECTRONICA_AR", "factura");
            AddKnownExtractorFields(catalog, "NOTA_CREDITO_ELECTRONICA_AR", "notaCredito");
        }

        private static void AddKnownExtractorFields(WfAiCatalog catalog, string codigo, string prefix)
        {
            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(prefix)) return;

            if (codigo.Equals("FACTURA_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase))
            {
                AddField(catalog, prefix, "numero", codigo);
                AddField(catalog, prefix, "fecha", codigo);
                AddField(catalog, prefix, "cae", codigo);
                AddField(catalog, prefix, "caeVencimiento", codigo);
                AddField(catalog, prefix, "total", codigo);
                AddField(catalog, prefix, "itemsCount", codigo);
            }

            if (codigo.Equals("NOTA_CREDITO_ELECTRONICA_AR", StringComparison.OrdinalIgnoreCase))
            {
                AddField(catalog, prefix, "numero", codigo);
                AddField(catalog, prefix, "fecha", codigo);
                AddField(catalog, prefix, "cae", codigo);
                AddField(catalog, prefix, "caeVencimiento", codigo);
                AddField(catalog, prefix, "total", codigo);
                AddField(catalog, prefix, "validacionBasicaOk", codigo);
                AddField(catalog, prefix, "itemsCount", codigo);
                AddField(catalog, prefix, "comprobanteAsociado.numero", codigo);
            }
        }

        private static void AddField(WfAiCatalog catalog, string prefix, string field, string docTipo)
        {
            catalog.Fields.Add(new WfAiFieldInfo
            {
                Path = "biz." + prefix + "." + field,
                Label = field,
                DocTipo = docTipo
            });
        }

        private void TryLoadRoles(WfAiCatalog catalog)
        {
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand(@"
SELECT RolKey
FROM dbo.WF_Rol
WHERE Activo = 1
ORDER BY RolKey;", cn))
                {
                    cn.Open();
                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            string rol = Convert.ToString(dr["RolKey"] ?? "").Trim();
                            if (rol.Length > 0) catalog.Roles.Add(rol);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                catalog.Warnings.Add("No se pudieron cargar roles: " + ex.Message);
                catalog.Roles.Add("DIR_GENERAL");
                catalog.Roles.Add("COMPRAS");
                catalog.Roles.Add("OPERACIONES");
                catalog.Roles.Add("ADM_FIN");
                catalog.Roles.Add("IT");
            }
        }

        private void TryLoadUsers(WfAiCatalog catalog)
        {
            try
            {
                using (var cn = new SqlConnection(Cnn))
                using (var cmd = new SqlCommand(@"
SELECT TOP 50 UserKey, DisplayName
FROM dbo.WF_User
WHERE Activo = 1
ORDER BY UserKey;", cn))
                {
                    cn.Open();
                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            catalog.Users.Add(new WfAiUserInfo
                            {
                                UserKey = Convert.ToString(dr["UserKey"] ?? "").Trim(),
                                DisplayName = Convert.ToString(dr["DisplayName"] ?? "").Trim()
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                catalog.Warnings.Add("No se pudieron cargar usuarios: " + ex.Message);
            }
        }
    }
}
