# Workflow Studio – Context Pack (Omar Silverii)

Proyecto real en producción / demo avanzada.

Autor: Omar Silverii  
Motor: ASP.NET WebForms (.NET 4.8)  
Base: SQL Server  
Repositorio: (se sube ZIP actualizado al iniciar sesión)

---

## 1. Visión general

Workflow Studio es un motor de workflows con:

- editor visual (canvas + toolbox + inspector)
- handlers propios (C# en App_Code/Handlers)
- ejecución persistente
- tareas humanas
- subflows
- extracción documental
- integración por watch folder
- colas SQL (WF_Queue)
- runtime con reejecución

No se debe rehacer arquitectura.

---

## 2. Identificación de workflows

Las definiciones se almacenan en:

Tabla: WF_Definicion

Campos importantes:

- Id
- Codigo
- Nombre
- Version
- JsonDef
- Key   ← clave técnica para subflows

Regla actual:

- el subflow se referencia por:
  
  WF_Definicion.Key

NO por Nombre.

Ejemplo real:

Key = DEMO.SUBFLOW.NOTIFICAR1

---

## 3. Modelo de ejecución

Instancias:

- WF_Instancia
- WF_InstanciaLog

Tareas humanas:

- WF_Tarea

Logs:

- WF_InstanciaLog

---

## 4. Estructura de variables de contexto

Convención actual (confirmada y usada):

### Entrada de datos de negocio

Todo lo funcional debe ir bajo:

biz.*

Ejemplo:

biz.Monto.Estimado  
biz.empresa  
biz.proveedor.codigo

---

### input.*

Se usa para:

- input inicial
- input de subflows

---

### Outputs estándar por nodo

Convención documentada y vigente:

- wf.tarea.<nodeId>.resultado
- payload.*
- biz.*
- input.*

---

### Outputs de subflow

Para el último subflow ejecutado:

- subflow.instanceId
- subflow.childState
- subflow.ref
- subflow.logs
- subflow.estado

Para subflows con alias:

- subflows.<alias>.instanceId
- subflows.<alias>.childState
- subflows.<alias>.ref
- subflows.<alias>.logs
- subflows.<alias>.estado

Ejemplo real:

subflows.hijo.instanceId

---

## 5. Subflows

Handler:

util.subflow  
Clase: HSubFlow.cs

Parámetros:

- ref  → WF_Definicion.Key   (obligatorio)
- as   → alias opcional
- input → JSON para el subflow

Reglas:

- el subflow es un workflow normal
- se crea una instancia hija
- se mantiene call stack y depth
- se previene recursión

Importante:

Si el subflow falla:

- se dispara salida "error"
- pero la instancia padre sólo queda en error si el grafo lo captura con util.error

---

## 6. Manejo de error

Nodo:

util.error

Es el único nodo que:

- marca wf.error = true
- hace que la instancia quede en Error

Si no hay util.error en el camino, el grafo puede finalizar en OK aunque haya fallado un nodo previo.

Esto es intencional y parte del diseño.

---

## 7. human.task

Clase:

HHumanTask.cs

Comportamiento confirmado:

- al ejecutarse:
  - detiene ejecución usando wf.detener
- PersistirFinal ya maneja correctamente este caso
- la instancia queda EnCurso

Esto NO debe romperse.

---

## 8. Escalamiento (SLA)

Arquitectura real:

### Detección de vencimientos

Stored:

WF_Tarea_Escalar_Pendientes

Función:

- busca tareas Pendiente
- vencidas por FechaVencimiento
- no asignadas
- que no tengan Datos.escalamientoEncolado=true
- marca:

Datos:
- escalamientoEncolado=true
- escalamientoEncoladoEn
- escalamientoEncoladoMotivo

Y encola mensaje:

WF_Queue
Queue = 'wf.escalamiento'

---

### Worker de escalamiento

Endpoint:

Api/Generico.ashx
action = worker.escalamiento.run

Clase:

Generico.cs

Consume:

WF_Queue (wf.escalamiento)

---

### Resolución de rol de escalamiento

Se toma desde:

WF_Setting

SettingKey:

wf.escalamiento.roleMap

Puede haber override por ScopeKey.

---

### Creación real de tarea escalada

Stored:

WF_Tarea_Escalar_CrearNueva

Comportamiento:

1) cierra tarea original
   Estado=Completada
   Resultado=Escalada

2) marca en Datos del original:

- escalado
- escaladoEn
- escaladoMotivo
- escaladoPor

3) crea nueva tarea:

- OrigenTareaId = tarea original
- mismo nodo
- nuevo rol
- copia Datos
- agrega objeto:

Datos.origenEscalamiento

con:

tareaId, motivo, rolOriginal, rolNuevo, creadaEn, creadaPor

---

## 9. Historial de escalamiento

API:

instancia.escalamiento.historial

Construye árbol usando:

OrigenTareaId

y Datos.origenEscalamiento

---

## 10. Integración confirmada

Opción A:

Watch Folder

Dispatcher:

Worker externo que:

- detecta archivos
- crea instancias usando WF_Definicion.Key
- pasa input.filePath
- mueve a Processed/Error

No se debe leer paths fijos dentro de workflows.

---

## 11. Handlers existentes reales

Handlers activos en App_Code/Handlers:

- util.start
- util.end
- util.logger
- util.error
- util.notify
- util.subflow
- human.task
- control.if
- control.delay
- control.retry
- control.parallel
- control.switch
- control.loop
- file.read
- file.write
- ftp.put
- http.request
- email.send
- queue.publish
- queue.consume (sql)
- queue.consume.legacy
- doc.extract
- doc.entrada
- util.docTipo.resolve

Existen clases duplicadas para algunos nodos
(H / Manejador) y no deben unificarse ahora.

---

## 12. UX – estado actual

Tema principal pendiente:

MEJORAR UX DE GRILLAS

### WF_Instancias.aspx

- filtros de estado
- ocultar finalizados
- búsqueda en DatosContexto aunque sea numérico

---

### WF_Definiciones.aspx

- marcar visualmente subflows
- bloquear ejecución manual de subflows
- propuesta visual profesional

Actualmente:

el subflow aparece bloqueado para Ejecutar
pero se puede acceder a Instancias.

Eso debe corregirse en UX.

---

## 13. Regla clave de grafos

Para cualquier JSON de ejemplo o demo:

SIEMPRE incluir camino con util.error
cuando se quiere mostrar comportamiento de error real.

---

## 14. Restricciones de trabajo

- No rehacer arquitectura
- No inventar tablas ni handlers
- No romper PersistirFinal
- No cambiar contrato de handlers existentes
- Siempre trabajar sobre el código real

---

Fin del Context Pack.
