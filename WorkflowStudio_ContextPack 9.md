# Workflow Studio – CONTEXT PACK
Autor: Omar Silverii
Proyecto: Workflow Studio (Intranet)

Este documento resume TODO el contexto técnico y funcional necesario
para continuar el proyecto en una nueva sesión sin perder decisiones.

--------------------------------------------------------------------
1. OBJETIVO DEL SISTEMA
--------------------------------------------------------------------

Workflow Studio es un motor de workflows para intranet corporativa,
basado en:

- definición visual de procesos (grafos),
- ejecución persistente,
- soporte de tareas humanas,
- extracción de datos de documentos,
- integración con sistemas externos,
- subflujos reutilizables.

Está orientado a automatización documental (OC, facturas, etc) y
procesos administrativos.

--------------------------------------------------------------------
2. STACK TÉCNICO
--------------------------------------------------------------------

- ASP.NET WebForms (.NET 4.8)
- SQL Server
- Autenticación Windows
- Bootstrap local
- JSON para definiciones
- Motor propio (no dependencias externas de workflow)

Solución principal:
Intranet.WorkflowStudio.WebForms

Worker adicional:
Dispatcher.WatchFolder

--------------------------------------------------------------------
3. TABLAS CLAVE
--------------------------------------------------------------------

WF_Definicion
- Id
- Codigo
- Nombre   <-- IMPORTANTE: se usa Nombre como key lógica
- Version
- JsonDef
- Activo

WF_Instancia
- Id
- WF_DefinicionId
- Estado              (EnCurso / Finalizado / Error)
- FechaInicio
- FechaFin
- DatosContexto (JSON consolidado de la ejecución)

WF_InstanciaLog
- logs técnicos

WF_Tarea
- tareas humanas

WF_InstanciaLog y WF_Tarea ya están integradas al motor.

--------------------------------------------------------------------
4. PÁGINAS PRINCIPALES
--------------------------------------------------------------------

WorkflowUI.aspx
- editor visual de grafos
- guarda JSON en WF_Definicion.JsonDef
- usa inspectors JS

WF_Definiciones.aspx
- listado de definiciones
- acciones:
  - Editar
  - Ver JSON
  - Instancias
  - Ejecutar

WF_Instancias.aspx
- grilla de instancias
- filtros
- panel de datos
- panel de logs
- reejecución

WF_Gerente_Tareas.aspx
- bandeja de tareas humanas

--------------------------------------------------------------------
5. MOTOR DE EJECUCIÓN
--------------------------------------------------------------------

Namespace:
Intranet.WorkflowStudio.Runtime

Clases importantes:

- WorkflowRuntime
- WorkflowRunner
- ContextoEjecucion
- NodeDef / EdgeDef / WorkflowDef

Entrada principal desde web:

WorkflowRuntime.CrearInstanciaYEjecutarAsync(
    int defId,
    string datosEntradaJson,
    string usuario
)

--------------------------------------------------------------------
6. SEMILLA DE CONTEXTO (SEED)
--------------------------------------------------------------------

CrearInstanciaYEjecutarAsync arma el seed así:

- seed["input"]            -> si hay JSON de entrada
- seed["inputRaw"]         -> fallback
- seed["wf.instanceId"]
- seed["wf.definicionId"]
- seed["wf.creadoPor"]

El seed se inyecta vía:

HttpContext.Current.Items["WF_SEED"]

El estado vivo de ejecución se mantiene en:

HttpContext.Current.Items["WF_CTX_ESTADO"]

--------------------------------------------------------------------
7. HANDLERS IMPLEMENTADOS
--------------------------------------------------------------------

Handlers registrados en ejecución:

- ManejadorSql
- HParallel
- HJoin
- HUtilError
- HUtilNotify
- HFileRead
- HFileWrite
- HDocExtract
- HDocLoad
- HDocTipoResolve
- HControlDelay
- HControlRetry
- HFtpPut
- HEmailSend
- HChatNotify
- HQueuePublishSql
- HQueueConsumeSql
- HSubflow

--------------------------------------------------------------------
8. CONVENCIÓN DE VARIABLES DE CONTEXTO
--------------------------------------------------------------------

Variables de negocio:

biz.*

Ejemplos:
- biz.ocNumero
- biz.fecha
- biz.total
- biz.proveedor.razonSocial

Variables del sistema:

wf.*
wf.currentNodeId
wf.currentNodeType
wf.detener
wf.error
wf.error.message

Subflow:
input.* dentro del subflujo

--------------------------------------------------------------------
9. DOC TIPOS Y EXTRACCIÓN
--------------------------------------------------------------------

Existe:

- WF_DocTipo
- WF_DocTipoReglas

Nodo:
util.docTipo.resolve

Luego:
doc.extract (regex y fixed)

Las reglas cargan directamente a biz.*

--------------------------------------------------------------------
10. HUMAN TASK
--------------------------------------------------------------------

Nodo:
human.task

Comportamiento:

- crea registro en WF_Tarea
- marca wf.detener = true
- el motor se detiene en ese nodo

