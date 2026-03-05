# CONTEXTPACK — Workflow Studio (Estado real al corte)

## 🎯 Objetivo del proyecto

Workflow Studio es un motor profesional de workflows en ASP.NET WebForms (.NET 4.8) con:

* editor visual (WorkflowUI.aspx)
* runtime propio (WorkflowRuntime / MotorFlujoMinimo)
* tareas humanas (human.task)
* extracción documental (doc.load / doc.search)
* RBAC corporativo (usuarios, roles, permisos)
* persistencia SQL (WF_Definicion, WF_Instancia, WF_Tarea, etc.)

El foco actual es:

👉 RBAC + bandejas + human.task + backtrack
👉 UX clara para usuarios funcionales

---

# 📍 Estado actual REAL (lo último probado)

## ✔️ Backtrack (rechazo en human.task)

Estado:

* Funciona con:

  * wfBack.returnToNodeId
  * fallback por grafo (edge entrante al nodo)

Problema anterior:

```
[Backtrack] ERROR: returnToNodeId vacío y no hay wf.exec.prevNodeId
```

Causa real:

* tareas generadas con returnToNodeId NULL

Solución aplicada:

✔ fallback por grafo (CalcularNodoAnterior)

Esto YA no es el problema actual.

---

## ✔️ Caso actual en análisis (último punto)

Instancia: **110417**
Tarea: **70127**

Datos reales:

* Nodo: n4 (human.task)
* RolDestino: COMPRAS
* Estado: Pendiente
* AsignadoA: NULL
* ScopeKey: NULL
* InstScope: NULL
* ScopeResuelto: GLOBAL
* PasaFiltro SQL: 1

SQL validado:

```
TieneGerencia = 1
TieneVerGerente = 0
PasaFiltro = 1
```

👉 Por lo tanto:

🚨 NO es problema de SP
🚨 NO es problema de permisos
🚨 NO es problema de Scope

El problema está en:

👉 carga UI (WF_Gerente_Tareas.aspx)
👉 binding de grilla
👉 selección de SP por pestaña
👉 o post-filtro en code-behind

---

# 📍 Comportamiento observado (UI)

## WF_Gerente_Tareas.aspx

Usuario: OMARD\USUARIO3

Tabs:

* Mis tareas → 0 ✔️ correcto
* Por mi rol → 0 ✔️ correcto
* Mi alcance → muestra contador pero grilla vacía ❌
* Cerradas → 0 ✔️

👉 inconsistencia clara: el SP devuelve filas, pero la UI no las muestra.

---

## WF_Instancias.aspx

La instancia aparece:

✔ En curso
✔ detenida en human.task

Pero:

❌ no indica:

* quién la tiene
* en qué rol está
* por qué se detuvo
* qué pidió el rechazo anterior

Esto se mejorará luego.

---

# 📍 RBAC — estado consolidado

✔ Todos los SP ya usan:

```
TAREAS_GERENCIA
```

✔ VER_GERENTE eliminado (no volver a mencionarlo)

✔ Funciones activas:

* WF_UserHasPermiso
* WF_IsAdmin

✔ Modelo válido

👉 RBAC NO es el problema actual.

---

# 📍 Conclusión del punto actual

👉 El problema NO es SQL
👉 El problema NO es permisos
👉 El problema NO es backtrack

👉 El problema está en:

➡ WF_Gerente_Tareas.aspx (code-behind)

---

# 📍 Qué revisar en la próxima sesión (prioridad real)

Orden exacto:

## 1️⃣ WF_Gerente_Tareas.aspx.cs

Buscar:

* método que carga "Mi alcance"
* qué SP llama realmente
* si filtra luego con LINQ/DataView
* si usa Session/ViewState para filtros

👉 esto hoy es lo más sospechoso

---

## 2️⃣ Ver binding real de la grilla

Confirmar:

* DataSource correcto
* DataBind ejecutándose
* sin RowFilter aplicado

---

## 3️⃣ Confirmar pestaña activa

Posible bug:

👉 contador usa un SP
👉 grilla usa otro

---

# 📍 Lineamientos UX (guardar para después)

Esto NO se hace ahora, solo guardar:

Objetivo final:

👉 un usuario debe ver en 2 segundos:

* qué tiene
* dónde está
* quién lo tiene
* por qué volvió
* qué falta

Futuro:

* eliminar JSON libre en Observaciones
* reemplazar por UI guiada:

  * motivo rechazo
  * tipo pedido
  * adjuntos

---

# 📍 Estado emocional / operativo (importante)

Contexto real:

* se hicieron +20 SP corregidos
* ya no repetir cosas viejas
* no sugerir cambios SQL ahora
* foco en runtime/UI

👉 Próxima sesión: análisis directo del ZIP

---

# 📍 RESUMEN FINAL

👉 El problema actual NO es SQL
👉 El problema actual NO es permisos
👉 El problema actual NO es backtrack

👉 El problema actual ES:

➡ WF_Gerente_Tareas.aspx.cs

---

FIN CONTEXTPACK
