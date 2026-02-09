# Workflow Studio – Context Pack completo (Feb 2026)

Autor: Omar Silverii  
Proyecto: Workflow Studio  
Stack: ASP.NET WebForms (.NET 4.8) – SQL Server – Bootstrap – JS Inspectors  
Motor propio de workflows.

------------------------------------------------------------
1. OBJETIVO DEL PROYECTO
------------------------------------------------------------

Workflow Studio es un motor BPM / Workflow profesional, visual, orientado a procesos empresariales reales, integrable con sistemas existentes.

Está pensado para ser:
- vendible
- integrable en intranets
- compatible con entornos bancarios
- sin dependencia obligatoria de Internet
- con nodos desacoplados por handlers

El proyecto NO es una demo: es un motor productivo.

------------------------------------------------------------
2. ARQUITECTURA GENERAL
------------------------------------------------------------

Editor visual:
- Canvas con nodos
- Toolbox
- Inspectors por tipo de nodo (JS)
- JSON del grafo

Motor:
- WorkflowRunner
- Handlers (IManejadorNodo)
- ContextoEjecucion

Persistencia:
- WF_Definicion
- WF_Instancia
- WF_InstanciaLog
- WF_Tarea

------------------------------------------------------------
3. CLASES Y ESTRUCTURA CLAVE
------------------------------------------------------------

Runtime:
Intranet.WorkflowStudio.Runtime.WorkflowRuntime

Métodos principales:
- CrearInstanciaYEjecutarAsync
- EjecutarInstanciaExistenteAsync
- ReanudarDesdeTareaAsync

Contexto:
- WF_SEED
- WF_CTX_ESTADO

Convención:
- ctx.Estado contiene TODO el estado del workflow

Se acordó explícitamente:

NO cambiar nombres existentes:
- wf.def
- wf.motor

No introducir variantes como:
- WF_DEF_OBJ
- WF_MOTOR_OBJ

------------------------------------------------------------
4. CONVENCIÓN DE VARIABLES EN CONTEXTO
------------------------------------------------------------

Variables técnicas:
- wf.instanceId
- wf.definicionId
- wf.currentNodeId
- wf.currentNodeType
- wf.error
- wf.error.message
- wf.detener
- wf.startNodeIdOverride
- wf.resume.*

Contrato de negocio:

Prefijo:
- biz.*

Ejemplos:
- biz.task.id
- biz.task.result
- biz.task.data
- biz.oc.numero
- biz.oc.importe
etc.

------------------------------------------------------------
5. POLÍTICA DE PERSISTENCIA DEL CONTEXTO
------------------------------------------------------------

En DatosContexto se persiste:

{
  logs,
  estado,   <-- estado filtrado (sin __*, sin wf.def ni wf.motor)
  biz
}

No se persisten:
- __wf.*
- wf.def
- wf.motor

------------------------------------------------------------
6. NODOS IMPLEMENTADOS Y PROBADOS EN ESTA SESIÓN
------------------------------------------------------------

Ya implementados y funcionando:

control.parallel
control.join
control.ratelimit
file.read
state.vars
transform.map
config.secrets
data.sql
ai.call

------------------------------------------------------------
7. NODO: state.vars
------------------------------------------------------------

Handler: HStateVars

Función:
- setear variables en ctx.Estado
- borrar variables

Soporta:
- claves con path: ej biz.oc.numero
- armado de objetos anidados

Parámetros:
- set : object
- remove : array | csv

Se acordó que:
NO debe lanzar error duro si no hay cambios.

------------------------------------------------------------
8. NODO: transform.map
------------------------------------------------------------

Función:
- mapear estructuras
- armar payloads para nodos posteriores
- desacoplar input técnico de estructura de destino

Ejemplo de uso profesional:
- armar payload.sql.query y payload.sql.params
para data.sql

transform.map es un nodo clave del diseño (no se elimina).

------------------------------------------------------------
9. NODO: data.sql
------------------------------------------------------------

Nodo profesional para acceso a base de datos.

Parámetros:
- connectionStringName   (ej: DefaultConnection)
- query
- params
- output

Regla:
NO se interpolan variables dentro del SQL con ${...}
Las variables se pasan por parámetros.

transform.map se usa para construir:

payload.sql.query
payload.sql.params

data.sql ejecuta con parámetros.

------------------------------------------------------------
10. NODO: control.ratelimit
------------------------------------------------------------

Uso real:
control de consumo de APIs, servicios externos, gateways internos.

Parámetros:

- key
- maxPerMinute
- burst
- mode: delay | error
- maxWaitMs

key es una clave lógica de recurso:
ejemplos reales:

- api.afip.padron
- corebanking.saldos
- dms.search
- proveedor.sap

No es un token, es un identificador funcional.

------------------------------------------------------------
11. NODO: config.secrets
------------------------------------------------------------

Función:
leer secretos desde appSettings.

No loguea valores.

Parámetros:
- key
- source (appSettings)
- output

Publica:
- secreto real
- len
- masked

------------------------------------------------------------
12. NODO: ai.call
------------------------------------------------------------