Luego, cuando el usuario resuelve la tarea,
se reanuda la ejecución.

--------------------------------------------------------------------
11. SUBFLOWS
--------------------------------------------------------------------

Nodo:
util.subflow

Implementación ya realizada:

- referencia por WF_Definicion.Key / Nombre
- parent / child en WF_Instancia
- depth + callStack anti-recursión
- outputs:
  - subflow.instanceId
  - subflow.childState
  - subflow.ref
  - subflow.estado
  - subflow.logs

Decisión tomada:
los subflows son workflows normales, pero:
NO deben ejecutarse manualmente desde la grilla de definiciones.

Pendiente:
mejorar UX en grilla para subflows.

--------------------------------------------------------------------
12. WATCH FOLDER (DECISIÓN A)
--------------------------------------------------------------------

Integración confirmada:

A: Watch Folder

Proyecto:
Dispatcher.WatchFolder

Función:

- monitorea carpeta de entrada
- por cada archivo:
  - resuelve defId por WF_Definicion.Nombre
  - ejecuta workflow
  - mueve archivo a Processed o Error

Configuración:

- InputFolder
- ProcessedFolder
- ErrorFolder
- WorkflowKey  (usa Nombre de WF_Definicion)
- Pattern
- PollSeconds

Input enviado al workflow:

Dictionary<string, object>:

- input.filePath
- filePath
- fileName

--------------------------------------------------------------------
13. RESOLUCIÓN DE DEFINICIÓN
--------------------------------------------------------------------

IMPORTANTE:

NO se usa WF_Definicion.Key.

Se usa:

WF_Definicion.Nombre

Ejemplo real:

DEMO.ORDEN_COMPRA.E2E

Esto se corrigió en el dispatcher.

--------------------------------------------------------------------
14. PROBLEMA CLAVE RESUELTO
--------------------------------------------------------------------

Cuando se ejecutaba desde Dispatcher,
la instancia quedaba como Finalizada aunque existiera human.task.

Se corrigió en:

PersistirFinal()

Nueva lógica:

si wf.detener == true
→ ActualizarInstanciaEnCurso()

solo se marca Finalizado si no hay detención ni error.

Este punto ya funciona correctamente.

--------------------------------------------------------------------
15. PersistirFinal (UNIFICADO)
--------------------------------------------------------------------

Se usa:

- wf.detener
- wf.error
- wf.error.message

Si no hay estado:
fallback y se marca OK.

Esta lógica es la base para ejecución batch + tareas humanas.

--------------------------------------------------------------------
16. WATCH FOLDER FUNCIONANDO
--------------------------------------------------------------------

Estado actual:

- se procesan múltiples OC
- se generan múltiples instancias
- se mueven archivos
- se ejecutan reglas
- se crean tareas humanas

Confirmado funcionando con:

OC_demo_1.txt ... OC_demo_5.txt

--------------------------------------------------------------------
17. WF_Instancias.aspx
--------------------------------------------------------------------

Se agregó:

- filtros
- ocultar finalizados
- búsqueda en DatosContexto
- búsqueda por estado
- panel de logs
- historial de escalamiento (modal)

Detalle importante:

En la búsqueda NO se debe asumir que si q es numérico es ID.
Eso se detectó como una mejora necesaria:
se quiere buscar también dentro de DatosContexto aunque el texto sea numérico.

--------------------------------------------------------------------
18. PROBLEMA CON CLASES DUPLICADAS
--------------------------------------------------------------------

Error cometido:

se pegó código de WF_Instancias dentro de WF_Definiciones.

Resultado:
errores CS0102 / CS0111 (miembros duplicados).

Decisión:
no volver a mezclar code-behind.

--------------------------------------------------------------------
19. UX – DEFINICIONES Y SUBFLOWS
--------------------------------------------------------------------

Observación:

un workflow que es subflujo aparece igual que uno principal
en la grilla de definiciones.

Esto genera confusión porque:

no debe ejecutarse manualmente.

Idea validada conceptualmente:

- marcar workflows que son subflow-only
- o deshabilitar botón Ejecutar
- o mostrar jerarquía padre/hijos

Pendiente de diseño.

--------------------------------------------------------------------
20. DEMO REAL IMPLEMENTADA
--------------------------------------------------------------------

Caso real:
Orden de Compra

Flujo:

- file.read
- util.docTipo.resolve
- doc.extract
- if por monto
- human.task
- notificaciones

Se usa para demo comercial.

--------------------------------------------------------------------
21. OBJETIVOS INMEDIATOS PENDIENTES
--------------------------------------------------------------------

- mejorar grilla de WF_Instancias:
  - filtros limpios
  - ocultar finalizados por default

- mejorar búsqueda:
  - permitir buscar siempre en DatosContexto

- mejorar grilla de WF_Definiciones:
  - UX para subflows

- continuar con delta tracking (auditoría automática del sistema)

--------------------------------------------------------------------
FIN CONTEXT PACK
--------------------------------------------------------------------
