using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix50: inventario formal de capacidades para el Constructor IA v2.
    ///
    /// Este archivo NO cambia la interpretación actual ni habilita nodos nuevos automáticamente.
    /// Sirve como mapa único para evolucionar el Constructor IA sin seguir agrandando
    /// WfAiMlnetProvider.cs con reglas sueltas.
    ///
    /// Fuente funcional inicial:
    /// - Scripts/workflow.catalog.js
    /// - handlers reales existentes en App_Code/Handlers, DocumentProcessing y Runtime
    /// </summary>
    public static class WfAiNodeCapabilityMap
    {
        public const string EstadoListo = "listo";
        public const string EstadoParcial = "parcial";
        public const string EstadoRestringido = "restringido";
        public const string EstadoPendiente = "pendiente";
        public const string EstadoNoHabilitado = "no_habilitado";

        public static List<WfAiNodeCapability> Build()
        {
            var list = new List<WfAiNodeCapability>();

            // Utilidad / Operación
            list.Add(Node("util.start", "Inicio", "Utilidad / Operación", true, EstadoListo, 1,
                null, null,
                L("iniciar", "inicio", "empezar", "arrancar workflow"),
                L("Nodo inicial técnico. Normalmente lo agrega el constructor, no el usuario."),
                L("Sin validación especial."),
                null));

            list.Add(Node("util.end", "Fin", "Utilidad / Operación", true, EstadoListo, 1,
                null, null,
                L("finalizar", "terminar", "fin", "cerrar workflow"),
                L("Nodo final técnico. Normalmente lo agrega el constructor."),
                L("Debe recibir al menos una conexión entrante."),
                null));

            list.Add(Node("util.logger", "Logger", "Utilidad / Operación", true, EstadoListo, 1,
                L("message"), L("level"),
                L("registrar", "registrar evento", "dejar log", "informativo", "advertencia", "error"),
                L("Usado por frases como 'registrar un evento informativo' o 'registrar una advertencia'."),
                L("message obligatorio", "level recomendado: Info/Warn/Error"),
                null));

            list.Add(Node("util.docTipo.resolve", "Documento: Resolver tipo", "Utilidad / Operación", true, EstadoParcial, 3,
                null, L("text", "path", "outputPrefix"),
                L("resolver tipo de documento", "detectar tipo de documento", "identificar documento"),
                L("Existe handler. Requiere decidir cuándo usarlo versus doc.load con docTipo explícito."),
                L("No generar si el usuario ya indicó tipo documental claro."),
                L("wf.docTipoCodigo", "wf.docTipoId", "wf.contextPrefix")));

            list.Add(Node("util.notify", "Notificar", "Utilidad / Operación", true, EstadoListo, 1,
                L("mensaje"), L("tipo", "canal", "nivel", "destinoTipo", "usuarioDestino", "rolDestino", "destino", "prioridad", "asunto", "urlAccion"),
                L("notificar", "avisar", "informar", "mandar aviso", "enviar notificación"),
                L("Debe distinguirse de human.task. 'notificar/avisar' no crea tarea humana."),
                L("mensaje obligatorio", "destino obligatorio: rolDestino, usuarioDestino o destino"),
                L("notify.last.id", "notify.last.destino", "notify.last.rolDestino", "notify.last.usuarioDestino", "notify.last.message")));

            list.Add(Node("util.error", "Manejador de Error", "Utilidad / Operación", true, EstadoParcial, 2,
                null, L("mensaje", "capturar", "capturarErrores", "notificar", "volverAIntentar", "reintentar"),
                L("manejar error", "si falla", "capturar error", "ante error", "en caso de error"),
                L("Ya existe handler. Para IA conviene usarlo con frases explícitas de error."),
                L("No usar como catch global automático sin que el usuario lo pida."),
                L("wf.error", "wf.error.message", "wf.error.nodeId", "wf.error.nodeType")));

            list.Add(Node("util.subflow", "Subflujo", "Utilidad / Operación", true, EstadoParcial, 2,
                L("ref"), L("input", "as", "maxDepth", "usuario"),
                L("ejecutar otro workflow", "llamar subflujo", "correr subproceso", "usar workflow"),
                L("Validado previamente. Requiere resolver bien la definición referenciada."),
                L("ref obligatorio", "no inventar ref", "respetar maxDepth"),
                L("subflow.instanceId", "subflow.childState", "subflow.ref", "subflow.estado", "subflow.logs")));

            list.Add(Node("human.task", "Tarea humana", "Utilidad / Operación", true, EstadoListo, 1,
                L("rol|usuarioAsignado", "titulo"), L("descripcion", "scopeKey", "deadlineMinutes", "estadoNegocioPendiente"),
                L("enviar a", "mandar tarea", "asignar a", "que revise", "que apruebe", "que valide", "para corregir", "para aprobar"),
                L("Crea una tarea real. Debe distinguirse de util.notify."),
                L("rol o usuarioAsignado obligatorio", "si hay resultado APTO/NO APTO debe haber dos ramas distintas"),
                L("wf.tarea.id", "wf.tarea.resultado", "wf.tarea.destino")));

            // Control
            list.Add(Node("control.if", "Condición (If)", "Control", true, EstadoListo, 1,
                null, L("field", "op", "value", "expression", "transform", "rulesMode", "rules"),
                L("si", "cuando", "en caso de", "si cumple", "caso contrario", "si no", "falta", "no tiene", "todas", "cualquiera"),
                L("Soporta condición simple y compuesta ALL/ANY."),
                L("Debe tener rama true y false", "true y false no pueden ir al mismo destino", "rules debe ser array si se usa condición compuesta"),
                null));

            list.Add(Node("control.switch", "Selector (Switch)", "Control", true, EstadoRestringido, 4,
                L("field|expression", "casos"), L("default"),
                L("según", "dependiendo de", "si es A/B/C", "selector", "switch"),
                L("Existe handler. Para IA libre requiere validación de todos los casos."),
                L("casos obligatorio", "default recomendado", "no generar si hay solo dos ramas: usar control.if"),
                null));

            list.Add(Node("control.parallel", "Paralelo (Split)", "Control", true, EstadoRestringido, 5,
                null, L("branches", "mode"),
                L("en paralelo", "hacer al mismo tiempo", "dividir en paralelo"),
                L("Existe handler. Requiere diseño cuidadoso con join."),
                L("No generar sin join o estrategia clara de convergencia."),
                null));

            list.Add(Node("control.join", "Unir (Join)", "Control", true, EstadoRestringido, 5,
                null, L("mode", "waitFor"),
                L("unir", "esperar ambas", "juntar ramas", "continuar cuando terminen"),
                L("Existe handler junto a HParallel. Requiere split previo."),
                L("No generar sin ramas paralelas previas."),
                null));

            list.Add(Node("control.loop", "Bucle (Loop/ForEach)", "Control", true, EstadoRestringido, 5,
                L("forEach|items"), L("itemVar", "max"),
                L("por cada", "para cada", "recorrer", "iterar", "bucle"),
                L("Existe handler. Para IA libre requiere prevenir loops peligrosos."),
                L("max recomendado", "items/forEach obligatorio", "no generar loops infinitos"),
                null));

            list.Add(Node("control.delay", "Demora (Delay)", "Control", true, EstadoParcial, 2,
                L("ms|seconds"), L("message"),
                L("esperar", "demorar", "pausar", "esperar unos segundos", "delay"),
                L("Validado como nodo simple."),
                L("Debe indicar duración o asumir valor seguro si la frase es clara."),
                null));

            list.Add(Node("control.retry", "Reintentar", "Control", true, EstadoParcial, 2,
                null, L("reintentos", "backoffMs", "message"),
                L("reintentar", "volver a intentar", "intentar de nuevo", "hasta tres veces"),
                L("Validado como nodo. Para IA debe asociarse al paso correcto."),
                L("Debe tener objetivo claro; no reintentar todo el workflow por defecto."),
                null));

            list.Add(Node("control.ratelimit", "Límite de tasa", "Control", true, EstadoRestringido, 5,
                L("key|limit"), L("periodSeconds", "message"),
                L("limitar tasa", "no más de", "rate limit", "máximo por minuto"),
                L("Handler existe. Uso operativo menos frecuente."),
                L("No generar si la frase no indica límite concreto."),
                null));

            // Datos e Integraciones
            list.Add(Node("http.request", "Solicitud HTTP", "Datos e Integraciones", true, EstadoParcial, 2,
                L("method", "url"), L("headers", "query", "body", "contentType", "timeoutMs", "failOnStatus", "failStatusMin"),
                L("llamar API", "consultar servicio", "hacer GET", "hacer POST", "solicitud HTTP", "webhook"),
                L("Ya incorporado al Constructor en casos simples."),
                L("method y url obligatorios", "no inventar URLs", "validar credenciales fuera de frase libre"),
                L("payload.status", "payload.body", "payload.json")));

            list.Add(Node("data.sql", "Consulta SQL", "Datos e Integraciones", true, EstadoParcial, 2,
                L("query|commandText"), L("connectionStringName", "parameters", "params", "maxRows"),
                L("consultar SQL", "buscar en base", "ejecutar consulta", "traer datos", "SELECT"),
                L("Ya incorporado al Constructor en casos simples."),
                L("SQL obligatorio", "DefaultConnection por defecto", "distinguir SELECT de comandos de escritura"),
                L("sql.rows", "sql.rowCount", "sql.first", "sql.scalar", "sql.rowsAffected", "sql.error")));

            list.Add(Node("data.redis.get", "Cache GET (Redis)", "Datos e Integraciones", false, EstadoNoHabilitado, 9,
                L("key"), null,
                L("leer de cache", "buscar en redis", "obtener cache"),
                L("Visible en catálogo, pero no se encontró handler runtime equivalente en esta base."),
                L("No habilitar para IA hasta tener handler real."),
                null));

            list.Add(Node("data.redis.set", "Cache SET (Redis)", "Datos e Integraciones", false, EstadoNoHabilitado, 9,
                L("key", "value"), L("ttlSeconds"),
                L("guardar en cache", "escribir en redis", "setear cache"),
                L("Visible en catálogo, pero no se encontró handler runtime equivalente en esta base."),
                L("No habilitar para IA hasta tener handler real."),
                null));

            list.Add(Node("file.read", "Archivo: Leer", "Datos e Integraciones", true, EstadoParcial, 2,
                L("path"), L("salida", "output", "asJson", "encoding", "zipMode", "zipEntry", "useCache"),
                L("leer archivo", "abrir archivo", "leer JSON", "leer texto", "leer zip"),
                L("Validado. Requiere rutas controladas."),
                L("path obligatorio", "evitar rutas no permitidas", "definir salida si no es archivo por defecto"),
                L("archivo", "file.read.lastPath", "file.read.lastLength", "file.read.lastError")));

            list.Add(Node("file.write", "Archivo: Escribir", "Datos e Integraciones", true, EstadoParcial, 2,
                L("path", "content|origen"), L("encoding", "overwrite", "zipMode", "entryName", "zipEntryName"),
                L("escribir archivo", "guardar archivo", "crear archivo", "exportar", "generar txt", "generar json"),
                L("Validado. Requiere rutas controladas."),
                L("path obligatorio", "content u origen obligatorio", "overwrite explícito recomendado"),
                L("file.write.lastPath", "file.write.lastLength", "file.write.lastError")));

            list.Add(Node("email.send", "Correo: Enviar", "Datos e Integraciones", true, EstadoRestringido, 6,
                L("to", "subject", "body"), L("from", "cc", "bcc", "html", "modo", "useWebConfig", "isHtml"),
                L("enviar correo", "mandar mail", "email", "avisar por correo"),
                L("Existe handler, pero SMTP/email.send está fuera de foco salvo pedido explícito de Omar."),
                L("No habilitar agresivamente en IA libre", "to/subject/body obligatorios"),
                L("email.lastError")));

            list.Add(Node("chat.notify", "Chat/Slack", "Datos e Integraciones", true, EstadoParcial, 4,
                L("mensaje"), L("canal", "webhookUrl"),
                L("avisar por chat", "notificar por slack", "enviar mensaje al canal"),
                L("Handler existe. Requiere configuración concreta de canal/webhook."),
                L("mensaje obligatorio", "canal o webhookUrl obligatorio"),
                null));

            list.Add(Node("cloud.storage", "Almacenamiento Cloud", "Datos e Integraciones", false, EstadoNoHabilitado, 9,
                null, null,
                L("subir a cloud", "guardar en nube", "storage"),
                L("Visible en catálogo, pero no se encontró handler runtime en esta base."),
                L("No habilitar para IA hasta tener handler real."),
                null));

            list.Add(Node("queue.publish", "Cola: Publicar", "Datos e Integraciones", true, EstadoParcial, 3,
                L("queue"), L("broker", "payload", "correlationId", "dueAt", "priority", "connectionStringName"),
                L("publicar en cola", "encolar", "mandar a cola", "queue publish"),
                L("Validado recientemente con WF_Queue."),
                L("queue obligatoria o default explícito", "payload recomendado"),
                L("queue.publish.lastId", "queue.error")));

            list.Add(Node("queue.consume", "Cola: Consumir", "Datos e Integraciones", true, EstadoParcial, 3,
                L("queue"), L("take", "connectionStringName", "outputPrefix"),
                L("consumir cola", "leer de cola", "tomar mensaje", "queue consume"),
                L("Validado recientemente con WF_Queue."),
                L("queue obligatoria o default explícito", "take seguro"),
                L("payload", "queue.consume.lastId", "queue.error")));

            list.Add(Node("ftp.get", "FTP: Descargar", "Datos e Integraciones", true, EstadoRestringido, 6,
                L("url|host", "remotePath", "localPath"), L("user", "password", "port", "secure"),
                L("descargar por ftp", "traer archivo ftp", "bajar de ftp"),
                L("Handler existe. Requiere credenciales/configuración segura."),
                L("No generar sin credenciales/config key claras", "no exponer secretos en frase"),
                null));

            list.Add(Node("ftp.put", "FTP: Subir", "Datos e Integraciones", true, EstadoRestringido, 6,
                L("url|host", "localPath", "remotePath"), L("user", "password", "port", "secure"),
                L("subir por ftp", "enviar archivo ftp", "poner en ftp"),
                L("Handler existe. Requiere credenciales/configuración segura."),
                L("No generar sin credenciales/config key claras", "no exponer secretos en frase"),
                null));

            list.Add(Node("doc.entrada", "Entrada de documento", "Datos e Integraciones", true, EstadoParcial, 3,
                null, L("entrada"),
                L("recibir documento", "entrada de documento", "datos del formulario"),
                L("Handler simple para volcar entrada al contexto."),
                L("entrada recomendada"),
                L("documento", "documento.tieneFirma")));

            list.Add(Node("doc.load", "Documento: Cargar archivo", "Datos e Integraciones", true, EstadoListo, 1,
                L("path", "docTipoCodigo"), L("mode", "outputPrefix", "connectionStringName"),
                L("cargar documento", "cargar nota de crédito", "cargar factura", "leer documento", "procesar archivo"),
                L("Muy trabajado y validado con NC_AR/FACTURA_AR."),
                L("path obligatorio", "docTipoCodigo obligatorio si la frase identifica tipo documental"),
                L("input.text", "input.hasText", "wf.docTipoCodigo", "biz.*")));

            list.Add(Node("doc.search", "Documento: Buscar (DMS)", "Datos e Integraciones", true, EstadoParcial, 4,
                null, L("criteria", "query", "max", "viewerUrlTemplate", "outputPrefix"),
                L("buscar documento", "buscar en DMS", "encontrar expediente", "traer documentos"),
                L("Handler existe. Requiere acordar frases y criterios de búsqueda."),
                L("No generar sin criterio claro."),
                L("doc.search.count", "doc.search.items")));

            list.Add(Node("doc.attach", "Documento: Adjuntar (DMS)", "Datos e Integraciones", true, EstadoParcial, 4,
                L("doc|docJson"), L("rootKey", "tareaId"),
                L("adjuntar documento", "agregar documento al expediente", "vincular archivo"),
                L("Handler existe. Requiere documento origen claro."),
                L("doc o docJson obligatorio"),
                null));

            // Transformación y Lógica
            list.Add(Node("transform.map", "Transformar (Mapeo)", "Transformación y Lógica", true, EstadoParcial, 4,
                L("map"), L("source", "target", "mode"),
                L("mapear", "transformar datos", "convertir campos", "armar payload"),
                L("Handler existe. Es clave para payloads HTTP/colas, pero necesita diseño de slots."),
                L("map obligatorio", "no inventar campos críticos"),
                null));

            list.Add(Node("code.function", "Función (C#)", "Transformación y Lógica", true, EstadoRestringido, 8,
                L("name"), L("input", "output"),
                L("ejecutar función", "función C#", "calcular con función"),
                L("Handler existe con registry. Debe limitarse a funciones registradas."),
                L("name obligatorio", "name debe existir en CodeFunctionRegistry", "no generar código libre"),
                null));

            list.Add(Node("code.script", "Script (JS)", "Transformación y Lógica", true, EstadoRestringido, 9,
                L("script"), L("input", "output"),
                L("ejecutar script", "script JS", "código javascript"),
                L("Handler existe, pero debe estar explícitamente habilitado por configuración."),
                L("No habilitar desde frase libre salvo entorno controlado", "WF_EnableCodeScript debe estar true"),
                null));

            list.Add(Node("state.vars", "Variables", "Transformación y Lógica", true, EstadoParcial, 2,
                null, L("set", "remove"),
                L("guardar variable", "setear variable", "quitar variable", "borrar variable", "guardar estado"),
                L("Validado con set/remove y objetos JSON."),
                L("set o remove obligatorio"),
                L("state.vars", "vars.*")));

            list.Add(Node("config.secrets", "Secretos/Claves", "Transformación y Lógica", true, EstadoRestringido, 8,
                L("key"), L("as", "required"),
                L("usar secreto", "leer clave", "obtener credencial", "usar token"),
                L("Handler existe. Debe usarse para no exponer secretos en texto."),
                L("key obligatorio", "no permitir valores secretos escritos por frase libre"),
                null));

            list.Add(Node("ai.call", "AI / LLM", "Transformación y Lógica", true, EstadoRestringido, 9,
                L("prompt"), L("provider", "model", "input", "output", "temperature", "maxTokens"),
                L("llamar IA", "analizar con IA", "pedir a LLM", "clasificar con IA"),
                L("Handler existe, pero el producto principal debe funcionar offline/intranet."),
                L("No depender de proveedores externos", "usar solo si la configuración local/interna está resuelta"),
                L("ai.lastText", "ai.lastJson")));

            list.Add(Node("doc.extract", "Extraer de texto", "Transformación y Lógica", true, EstadoParcial, 3,
                null, L("origen", "source", "destino", "mode", "regex", "fields", "docTipoCodigo"),
                L("extraer datos", "sacar campos", "leer campos del texto", "extraer de texto"),
                L("Handler existe. Puede ser importante para doc tipos nuevos."),
                L("Debe indicar origen y campos/reglas si no usa docTipo."),
                L("doc.extract.lastDestino", "doc.extract.lastMode", "doc.extract.lastMatches", "doc.extract.lastError")));

            return list;
        }

        public static WfAiNodeCapability Find(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType)) return null;

            var list = Build();
            foreach (var item in list)
            {
                if (string.Equals(item.NodeType, nodeType, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        public static List<WfAiNodeCapability> ForAiPriority(int maxPriority)
        {
            var output = new List<WfAiNodeCapability>();
            foreach (var item in Build())
            {
                if (item.AiPriority <= maxPriority)
                    output.Add(item);
            }
            return output;
        }

        private static WfAiNodeCapability Node(
            string nodeType,
            string label,
            string group,
            bool runtimeHandlerFound,
            string aiStatus,
            int aiPriority,
            List<string> requiredParams,
            List<string> optionalParams,
            List<string> humanPhrases,
            List<string> notes,
            List<string> validations,
            List<string> outputFields)
        {
            return new WfAiNodeCapability
            {
                NodeType = nodeType,
                Label = label,
                Group = group,
                RuntimeHandlerFound = runtimeHandlerFound,
                AiStatus = aiStatus,
                AiPriority = aiPriority,
                RequiredParams = requiredParams ?? new List<string>(),
                OptionalParams = optionalParams ?? new List<string>(),
                HumanPhrases = humanPhrases ?? new List<string>(),
                Notes = notes ?? new List<string>(),
                RequiredValidations = validations ?? new List<string>(),
                OutputFields = outputFields ?? new List<string>()
            };
        }

        private static List<string> L(params string[] values)
        {
            return new List<string>(values ?? new string[0]);
        }
    }

    public class WfAiNodeCapability
    {
        public string NodeType { get; set; }
        public string Label { get; set; }
        public string Group { get; set; }
        public bool RuntimeHandlerFound { get; set; }
        public string AiStatus { get; set; }
        public int AiPriority { get; set; }
        public List<string> RequiredParams { get; set; }
        public List<string> OptionalParams { get; set; }
        public List<string> HumanPhrases { get; set; }
        public List<string> Notes { get; set; }
        public List<string> RequiredValidations { get; set; }
        public List<string> OutputFields { get; set; }

        public WfAiNodeCapability()
        {
            RequiredParams = new List<string>();
            OptionalParams = new List<string>();
            HumanPhrases = new List<string>();
            Notes = new List<string>();
            RequiredValidations = new List<string>();
            OutputFields = new List<string>();
        }
    }
}
