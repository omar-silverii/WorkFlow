1. Context Pack para nueva conversación

Copiá todo lo que viene en este bloque y guardalo como WorkflowStudio_ContextPack_v2.md (o PDF, lo que uses):

Proyecto: Workflow Studio (Omar) – Context Pack v2

Tecnología base

ASP.NET WebForms (.NET 4.8 / C#).

Proyecto Intranet con páginas:

WorkflowUI.aspx → editor/canvas de flujos.

WF_Definiciones.aspx → grilla de definiciones (WF_Definicion).

WF_Instancias.aspx → lista/log de instancias (WF_Instancia + WF_InstanciaLog).

Motor de flujo:

Modelo: WorkflowDef, NodeDef, EdgeDef.

Runtime: WorkflowRuntime, ContextoEjecucion, IManejadorNodo y handlers por tipo de nodo.

Tablas SQL:

WF_Definicion (definición, con jsonDef).

WF_Instancia.

WF_InstanciaLog.

Convenciones de contexto (ctx.Estado)

Espacios actuales:

input.* → datos de negocio del flujo (canonical).

wf.* → metadata de instancia (ids, etc.).

doc.* → se usaba antes como prefijo para extracción de documentos, pero lo estamos dejando de usar a favor de input.*.

Decisión actual:

Para este proyecto y los flujos que estamos armando ahora, normalizamos todo a input.*:

Ejemplos:

input.usuarioId

input.codigoItem

input.descripcion

input.cantidad

input.monto

input.autorizada

En el flujo de Nota de Pedido de Compras:

doc.load obtiene el texto del DOCX.

doc.extract (modo legacy) escribe directamente en:

input.Fecha

input.usuarioId

input.Solicitante

input.codigoItem

input.Descripcion

input.Cantidad

input.monto

input.autorizada

Nodos relevantes ya implementados
1. doc.load (handler C#)

Clase: HDocLoad : IManejadorNodo en Intranet.WorkflowStudio.WebForms.DocumentProcessing.

Parámetros:

path (string) → ruta del archivo (pdf/docx/txt).

mode (auto|pdf|word|text|image) → normalmente auto.

Comportamiento:

Lee archivo a bytes.

Si mode = auto:

.pdf → usa PdfPig → extrae texto página por página.

.docx → usa OpenXML → doc.MainDocumentPart.Document.InnerText.

otro → Encoding.UTF8.GetString(bytes).

Escritura en contexto (unificado):

ctx.Estado["input.filename"] = Path.GetFileName(path);

ctx.Estado["input.text"] = text;

Log:

"[doc.load] OK — Archivo cargado y texto extraído."

2. doc.load (Inspector JS)

Archivo: Scripts/workflow.inspector.doc.load.js (nombre aproximado).

Muestra:

Etiqueta del nodo (label).

Ruta (path).

Modo (mode).

Info fija “Salida: input.filename / input.text”.

Ya no guarda salidaPrefix en parámetros.

3. doc.extract (handler C#)

Clase: HDocExtract : IManejadorNodo.

Soporta dos modos:

Modo Legacy (lo estamos usando ahora)

Param rulesJson = JSON con array de reglas:

[
  { "campo": "Fecha", "regex": "Fecha:\\s*(\\d{2}/\\d{2}/\\d{4})", "grupo": 1 },
  { "campo": "usuarioId", "regex": "Legajo\\s*(\\d+)", "grupo": 1 },
  ...
]


Origen:

Ahora por default toma origen = "input.text".

Para cada regla:

Si tiene linea/colDesde/largo → modo fixed.

Si tiene regex → busca en el texto.

Escritura en contexto (unificado):

Antes: doc.<campo>

Ahora: input.<campo>
ej: input.Fecha, input.usuarioId, etc.

Log de ejemplo:

[doc.extract/regex] input.Fecha = "09/12/2025".

Modo Nuevo (Regex genérico) (ya listo pero aún no lo usamos a fondo):

Permite:

regex

regexOptions

destino (default input.result)

mode (single|multi)

fields (JSON) con definición de grupos → tipos (int, decimal, date, etc.).

Guarda resultado (diccionario/lista) en ctx.Estado[destino] usando rutas con puntos.

4. doc.extract (Inspector JS)

Formulario:

Etiqueta.

Origen (origen).

Reglas JSON (rulesJson).

Botones:

Insertar ejemplo.

Probar extracción (previewExtract(origen, rules)).

Guardar.

Eliminar nodo.

Actualmente el ejemplo usa formato legacy (array de reglas).

Flujo actual: Nota de Pedido → Proceso de Compra

Documento de entrada (Nota de Pedido de Compras):

Texto tipo:

Fecha, Solicitante, Legajo, Código de ítem, Descripción, Cantidad, Monto Estimado, Motivo, Aprobación requerida.

Workflow de ejemplo (simplificado):

util.start (n1).

doc.load (n31):

path = "C:\\temp\\Nota de Pedido de Compras.docx"

Deja texto en input.text.

doc.extract (n31/n27):

Reglas legacy que llenan:

input.Fecha

input.usuarioId (desde Legajo).

input.Solicitante

input.codigoItem

input.Descripcion

input.Cantidad

input.monto

input.autorizada

util.notify “Solicitud de compra” (n2):

Log:

Usuario=${input.usuarioId}, Tipo=${input.tipo}, Item=${input.codigoItem}, Importe=${input.monto}.

control.if “¿Material o servicio?” (n3):

expression = ${input.tipo} == "MATERIAL" (por ahora input.tipo está vacío en el ejemplo).

Rama MATERIAL:

Consulta stock (http.request).

Si hay stock → notificación “Atender y actualizar stock” + delay.

Rama SERVICIO / sin stock:

Notificaciones:

“Comprobar presupuestos anteriores”.

“Hacer al menos 3 presupuestos”.

control.if “¿Compra autorizada?” (n10):

En el ejemplo usamos input.autorizada == "Sí".

control.if “¿Monto > 1000?” (n11):

expression = ${input.monto} > 1000
(más adelante deberíamos parsear decimales).

Si autorizado y monto > 1000:

Publicar en cola de autorización (queue.publish).

Luego contrato + proceso de compra.

Si autorizado y monto <= 1000:

Directamente contrato + proceso de compra.

Si NO autorizado:

util.error → marca wf.error = true y notifica.

Estado actual confirmado (Instancia 147):

Se ve en el log:

input.Fecha = "09/12/2025"

input.usuarioId = "2219"

input.Solicitante = "Juan Pérez"

input.codigoItem = "MAT-55819D"

input.Descripcion = "Tornillos acero inoxidable 10mm"

input.Cantidad = "120"

input.monto y input.autorizada cargados desde el texto.

El flujo recorre correctamente las condiciones y termina en Fin sin error cuando autorizada == "Sí".

Idea futura acordada (para siguiente etapa)

Definir tabla de tipos de documento (catálogo de documentos de la empresa):

Campos mínimos:

DocTipoId

Codigo (ej. ORDEN_COMPRA, NOTA_PEDIDO, FACTURA_VENTA)

Nombre

ContextPrefix (ej. oc, np, fact)

Opcionales:

plantilla, ruta base, etc.

Nodos de documento (doc.load, doc.extract, etc.) deberían:

Tomar un DocTipo y, desde SQL, obtener ContextPrefix.

Usar ese prefijo para escribir en <prefix>.text, <prefix>.result, etc.

Luego, un mapeo (transformación) se encarga de pasar lo necesario a input.*.

Por ahora, y hasta terminar el prototipo OC, seguimos con:

doc.load → input.filename, input.text.

doc.extract (legacy) → input.<campo>.

2. Qué conviene que tengas subido a GitHub (clarito)

En tu repo, sería ideal tener carpetas/archivos más o menos así:

/Docs/WorkflowStudio_ContextPack_v2.md ← este texto.

/WebForms/WorkflowUI.aspx + .cs.

/WebForms/WF_Definiciones.aspx + .cs.

/WebForms/WF_Instancias.aspx + .cs.

/WebForms/App_Code/Workflow:

ContextoEjecucion.cs

WorkflowDef.cs

WorkflowRuntime.cs

IManejadorNodo.cs

HDocLoad.cs

HDocExtract.cs

Handlers de util.*, control.*, etc.

/Scripts/workflow.catalog.js

/Scripts/workflow.inspector.doc.load.js

/Scripts/workflow.inspector.doc.extract.js

/SQL/Workflow_Tables.sql (WF_Definicion, WF_Instancia, WF_InstanciaLog, y más adelante DocTipos, etc.)

Con eso, cuando abras una nueva conversación, podés:

Pegar al principio el contenido de WorkflowStudio_ContextPack_v2.md.

Decir algo como:

“Este es el contexto de mi proyecto Workflow Studio. Quiero seguir desde acá con: [tema X]…”

Y si hace falta, pegar archivos puntuales de GitHub (o links + fragmentos) según lo que queramos tocar.