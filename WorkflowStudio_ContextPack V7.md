1) WorkflowStudio_ContextPack.md
# Workflow Studio – Context Pack

Autor del proyecto: Omar Silverii  
Plataforma: ASP.NET WebForms (.NET Framework 4.8)  
Lenguajes: C#, VB.NET, JavaScript  
Base de datos: SQL Server  
Arquitectura: Intranet, motor propio de workflows con canvas visual

---

## OBJETIVO DEL SISTEMA

Workflow Studio es un motor profesional de flujos de trabajo para intranet:

- Editor visual tipo canvas (nodos + aristas).
- Definiciones persistidas en SQL (`WF_Definicion.JsonDef`).
- Ejecución real en servidor mediante motor propio.
- Soporte de:
  - HTTP requests
  - SQL
  - Logger
  - Human Tasks
  - Queues
  - Email
  - Subflows (flujos hijos)
- Persistencia de instancias (`WF_Instancia`, `WF_InstanciaLog`, `WF_Tarea`, etc).

El sistema **no es un demo**: debe ser genérico, profesional y escalable.

---

## MOTOR

Clases clave:

- `MotorFlujo`
- `WorkflowRunner`
- `WorkflowRuntime`
- `ContextoEjecucion`
- `NodeDef`, `EdgeDef`, `WorkflowDef`
- `IManejadorNodo`

Ejecución:

1. `WorkflowRuntime.CrearInstanciaYEjecutarAsync`
2. Seed inicial (`WF_SEED`) en `HttpContext.Items`
3. `WorkflowRunner.EjecutarAsync`
4. `MotorFlujo.EjecutarAsync`
5. Cada nodo ejecuta su `IManejadorNodo`

El estado de ejecución vive en:

```csharp
HttpContext.Current.Items["WF_CTX_ESTADO"]


Y se persiste en:

WF_Instancia.DatosContexto

CONVENCIONES DEL PROYECTO

No renombrar clases existentes.

No reescribir módulos completos si no es necesario.

Cambios mínimos, precisos, profesionales.

Código completo cuando se entrega.

No inventar comportamientos.

No romper compatibilidad.

Bootstrap local.

DefaultConnection siempre.

Inspector JS define UI, handlers C# definen ejecución.

SUBFLOW (util.subflow)

El subflow es una llamada a otro workflow, creando siempre una nueva WF_Instancia hija.

Objetivos:

El PADRE llama a uno o más HIJOS.

Cada HIJO:

Tiene su propia instancia.

Recibe input.

Se ejecuta completo.

Devuelve resultado al PADRE.

Evitar bucles infinitos.

Permitir anidación controlada.

Diseño actual:

Se agregó:

En WF_Definicion: campo estable Key (ej: DEMO.SUBFLOW.VALIDAR)

En WF_Instancia:

ParentInstanciaId

ParentNodoId

RootInstanciaId

Depth

El nodo util.subflow:

Parámetros:

{
  "ref": "DEMO.SUBFLOW.VALIDAR",
  "input": {
    "clienteId": "${input.clienteId}"
  }
}


Comportamiento:

Resuelve ref contra WF_Definicion.Key.

Anti-recursión:

wf.depth

wf.callStack

maxDepth

Crea instancia hija con parenting.

Ejecuta la instancia hija.

Lee Estado y DatosContexto del hijo.

Expone en el PADRE:

${subflow.instanceId}
${subflow.childState}
${subflow.ref}
${subflow.estado}
${subflow.logs}

GRAFO PADRE FINAL (GUARDAR)
{
  "StartNodeId": "n1",
  "Nodes": {
    "n1": {
      "Id": "n1",
      "Type": "util.start",
      "Label": "Inicio",
      "Parameters": { "position": { "x": 240, "y": 80 } }
    },
    "n2": {
      "Id": "n2",
      "Type": "util.logger",
      "Label": "Inicio PADRE",
      "Parameters": {
        "level": "Info",
        "message": "PADRE: Inicio. clienteId=${input.clienteId}",
        "position": { "x": 240, "y": 170 }
      }
    },
    "n3": {
      "Id": "n3",
      "Type": "util.subflow",
      "Label": "Subflow Validar",
      "Parameters": {
        "ref": "DEMO.SUBFLOW.VALIDAR",
        "input": { "clienteId": "${input.clienteId}" },
        "position": { "x": 240, "y": 270 }
      }
    },
    "n4": {
      "Id": "n4",
      "Type": "util.logger",
      "Label": "Resultado HIJO1",
      "Parameters": {
        "level": "Info",
        "message": "PADRE: HIJO1 instanceId=${subflow.instanceId} childState=${subflow.childState} ref=${subflow.ref}",
        "position": { "x": 240, "y": 370 }
      }
    },
    "n5": {
      "Id": "n5",
      "Type": "util.subflow",
      "Label": "Subflow Notificar",
      "Parameters": {
        "ref": "DEMO.SUBFLOW.NOTIFICAR",
        "input": {
          "clienteId": "${input.clienteId}",
          "mensaje": "Cliente validado OK. Notificar a mesa de ayuda."
        },
        "position": { "x": 240, "y": 470 }
      }
    },
    "n6": {
      "Id": "n6",
      "Type": "util.logger",
      "Label": "Resultado HIJO2",
      "Parameters": {
        "level": "Info",
        "message": "PADRE: HIJO2 instanceId=${subflow.instanceId} childState=${subflow.childState} ref=${subflow.ref}",
        "position": { "x": 240, "y": 570 }
      }
    },
    "n7": {
      "Id": "n7",
      "Type": "util.end",
      "Label": "Fin",
      "Parameters": { "position": { "x": 240, "y": 680 } }
    }
  },
  "Edges": [
    { "Id": "e1", "From": "n1", "To": "n2", "Condition": "always" },
    { "Id": "e2", "From": "n2", "To": "n3", "Condition": "always" },
    { "Id": "e3", "From": "n3", "To": "n4", "Condition": "always" },
    { "Id": "e4", "From": "n4", "To": "n5", "Condition": "always" },
    { "Id": "e5", "From": "n5", "To": "n6", "Condition": "always" },
    { "Id": "e6", "From": "n6", "To": "n7", "Condition": "always" }
  ],
  "Meta": null
}

PENDIENTES CLAROS

El inspector de util.subflow debe:

Mostrar ref

Mostrar input con validador JSON

Documentar outputs visibles

El PADRE debe ser quien define cuántos HIJOS hay (uno por nodo).

El sistema debe permitir jerarquía PADRE → HIJO → NIETO sin loops.

Optimizar experiencia de usuario en canvas.