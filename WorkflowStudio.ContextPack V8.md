# Workflow Studio – Context Pack

Autor del proyecto: Omar Silverii  
Entorno: ASP.NET WebForms (.NET 4.8), C#, SQL Server  
Proyecto: “Workflow Studio”  
Objetivo: Motor profesional de workflows empresariales con editor visual, ejecución en servidor y persistencia en SQL.

---

## Arquitectura General

- WebForms (Intranet)
- Editor visual en `WorkflowUI.aspx`
- Definiciones guardadas en tabla `WF_Definicion` (campo `JsonDef`)
- Instancias en `WF_Instancia`
- Logs en `WF_InstanciaLog`
- Tareas humanas en `WF_Tarea`

Motor principal:
- `WorkflowRunner` (ejecuta nodos)
- `ContextoEjecucion` (diccionario de estado)
- `WorkflowRuntime` (orquesta creación, ejecución, reanudación, persistencia)

Handlers principales:
- util.start / util.end
- util.logger
- util.notify
- util.error
- http.request
- data.sql
- human.task
- util.subflow
- control.if
- control.retry
- file.read / file.write
- queue.publish / queue.consume
- doc.load / doc.tipo.resolve
- parallel / join

---

## Flujo de Ejecución

1. Crear instancia:
   - `WorkflowRuntime.CrearInstanciaYEjecutarAsync`
   - Inserta en `WF_Instancia`
   - Crea `seed` en `WF_SEED`
   - Ejecuta motor

2. El motor:
   - Lee `WF_SEED` y lo usa como estado inicial
   - Ejecuta nodos en orden según edges
   - Cada handler puede modificar `ctx.Estado`
   - Si un nodo marca `wf.detener = true`, el motor se detiene

3. Persistencia:
   - Al finalizar o detenerse:
     - Se guarda `DatosContexto` en `WF_Instancia`
     - Contiene:
       - logs
       - estado completo
       - (nuevo) biz = subset del estado con claves `biz.*`

Formato actual de DatosContexto:
```json
{
  "logs": [...],
  "estado": { ... },
  "biz": { ... },
  "error": { "message": "..." }
}
