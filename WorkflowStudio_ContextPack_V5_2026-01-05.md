# Workflow Studio (Omar) — Context Pack (2026-01-05)

> Objetivo: que una nueva sesión entienda **rápido** el estado real del proyecto y no invente nada.

## 0) Reglas de trabajo (importantes)
- **No inventar** archivos, controles, ids, ni rutas. Si falta algo, pedir el archivo exacto.
- No hablar de `WF_SAVE` salvo que Omar lo pida explícitamente.
- Mantener cambios **mínimos**, profesionales, y pegables.
- Proyecto: **ASP.NET WebForms (.NET Framework 4.8, C#)** + **SQL Server** + **Bootstrap local**.

## 1) Estado actual (confirmado por Omar)
- Omar **revirtió** cambios relacionados a **MasterPage** y volvió a páginas “originales” (cada página con su CSS).
- El error de “guardar me saca de la pantalla / desaparece” fue por MasterPage y **ya no aplica** (está revertido).
- Ahora el foco es:
  1) **Nodos/Inspector**: que funcionen todos (al menos que el inspector edite y guarde params de cada nodo).
  2) UI menor (botones/usuario), pero el hilo principal ahora es **nodos**.

## 2) Archivos clave (UI)
### 2.1 Scripts/workflow.catalog.js
- Define `window.WorkflowData = { CATALOG, GROUPS, PARAM_TEMPLATES, ICONS }`.
- Contiene catálogo profesional en español (keys tipo `control.if`, `data.sql`, `util.docTipo.resolve`, etc.).

### 2.2 Scripts/workflow.templates.js (PACK de templates)
- IIFE que **muta** `window.PARAM_TEMPLATES` (no lo reasigna).
- Define defaults y `*.templates` (ej: `http.request.templates`, `data.sql.templates`, `control.if.templates`, etc.).
- Dispara evento `wf-templates-ready`.

### 2.3 Scripts/workflow.ui.js
- Mantiene canvas, nodos, edges, selección, export JSON, validación, y toolbar actions.
- Guarda posición en `Parameters.position {x,y}`.
- Usa `window.__WF_RESTORE` si viene del server para rehidratar.
- Importante: el inspector del panel derecho **se delega** a `window.WF_Inspector.render(...)` si existe.

Fragmento clave:
- `renderInspector()`:
  - Si existe `window.WF_Inspector.render`, delega; si no, muestra fallback.
- `buildWorkflow()` arma `{ StartNodeId, Nodes{}, Edges[], Meta }`.
- `captureWorkflow()` escribe JSON en hidden `hfWorkflow` y mete `Meta.Name` desde `txtNombreWf`.
- Botón Guardar SQL llama `__doPostBack('WF_SAVE','')` (pero **no hablar** de esto si Omar no lo pide).

### 2.4 (Encontrado por Omar) inspector.core.js  ✅
Este archivo **SÍ** es lo que se buscaba:
- Define `window.WF_Inspector = { register, render, helpers }`.
- Tiene:
  - `registry` para renderers específicos por `node.key`.
  - `renderGeneric(node)` como fallback (edita label y JSON params).
  - `renderEdge(edge)` con combo de condición (`always/true/false/error`).
- Permite que todos los nodos “funcionen” aunque no tengan inspector específico (porque cae en `renderGeneric`).

**Conclusión**: `inspector.core.js` es el “core” del inspector que `workflow.ui.js` intenta usar.

## 3) Archivos clave (Server)
### 3.1 WorkflowUI.aspx.cs (C#)
- En `Page_Load`:
  - Si viene `?defId=###` lee `WF_Definicion.JsonDef` y hace:
    - `window.__WF_RESTORE = <json>;` para rehidratar canvas.
- Tiene `GuardarDefinicionEnSql(json)` que:
  - valida JSON (`ValidateWorkflowJson`)
  - INSERT/UPDATE en `dbo.WF_Definicion` (campos: Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef)
  - actualiza hidden `hfDefId` cuando inserta.
- `btnProbarMotor_Click` ejecuta motor (`WorkflowRunner`) y rehidrata `window.__WF_RESTORE` con el mismo JSON al final.

### 3.2 WF_Definiciones.aspx.cs (C#)
- Grid con paging.
- Problema reportado: abrir con `WF_Definiciones?defId=67` no posiciona en la página correcta (si hay 4 páginas de definiciones).
- Aún no resuelto en este pack.

### 3.3 App_Code/MotorFlujoMinimo.cs
- Runtime del workflow (handlers, ContextoEjecucion, ejecución y routing por edges).
- **No** tiene relación con `WF_Inspector` (UI).

## 4) Problemas recientes que confundieron (para no repetir)
- Se mezcló el tema de MasterPage y el tema de guardado SQL/canvas. Omar **revirtió** MasterPage.
- El foco actual es **nodos/inspector** y consistencia UI.
- No agregar “pasos extra” ni listas largas: ir al grano.

## 5) Próximo paso recomendado (mínimo y directo)
1) Confirmar que `inspector.core.js` se esté cargando **antes** de usarlo:
   - Debe estar incluido en `WorkflowUI.aspx` (en el orden correcto).
2) Confirmar que existan estos elementos (ids) en el markup:
   - `inspectorBody`, `inspectorTitle`, `inspectorSub`
3) Si “algunos nodos no funcionan”, es porque faltan inspectores específicos:
   - Con `inspector.core.js` al menos deberían funcionar con `renderGeneric` (editar JSON params).
   - Luego, para nodos especiales (ej `util.docTipo.resolve` con `rulesJson` textarea), se registran renderers con:
     - `WF_Inspector.register('util.docTipo.resolve', function(node, ctx, dom, util){ ... })`

## 6) Cosas que Omar NO quiere
- Referencias a MasterPage (por ahora).
- Que el asistente “asuma” archivos o pida 10 cosas.
- Que el asistente cambie nombres/convenciones del proyecto.

---

# Prompt para iniciar nueva sesión (copiar y pegar)
Hola. Continuamos el proyecto **Workflow Studio** (ASP.NET WebForms .NET 4.8 C#, SQL Server, Bootstrap local).
Leé primero este Context Pack. Reglas: **no inventes nada**, cambios mínimos y pegables, no hables de `WF_SAVE` salvo que te lo pida.
Estado: revertí todo lo de MasterPage. El foco ahora es **nodos/inspector**.
En el front: `workflow.ui.js` delega el inspector a `window.WF_Inspector.render`. Encontré `inspector.core.js` que define `window.WF_Inspector` (registry + renderGeneric + edge editor).
Quiero que me guíes para asegurar que el inspector esté cargando bien y que TODOS los nodos puedan editar sus params (aunque sea con el inspector genérico). Si faltan inspectores específicos, vamos registrándolos con `WF_Inspector.register(key, renderer)`.
Te iré pegando los archivos exactos que pidas.
