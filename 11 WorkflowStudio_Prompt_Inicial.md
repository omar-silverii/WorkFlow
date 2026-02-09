Vas a trabajar con el proyecto Workflow Studio de Omar Silverii.

Antes de responder cualquier cosa:

1) Leer completamente el archivo:
WorkflowStudio_ContextPack.md

2) Asumir que:

- Es un motor profesional de workflows.
- Está desarrollado en ASP.NET WebForms (.NET 4.8).
- Tiene un motor propio con handlers IManejadorNodo.
- El UI se define por inspectors JavaScript.
- El runtime está en WorkflowRuntime.

Reglas obligatorias:

- NO cambiar arquitectura.
- NO inventar nombres de claves internas.
- NO crear nuevos conceptos si ya existe uno equivalente.
- NO renombrar wf.def ni wf.motor.
- NO reescribir archivos fuera del alcance.
- Siempre trabajar sobre el código real del proyecto.
- Siempre entregar clases completas cuando se pide código.
- Siempre respetar el patrón de los handlers existentes.
- No proponer soluciones teóricas que no encajen con este motor.

Convenciones obligatorias:

- Variables técnicas con prefijo wf.*
- Variables de negocio con prefijo biz.*

Estado de ejecución:
ctx.Estado es la única fuente de estado.

Persistencia:
DatosContexto guarda:
logs, estado (filtrado) y biz.

Proyecto actual:

- Ya existen los nodos:
  state.vars
  transform.map
  data.sql
  config.secrets
  ai.call
  control.parallel
  control.join
  control.ratelimit
  file.read

Tema estratégico actual:

Integración del motor de workflow con el sistema de Gestión Documental existente
(de la empresa de Omar).

Principio clave:

El workflow NO gestiona archivos.
El workflow solo gestiona referencias documentales.

Contrato documental obligatorio en contexto:

biz.case.rootDoc
biz.case.attachments[]

Cada documento debe representarse mediante:

- documentoId
- carpetaId
- ficheroId
- tipo
- índices
- referencia al visor

Los próximos nodos a diseñar son:

- dms.search (o doc.search)
- dms.attach (o doc.attach)

La UI del workflow debe mostrar:

- documentos asociados a la instancia
- documentos asociados a cada tarea
- abrir siempre el visor existente

Nunca crear visores propios dentro del workflow.

El objetivo final es un producto vendible para empresas,
incluyendo bancos, con restricciones de red e infraestructura.

En esta sesión recibirás además:

- el ZIP actualizado del proyecto
- este Context Pack
- este mismo prompt

Debes utilizar TODO este material como única base de verdad.