Nodo de integración con IA desacoplada.

NO está acoplado a OpenAI.
Se diseñó como cliente genérico de gateway IA.

Contrato acordado:

Entrada:
- url
- method
- headers
- prompt
- system
- responseFormat (text | json)
- timeoutMs
- output

Salida:

output.status
output.raw
output.text
output.json
output.usage.*

El handler soporta:
- OpenAI style
- gateway interno
- mocks locales

NO se implementa lógica de negocio dentro del nodo.
Es un simple conector.

------------------------------------------------------------
13. PROBLEMA IIS / ASHX
------------------------------------------------------------

Se detectó:

- error 401 en AiMock.ashx
- IIS Express con autenticación Windows

Se solucionó configurando correctamente el location
como el resto de handlers .ashx del proyecto.

------------------------------------------------------------
14. POLÍTICA DE NODOS
------------------------------------------------------------

Reglas acordadas:

- No se inventan nombres nuevos de claves internas
- No se cambia arquitectura
- No se duplican conceptos
- Se crean handlers mínimos
- Inspectors definen UI
- Handlers definen ejecución

------------------------------------------------------------
15. LISTA DE NODOS DEL CATÁLOGO

Ya desarrollados en esta etapa:

- state.vars
- transform.map
- data.sql
- config.secrets
- ai.call
- control.parallel
- control.join
- control.ratelimit

Pendientes a futuro:
- data.redis.*
- cloud.storage
- code.function
- code.script
etc.

------------------------------------------------------------
16. INPUT DE WORKFLOW
------------------------------------------------------------

Se acordó:

En entorno productivo:
los workflows no dependen de pegar JSON manual.

El input real se obtiene por:

- file.read
- eventos externos
- integraciones

El uso de DatosEntrada manual es solo auxiliar.

------------------------------------------------------------
17. TEMA CLAVE NUEVO – INTEGRACIÓN CON GESTIÓN DOCUMENTAL
------------------------------------------------------------

La empresa es una empresa de Gestión Documental.

El sistema existente:

- gestiona FICHERO
- CARPETA
- DOCUMENTOS
- cada documento tiene:
  - documentoId
  - índices
  - tipo
  - visor propio
  - capturas AS400 (spool)
  - capturas Windows

El workflow NO debe almacenar archivos.

Debe integrarse por referencia.

------------------------------------------------------------
18. CONTRATO DOCUMENTAL PARA EL WORKFLOW
------------------------------------------------------------

Se definió como principio:

El workflow solo maneja referencias documentales.

Convención de contexto:

biz.case.rootDoc
biz.case.attachments[]

Cada documento debe representarse como:

{
  documentoId,
  carpetaId,
  ficheroId,
  tipo,
  indices,
  viewerUrl (o viewerKey)
}

------------------------------------------------------------
19. MODELO DE USO REAL
------------------------------------------------------------

Cada instancia de workflow tiene:

- un documento raíz del expediente
- múltiples documentos adjuntos durante el proceso

Las tareas humanas deben poder:

- ver documentos asociados
- adjuntar nuevos documentos existentes en el DMS

------------------------------------------------------------
20. NODOS DOCUMENTALES A CREAR (PUENTE CON DMS)
------------------------------------------------------------

No se va a crear un sistema documental nuevo.

Se crearán únicamente nodos puente:

- dms.search   (o doc.search)
- dms.attach   (o doc.attach)

Funciones:

dms.search
- buscar documentos por índices
- devolver referencias documentales

dms.attach
- asociar un documento existente al expediente del workflow

------------------------------------------------------------
21. VISUALIZACIÓN
------------------------------------------------------------

Workflow Studio no renderiza documentos.

Solo muestra:

- lista de documentos asociados
- botón Ver

El visor es el visor existente del sistema documental.

------------------------------------------------------------
22. PERSISTENCIA DOCUMENTAL
------------------------------------------------------------

Recomendación profesional:

crear tabla:

WF_InstanciaDocumento

con:

- InstanciaId
- DocumentoId
- NodoId
- TareaId
- Fecha
- Usuario
- Índices

Para auditoría bancaria.

------------------------------------------------------------
23. SEGURIDAD / ENTORNOS BANCARIOS
------------------------------------------------------------

El diseño es compatible con:

- intranet
- sin salida a Internet
- gateways internos
- servicios on-premise

ai.call está pensado para apuntar a gateways internos.

------------------------------------------------------------
24. PUNTO ACTUAL DEL PROYECTO
------------------------------------------------------------

El motor ya soporta:

- ejecución
- reintentos
- paralelos
- joins
- control de tasa
- transformación
- acceso SQL
- manejo de secretos
- integración con IA

El próximo gran módulo estratégico es:

Integración documental.

------------------------------------------------------------
25. REGLAS DE COLABORACIÓN
------------------------------------------------------------

- No inventar estructuras
- No renombrar claves existentes
- No reescribir archivos completos si no es necesario
- Cambios mínimos
- Siempre sobre código real
- Siempre entregar clases completas
- Inspectors en JS
- Handlers en C#

------------------------------------------------------------
FIN CONTEXT PACK
