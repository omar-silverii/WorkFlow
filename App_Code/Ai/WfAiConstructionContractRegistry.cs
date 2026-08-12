using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix84a: primera muestra ejecutable del contrato universal.
    /// La cobertura se amplía de forma incremental sobre los nodos ya cerrados; FIX84C2D4 incorpora state.vars.
    /// El registro describe intención, parámetros, inferencia, controles, salidas y validaciones;
    /// no reemplaza handlers, catálogo, Phrase Engine ni WfAiMlnetProvider.
    /// </summary>
    public static class WfAiConstructionContractRegistry
    {
        public const string Version = "fix84c2d4-contract-v10";

        public static List<WfAiNodeConstructionContract> Build()
        {
            return new List<WfAiNodeConstructionContract>
            {
                QueuePublish(),
                QueueConsume(),
                Logger(),
                HumanTask(),
                ControlIf(),
                Notify(),
                FileWrite(),
                FileRead(),
                StateVars()
            };
        }

        public static WfAiNodeConstructionContract Find(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType)) return null;
            foreach (WfAiNodeConstructionContract item in Build())
            {
                if (item != null && string.Equals(item.NodeType, nodeType, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        public static List<string> CoveredNodeTypes()
        {
            var result = new List<string>();
            foreach (WfAiNodeConstructionContract item in Build())
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.NodeType))
                    result.Add(item.NodeType);
            }
            return result;
        }

        /// <summary>
        /// Defensa de integración: comprueba que el contrato no declare parámetros inexistentes
        /// en el catálogo que usa hoy WfAiPlanValidator. No modifica ese catálogo.
        /// </summary>
        public static List<string> ValidateAgainstCatalog(WfAiCatalog catalog)
        {
            var errors = new List<string>();
            var seenNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WfAiNodeConstructionContract contract in Build())
            {
                if (contract == null || string.IsNullOrWhiteSpace(contract.NodeType))
                {
                    errors.Add("Existe un contrato fix84a sin nodeType.");
                    continue;
                }

                if (!seenNodes.Add(contract.NodeType))
                    errors.Add("Contrato duplicado para nodeType: " + contract.NodeType);

                WfAiNodeInfo catalogNode = FindCatalogNode(catalog, contract.NodeType);
                if (catalogNode == null)
                {
                    errors.Add("El contrato declara un nodo que no existe en WfAiCatalogProvider: " + contract.NodeType);
                    continue;
                }

                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in catalogNode.Params ?? new List<string>())
                    allowed.Add(p);

                var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (WfAiParameterContract parameter in contract.Parameters)
                {
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        errors.Add("El contrato " + contract.NodeType + " tiene un parámetro sin nombre.");
                        continue;
                    }

                    if (!seenParams.Add(parameter.Name))
                        errors.Add("Parámetro duplicado en contrato " + contract.NodeType + ": " + parameter.Name);

                    if (!allowed.Contains(parameter.Name))
                        errors.Add("El contrato " + contract.NodeType + " declara un parámetro no permitido por catálogo: " + parameter.Name);
                }

                foreach (WfAiAlternativeRequirement requirement in contract.AlternativeRequirements)
                {
                    if (requirement == null) continue;
                    foreach (List<string> alternative in requirement.Alternatives)
                    {
                        foreach (string name in alternative ?? new List<string>())
                        {
                            if (!seenParams.Contains(name))
                                errors.Add("El requisito " + requirement.Key + " de " + contract.NodeType + " referencia un parámetro no declarado: " + name);
                        }
                    }
                }
            }

            return errors;
        }

        private static WfAiNodeInfo FindCatalogNode(WfAiCatalog catalog, string nodeType)
        {
            if (catalog == null || catalog.Nodes == null) return null;
            foreach (WfAiNodeInfo node in catalog.Nodes)
            {
                if (node != null && string.Equals(node.Type, nodeType, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            return null;
        }

        private static WfAiNodeConstructionContract QueuePublish()
        {
            return new WfAiNodeConstructionContract
            {
                NodeType = "queue.publish",
                Label = "Cola: Publicar",
                HumanIntent = "Publicar un mensaje de negocio en una cola existente.",
                HumanPhrases = L("publicar en una cola", "publicar", "mandar un mensaje a una cola", "encolar un mensaje"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("broker", "Broker", "string", false, WfAiInferencePolicy.SafeDefault, J("sql"), "", WfAiControlKind.Select, L("sql"), false),
                    P("queue", "Cola", "string", true, WfAiInferencePolicy.NeverInfer, null, "¿En qué cola querés publicar?", WfAiControlKind.Text, null, true, L("default", "banco-regresion")),
                    P("payload", "Contenido del mensaje", "object", true, WfAiInferencePolicy.AskIfMissing, null, "¿Qué información querés publicar en la cola?", WfAiControlKind.PayloadEditor, null, true, L("Mensaje generado por Constructor IA", "Mensaje generado por Asistente IA")),
                    P("connectionStringName", "Conexión", "string", false, WfAiInferencePolicy.SafeDefault, J("DefaultConnection"), "", WfAiControlKind.Select, L("DefaultConnection"), false),
                    P("correlationId", "CorrelationId", "string", false, WfAiInferencePolicy.SafeDefault, null, "", WfAiControlKind.AvailableData, null, false),
                    P("dueAt", "Disponible desde", "datetime", false, WfAiInferencePolicy.AskIfMissing, null, "", WfAiControlKind.Text, null, false),
                    P("priority", "Prioridad", "number", false, WfAiInferencePolicy.SafeDefault, new JValue(0), "", WfAiControlKind.Number, null, false)
                },
                InputFields = L("wf.instanceId", "Datos disponibles del workflow"),
                OutputFields = L("queue.last", "queue.last.queue", "queue.last.id", "queue.last.correlationId", "queue.last.payload", "queue.error"),
                DynamicOutputPrefixes = L("queue.last.payload."),
                AmbiguityRules = new List<WfAiAmbiguityRule>
                {
                    new WfAiAmbiguityRule
                    {
                        Key = "createQueueMeaning",
                        PhraseFragments = L("crear una cola", "crear cola"),
                        Question = "Cuando decís crear una cola, ¿querés usar una cola existente dentro del workflow o realmente crear la infraestructura de la cola?",
                        ControlKind = WfAiControlKind.Select,
                        Options = L("Usar una cola existente", "Crear infraestructura de cola", "Dejar la creación fuera del workflow"),
                        Blocking = true,
                        ContextResolverKey = "queue.create.followed_by_publish"
                    }
                },
                Validations = L("queue no debe inventarse", "payload conserva texto simple, objeto o arreglo según lo expresado", "connectionStringName usa DefaultConnection salvo decisión explícita"),
                SummaryTemplate = "Publicar en la cola {queue}."
            };
        }

        private static WfAiNodeConstructionContract QueueConsume()
        {
            return new WfAiNodeConstructionContract
            {
                NodeType = "queue.consume",
                Label = "Cola: Consumir",
                HumanIntent = "Tomar mensajes pendientes de una cola existente.",
                HumanPhrases = L("consumir una cola", "leer mensajes de una cola", "tomar un mensaje de una cola"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("broker", "Broker", "string", false, WfAiInferencePolicy.SafeDefault, J("sql"), "", WfAiControlKind.Select, L("sql"), false),
                    P("queue", "Cola", "string", true, WfAiInferencePolicy.NeverInfer, null, "¿De qué cola querés consumir mensajes?", WfAiControlKind.Text, null, true, L("default", "banco-regresion")),
                    P("take", "Cantidad", "number", false, WfAiInferencePolicy.SafeDefault, new JValue(1), "", WfAiControlKind.Number, null, false),
                    P("prefetch", "Prefetch", "number", false, WfAiInferencePolicy.SafeDefault, new JValue(1), "", WfAiControlKind.Number, null, false, null, "take"),
                    P("connectionStringName", "Conexión", "string", false, WfAiInferencePolicy.SafeDefault, J("DefaultConnection"), "", WfAiControlKind.Select, L("DefaultConnection"), false),
                    P("outputPrefix", "Prefijo de salida", "string", false, WfAiInferencePolicy.SafeDefault, J("queue.consume"), "", WfAiControlKind.Text, null, false),
                    P("debug", "Debug", "boolean", false, WfAiInferencePolicy.SafeDefault, new JValue(false), "", WfAiControlKind.Boolean, null, false)
                },
                OutputFields = L("queue.hasMessage", "queue.messages", "queue.message", "queue.messageId", "queue", "payload", "payload.raw", "queue.error"),
                DynamicOutputPrefixes = L("payload.", "queue.message."),
                Validations = L("queue no debe inventarse", "take mínimo 1", "prefetch se deriva de take durante la construcción", "DefaultConnection es la conexión estándar actual"),
                SummaryTemplate = "Consumir mensajes de la cola {queue}."
            };
        }

        private static WfAiNodeConstructionContract Logger()
        {
            return new WfAiNodeConstructionContract
            {
                NodeType = "util.logger",
                Label = "Logger",
                HumanIntent = "Registrar información operativa en el log de la instancia.",
                HumanPhrases = L("registrar un log", "dejar constancia", "registrar un evento", "registrar una advertencia"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("message", "Mensaje", "string", true, WfAiInferencePolicy.NeverInfer, null, "¿Qué querés registrar en el log?", WfAiControlKind.Text, null, true, L("Paso agregado por Asistente IA")),
                    P("level", "Nivel", "string", false, WfAiInferencePolicy.SafeDefault, J("Info"), "", WfAiControlKind.Select, L("Info", "Warn", "Error", "Debug"), false)
                },
                InputFields = L("Cualquier dato disponible mediante ${...}"),
                OutputFields = L("logger.last.level", "logger.last.message", "logger.last.nodeId"),
                Validations = L("message obligatorio", "level permitido: Info/Warn/Error/Debug"),
                SummaryTemplate = "Registrar {level}: {message}."
            };
        }

        private static WfAiNodeConstructionContract HumanTask()
        {
            var contract = new WfAiNodeConstructionContract
            {
                NodeType = "human.task",
                Label = "Tarea humana",
                HumanIntent = "Crear una tarea real para que una persona o rol revise, apruebe o complete una acción.",
                HumanPhrases = L("mandar a revisar", "crear una tarea", "que apruebe", "asignar a", "enviar a compras"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("rol", "Rol", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿A qué rol querés asignar la tarea?", WfAiControlKind.Select, null, true),
                    P("usuarioAsignado", "Usuario", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿A qué usuario querés asignar la tarea?", WfAiControlKind.Select, null, true),
                    P("titulo", "Título", "string", true, WfAiInferencePolicy.VisibleInference, null, "¿Qué título querés para la tarea?", WfAiControlKind.Text, null, false, L("Tarea humana")),
                    P("descripcion", "Descripción", "string", false, WfAiInferencePolicy.NeverInfer, null, "", WfAiControlKind.Text, null, false, L("Revisión generada por el Asistente IA.", "Tarea generada por el Constructor IA: revisión")),
                    P("deadlineMinutes", "Vencimiento", "number", false, WfAiInferencePolicy.SafeDefault, new JValue(0), "", WfAiControlKind.Number, null, false),
                    P("estadoNegocioPendiente", "Estado pendiente", "string", false, WfAiInferencePolicy.VisibleInference, null, "", WfAiControlKind.Text, null, false)
                },
                InputFields = L("input.scopeKey", "input.sector", "input.Sector"),
                OutputFields = L("wf.tarea.id", "wf.tarea.estado", "wf.tarea.resultado", "wf.tarea.{nodeId}.resultado"),
                Validations = L("debe existir exactamente un destino: rol O usuarioAsignado", "no inventar destinatario", "scope se toma del contexto de entrada", "APTO/NO APTO requiere ramas distintas"),
                SummaryTemplate = "Crear tarea humana para {destino}: {titulo}."
            };

            contract.AlternativeRequirements.Add(new WfAiAlternativeRequirement
            {
                Key = "taskDestination",
                Label = "Destino de la tarea",
                Alternatives = new List<List<string>> { L("rol"), L("usuarioAsignado") },
                ClarificationQuestion = "¿Quién debe recibir la tarea?",
                ControlKind = WfAiControlKind.RoleOrUser,
                Blocking = true,
                Exclusive = true
            });

            return contract;
        }

        private static WfAiNodeConstructionContract Notify()
        {
            var contract = new WfAiNodeConstructionContract
            {
                NodeType = "util.notify",
                Label = "Notificar",
                HumanIntent = "Crear una notificación operativa interna para un rol o un usuario real.",
                HumanPhrases = L("notificar internamente", "avisar por sistema", "crear una notificación interna", "notificar a compras"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("tipo", "Tipo", "string", false, WfAiInferencePolicy.SafeDefault, J("sistema"), "", WfAiControlKind.Select, L("sistema"), false),
                    P("canal", "Canal", "string", false, WfAiInferencePolicy.SafeDefault, J("sistema"), "", WfAiControlKind.Select, L("sistema"), false),
                    P("nivel", "Nivel", "string", false, WfAiInferencePolicy.SafeDefault, J("info"), "", WfAiControlKind.Select, L("info", "warn", "error", "debug"), false),
                    P("destinoTipo", "Tipo de destino", "string", false, WfAiInferencePolicy.NeverInfer, null, "", WfAiControlKind.Select, L("rol", "usuario"), false),
                    P("usuarioDestino", "Usuario destino", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿A qué usuario querés notificar?", WfAiControlKind.Select, null, true),
                    P("rolDestino", "Rol destino", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿A qué rol querés notificar?", WfAiControlKind.Select, null, true),
                    P("destino", "Destino", "string", false, WfAiInferencePolicy.NeverInfer, null, "", WfAiControlKind.Text, null, false),
                    P("prioridad", "Prioridad", "string", false, WfAiInferencePolicy.SafeDefault, J("normal"), "", WfAiControlKind.Select, L("normal", "alta", "baja"), false),
                    P("asunto", "Asunto", "string", false, WfAiInferencePolicy.SafeDefault, J("Notificación"), "", WfAiControlKind.Text, null, false, L("Notificación Workflow Studio", "Aviso interno")),
                    P("mensaje", "Mensaje", "string", true, WfAiInferencePolicy.NeverInfer, null, "¿Qué mensaje querés enviar en la notificación?", WfAiControlKind.Text, null, true, L("Notificación interna generada por Asistente IA.", "Notificación generada por Asistente IA.", "Hay una novedad pendiente en el workflow")),
                    P("urlAccion", "URL de acción", "string", false, WfAiInferencePolicy.SafeDefault, null, "", WfAiControlKind.Text, null, false)
                },
                InputFields = L("Cualquier dato disponible mediante ${...}"),
                OutputFields = L(
                    "notify.last.id", "notify.last.persisted", "notify.last.error",
                    "notify.last.type", "notify.last.canal", "notify.last.requestedCanal",
                    "notify.last.nivel", "notify.last.prioridad", "notify.last.destino",
                    "notify.last.usuarioDestino", "notify.last.rolDestino", "notify.last.title",
                    "notify.last.message", "notify.last.urlAccion"),
                Validations = L(
                    "debe existir exactamente un destino: rolDestino O usuarioDestino",
                    "no inventar destinatario",
                    "mensaje obligatorio",
                    "util.notify es notificación interna; para correo real usar email.send",
                    "tipo/canal sistema, nivel info/warn/error/debug y prioridad normal/alta/baja"),
                SummaryTemplate = "Notificar a {destino}: {mensaje}."
            };

            contract.AlternativeRequirements.Add(new WfAiAlternativeRequirement
            {
                Key = "notificationDestination",
                Label = "Destino de la notificación",
                Alternatives = new List<List<string>> { L("rolDestino"), L("usuarioDestino") },
                ClarificationQuestion = "¿Quién debe recibir la notificación interna?",
                ControlKind = WfAiControlKind.RoleOrUser,
                Blocking = true,
                Exclusive = true
            });

            return contract;
        }

        private static WfAiNodeConstructionContract FileWrite()
        {
            var contract = new WfAiNodeConstructionContract
            {
                NodeType = "file.write",
                Label = "Archivo: Escribir",
                HumanIntent = "Escribir texto, una plantilla o un dato disponible en una ruta de archivo elegida por el usuario.",
                HumanPhrases = L("escribir un archivo", "guardar un archivo", "escribir un dato en un archivo", "guardar texto en una ruta"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("path", "Ruta destino", "string", true, WfAiInferencePolicy.NeverInfer, null,
                        "¿En qué ruta querés escribir el archivo?", WfAiControlKind.Text, null, true,
                        L(@"C:\temp\wf_ai_regression.txt", @"C:\temp\out.txt", "C:/data/salida.json")),
                    P("content", "Contenido", "string", false, WfAiInferencePolicy.NeverInfer, null,
                        "¿Qué contenido querés escribir?", WfAiControlKind.Text, null, true,
                        L("Contenido generado por Asistente IA", "Contenido generado por Constructor IA")),
                    P("origen", "Dato de origen", "string", false, WfAiInferencePolicy.NeverInfer, null,
                        "¿Qué dato disponible querés escribir?", WfAiControlKind.AvailableData, null, true),
                    P("encoding", "Encoding", "string", false, WfAiInferencePolicy.SafeDefault, J("utf-8"), "", WfAiControlKind.Text, null, false),
                    P("overwrite", "Sobrescribir", "boolean", false, WfAiInferencePolicy.SafeDefault, new JValue(true), "", WfAiControlKind.Boolean, null, false),
                    P("zipMode", "Modo ZIP", "string", false, WfAiInferencePolicy.SafeDefault, J("none"), "", WfAiControlKind.Select, L("none", "zip"), false),
                    P("entryName", "Entrada ZIP", "string", false, WfAiInferencePolicy.AskIfMissing, null, "", WfAiControlKind.Text, null, false)
                },
                InputFields = L("Datos disponibles del workflow mediante ${...} o una clave de contexto"),
                OutputFields = L(
                    "file.write.lastPath", "file.write.lastLength", "file.write.lastEncoding",
                    "file.write.lastZipMode", "file.write.lastEntryName", "file.write.skipped", "file.write.lastError"),
                Validations = L(
                    "path es obligatorio y nunca se inventa",
                    "debe existir content u origen",
                    "content, path y origen son datos declarativos y no nuevas intenciones",
                    "encoding utf-8, overwrite true y zipMode none son defaults técnicos seguros"),
                SummaryTemplate = "Escribir un archivo en {path}."
            };

            contract.AlternativeRequirements.Add(new WfAiAlternativeRequirement
            {
                Key = "fileWriteSource",
                Label = "Contenido a escribir",
                Alternatives = new List<List<string>> { L("content"), L("origen") },
                ClarificationQuestion = "¿Qué contenido querés escribir? Podés escribir texto o insertar un dato disponible.",
                ControlKind = WfAiControlKind.Text,
                Blocking = true,
                Exclusive = false
            });

            return contract;
        }

        private static WfAiNodeConstructionContract FileRead()
        {
            return new WfAiNodeConstructionContract
            {
                NodeType = "file.read",
                Label = "Archivo: Leer",
                HumanIntent = "Leer un archivo del servidor y dejar su contenido disponible en el contexto del workflow.",
                HumanPhrases = L("leer un archivo", "abrir un archivo", "leer archivo y guardar el contenido", "leer archivo como JSON"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("path", "Ruta del archivo", "string", true, WfAiInferencePolicy.NeverInfer, null,
                        "¿Qué archivo querés leer? Indicá la ruta del servidor.", WfAiControlKind.Text, null, true,
                        L(@"C:\Temp\entrada.txt", @"C:\temp\input.json")),
                    P("salida", "Guardar contenido en", "string", false, WfAiInferencePolicy.SafeDefault, J("archivo"),
                        "", WfAiControlKind.Text, null, false),
                    P("asJson", "Leer como JSON", "boolean", false, WfAiInferencePolicy.SafeDefault, new JValue(false),
                        "", WfAiControlKind.Boolean, null, false),
                    P("encoding", "Encoding", "string", false, WfAiInferencePolicy.SafeDefault, J("utf-8"),
                        "", WfAiControlKind.Text, null, false),
                    P("zipMode", "Compresión", "string", false, WfAiInferencePolicy.SafeDefault, J("auto"),
                        "", WfAiControlKind.Select, L("auto", "none", "zip", "gzip"), false),
                    P("zipEntry", "Entrada ZIP", "string", false, WfAiInferencePolicy.AskIfMissing, null,
                        "", WfAiControlKind.Text, null, false),
                    P("useCache", "Usar caché al reanudar", "boolean", false, WfAiInferencePolicy.SafeDefault, new JValue(true),
                        "", WfAiControlKind.Boolean, null, false)
                },
                InputFields = L("input.filePath", "Una ruta literal o una plantilla ${...} que resuelva a una ruta"),
                OutputFields = L(
                    "archivo (o la salida elegida)", "file.read.lastPath", "file.read.lastLength",
                    "file.read.lastEncoding", "file.read.lastZipMode", "file.read.lastAsJson",
                    "file.read.exists", "file.read.lastError"),
                Validations = L(
                    "path es obligatorio y nunca se inventa",
                    "output es alias legacy; el contrato canónico usa salida",
                    "salida debe ser una clave válida de contexto y no debe pisar file.read.*",
                    "salida archivo, asJson false, encoding utf-8, zipMode auto y useCache true son defaults técnicos seguros",
                    "path, salida y zipEntry son datos declarativos y no nuevas intenciones"),
                SummaryTemplate = "Leer el archivo {path} y guardar su contenido en {salida}."
            };
        }

        private static WfAiNodeConstructionContract StateVars()
        {
            var contract = new WfAiNodeConstructionContract
            {
                NodeType = "state.vars",
                Label = "Variables",
                HumanIntent = "Guardar y/o quitar variables del contexto del workflow.",
                HumanPhrases = L("guardar una variable", "definir una variable", "asignar una variable", "quitar una variable", "eliminar una variable"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("set", "Variables a guardar", "object", false, WfAiInferencePolicy.NeverInfer, null,
                        "", WfAiControlKind.PayloadEditor, null, false),
                    P("remove", "Variables a quitar", "array", false, WfAiInferencePolicy.NeverInfer, null,
                        "", WfAiControlKind.Text, null, false)
                },
                InputFields = L("Datos disponibles del workflow mediante ${...}"),
                OutputFields = L(
                    "state.last.nodeId", "state.last.setCount", "state.last.removeCount",
                    "state.last.removedCount", "state.last.setKeys", "state.last.removeKeys"),
                Validations = L(
                    "debe existir al menos una variable en set o remove",
                    "las claves de set/remove deben ser rutas de contexto válidas",
                    "set y remove son datos declarativos y no nuevas intenciones",
                    "los valores guardados conservan texto, JSON, números, booleanos y plantillas ${...}"),
                SummaryTemplate = "Actualizar variables del contexto."
            };

            contract.AlternativeRequirements.Add(new WfAiAlternativeRequirement
            {
                Key = "stateVarsChange",
                Label = "Cambio de variables",
                Alternatives = new List<List<string>> { L("set"), L("remove") },
                ClarificationQuestion = "¿Qué variable querés guardar o quitar? Para guardar podés indicar variable = valor; para quitar, indicá la variable.",
                ControlKind = WfAiControlKind.Text,
                Blocking = true,
                Exclusive = false
            });

            return contract;
        }

        private static WfAiNodeConstructionContract ControlIf()
        {
            var contract = new WfAiNodeConstructionContract
            {
                NodeType = "control.if",
                Label = "Condición (If)",
                HumanIntent = "Evaluar una condición y abrir una rama SI y una rama NO.",
                HumanPhrases = L("si", "cuando", "en caso de", "si cumple", "caso contrario"),
                Parameters = new List<WfAiParameterContract>
                {
                    P("field", "Dato a evaluar", "field", false, WfAiInferencePolicy.NeverInfer, null, "¿Qué dato querés evaluar?", WfAiControlKind.AvailableData, null, true),
                    P("op", "Operador", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿Cómo querés comparar ese dato?", WfAiControlKind.Select, null, true),
                    P("value", "Valor", "object", false, WfAiInferencePolicy.NeverInfer, null, "¿Contra qué valor querés comparar?", WfAiControlKind.Text, null, true),
                    P("expression", "Expresión", "string", false, WfAiInferencePolicy.NeverInfer, null, "¿Qué condición querés evaluar?", WfAiControlKind.ConditionBuilder, null, true),
                    P("transform", "Transformación", "string", false, WfAiInferencePolicy.AskIfMissing, null, "", WfAiControlKind.Select, null, false),
                    P("rulesMode", "Modo de reglas", "string", false, WfAiInferencePolicy.SafeDefault, J("all"), "", WfAiControlKind.Select, L("all", "any"), false),
                    P("rules", "Reglas", "array", false, WfAiInferencePolicy.NeverInfer, null, "¿Qué condiciones debe evaluar?", WfAiControlKind.ConditionBuilder, null, true)
                },
                InputFields = L("Datos disponibles del workflow"),
                Validations = L("debe existir condición simple, expresión o reglas", "SI y NO deben existir", "SI y NO no pueden terminar en el mismo destino"),
                SummaryTemplate = "Evaluar una condición y continuar por SI o NO."
            };

            contract.AlternativeRequirements.Add(new WfAiAlternativeRequirement
            {
                Key = "conditionDefinition",
                Label = "Definición de la condición",
                Alternatives = new List<List<string>> { L("rules"), L("expression"), L("field", "op") },
                ClarificationQuestion = "¿Qué condición querés evaluar?",
                ControlKind = WfAiControlKind.ConditionBuilder,
                Blocking = true
            });

            return contract;
        }

        private static WfAiParameterContract P(
            string name,
            string label,
            string dataType,
            bool required,
            string inferencePolicy,
            JToken defaultValue,
            string question,
            string controlKind,
            List<string> options,
            bool importantDecision,
            List<string> placeholderValues = null,
            string derivedFromParameter = null)
        {
            return new WfAiParameterContract
            {
                Name = name,
                Label = label,
                DataType = dataType,
                Required = required,
                InferencePolicy = inferencePolicy,
                DefaultValue = defaultValue,
                ClarificationQuestion = question ?? string.Empty,
                ControlKind = controlKind,
                Options = options ?? new List<string>(),
                ImportantDecision = importantDecision,
                PlaceholderValues = placeholderValues ?? new List<string>(),
                DerivedFromParameter = derivedFromParameter ?? string.Empty
            };
        }

        private static JToken J(string value)
        {
            return value == null ? null : new JValue(value);
        }

        private static List<string> L(params string[] values)
        {
            return new List<string>(values ?? new string[0]);
        }
    }
}
