# Workflow Studio — ContextPack (Marzo 2026)

## 🎯 Estado actual del proyecto

Proyecto: **Workflow Studio**
Arquitectura: ASP.NET WebForms (.NET 4.8) + SQL Server
Motor: propio (WorkflowRunner + Runtime + Handlers)

Objetivo actual:
✔️ Flujo documental real
✔️ RBAC real por roles
✔️ tareas humanas con retroceso controlado
✔️ documentos adjuntos visibles por tarea
✔️ demo estable y vendible

---

# 🧠 REGLAS DE TRABAJO (OBLIGATORIAS)

1. ❌ NO inventar nada
2. ❌ NO reescribir arquitectura
3. ❌ NO mandar parches enormes
4. ✔️ SIEMPRE cambios mínimos
5. ✔️ trabajar sobre código REAL del ZIP
6. ✔️ pensar antes de responder

---

# ⚙️ ARQUITECTURA RESUMIDA

## Tablas principales

### WF_Definicion

* JsonDef = grafo

### WF_Instancia

* DatosContexto (snapshot runtime)

### WF_Tarea

Campos clave:

* WF_InstanciaId
* NodoId
* NodoTipo
* RolDestino
* UsuarioAsignado
* Estado
* Resultado
* Datos (metadata + wfBack)

Campos agregados:

* ScopeKey
* AsignadoA

---

# 🧩 MOTOR — COMPORTAMIENTO CLAVE

## HHumanTask (contrato real)

### Primera vez

✔️ crea WF_Tarea
✔️ detiene motor

### Si existe tarea

#### Pendiente

✔️ detiene

#### Cerrada

✔️ continúa

#### Backtrack (rechazo)

✔️ crea nueva tarea con ciclo++

---

# 🔁 RETROCESO CONTROLADO (BACKTRACK)

## Modelo conceptual

Cada tarea humana tiene:

```
frameId
taskNodeId
returnToNodeId
cycle
status (open/rejected/approved)
```

Guardado en:

```
WF_Tarea.Datos.meta.wfBack
```

y runtime:

```
wf.back.stack
```

---

## Flujo real implementado

### Si RECHAZA

Runtime hace:

```
returnToNodeId
   ↓
wf.startNodeIdOverride
   ↓
reanuda desde ahí
```

### HHumanTask

Detecta:

```
expectedCycle
```

y decide:

✔️ nueva tarea
❌ continuar

---

# 🛠️ CORRECCIONES IMPORTANTES HECHAS (HOY)

## 🔹 FIX 1 — HHumanTask

Problema:

usaba:

```
wf.currentNodeId
```

que venía sucio.

Fix aplicado:

```csharp
var prevNodeId = GetStringFromState(ctx, "wf.exec.prevNodeId");

var rf0 = back2.ActiveRejectedFrameReturningTo(nodo.Id);
if (rf0 != null)
    prevNodeId = rf0.ReturnToNodeId;
```

✔️ backtrack consistente

---

## 🔹 FIX 2 — Runtime

Se persistía mal returnTo.

Fix:

```csharp
if (string.IsNullOrWhiteSpace(returnTo))
{
    if (seed.TryGetValue("wf.exec.prevNodeId", out var pv) && pv != null)
        returnTo = Convert.ToString(pv);
}

if (!string.IsNullOrWhiteSpace(returnTo))
    fr["returnToNodeId"] = returnTo;
else
    fr["returnToNodeId"] = null;
```

✔️ ahora vuelve bien

---

# 📎 DOCUMENTOS (IMPLEMENTADO)

NO existe WF_Adjunto
Se usa:

```
DatosContexto
  → biz.case.attachments[]
```

estructura:

```
{
 fileName
 viewerUrl
 tareaId
 tipo
 fecha
}
```

---

## Upload

Ruta actual:

```
App_Data/WFUploads/<inst>/<tarea>/
```

Futuro:

⚠️ configurable desde Administración

---

## Visualización

BindDocs soporta:

✔️ root.biz.case
✔️ root.estado.biz.case

Scopes:

✔️ Tarea actual
✔️ Instancia
✔️ Instancia/Tarea X

---

# 👥 RBAC (CRITERIO DEFINIDO)

## Permisos

### DASH

✔️ acceso al sistema
⚠️ default para todos

### TAREAS_MIS

✔️ ver tareas propias

### TAREAS_GERENCIA

✔️ ver global

### WF_ADMIN

✔️ ABM workflow

---

## Regla acordada

✔️ Si no tiene DASH → no entra a nada
✔️ DASH será default

---

# 🧪 ESCENARIO DE PRUEBA (OFICIAL)

Usuarios:

OMARD
USUARIO1 (Compras)
USUARIO2 (Operaciones)

Flujo:

```
OMARD → U2 → OMARD → U2 → U1 → U2 → U1 → FIN
```

con rechazos múltiples

---

# 📌 ESTADO ACTUAL

✔️ backtrack funcionando
✔️ docs visibles
✔️ roles funcionando
✔️ demo estable

pendiente:

* storage configurable
* UX adjuntos
* cerrar Execute (posible eliminar)

---

# 🧠 IMPORTANTE PARA FUTURO

NO romper:

✔️ backtrack
✔️ HHumanTask
✔️ runtime seed
✔️ DatosContexto

---

# FIN CONTEXTPACK
