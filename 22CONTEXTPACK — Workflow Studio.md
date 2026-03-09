# Workflow Studio — ContextPack (Estado consolidado)

## 1. Stack y arquitectura (NO CAMBIAR)

* ASP.NET WebForms (.NET Framework 4.8)
* SQL Server (una sola BD, DefaultConnection)
* Runtime propio:

  * WorkflowRuntime
  * MotorFlujoMinimo
  * IManejadorNodo
* Tablas clave:

  * WF_Definicion
  * WF_Instancia
  * WF_InstanciaLog
  * WF_InstanciaDocumento
  * WF_Tarea
  * WF_UsuarioRol / WF_UserPermiso

Reglas:

* No inventar nada
* Cambios mínimos
* No reescribir arquitectura
* Basarse SIEMPRE en el código real

---

## 2. Estado funcional actual (marzo 2026)

### ✔ Runtime

* util.end guarda snapshot final vía:
  EntidadService.SnapshotFromState(ctx.Estado)

* estadoNegocio:
  se toma desde parámetros del nodo y se guarda en:

  ctx.Estado["wf.estadoNegocio"]

✔ Funciona.

---

## 3. Estados reales del sistema (definido hoy)

### Estado técnico (WF_Instancia.Estado)

* EnCurso
* Finalizado
* Error

### Estado negocio (Entidad)

* Pendiente
* Aprobada
* etc.

Decisión tomada:
👉 A nivel workflow usamos SOLO:

* EnCurso
* Finalizado
* Error

👉 “Aprobada” NO se usa más como estado de instancia.

---

## 4. Problema que quedó claro hoy

El pedido era:

👉 Desde Instancias → ver todos los documentos de la instancia finalizada
👉 NO repetir lo de “Datos”
👉 UX clara y directa

Lo que pasó:

* Se intentó reutilizar MostrarDatos()
* Terminó siendo redundante
* Se perdió tiempo

👉 DECISIÓN FINAL:

📌 El botón Docs debe mostrar SOLO documentos
📌 NO debe mostrar JSON
📌 NO debe mezclar con Datos

Esto queda para implementar en la próxima sesión.

---

## 5. Estado actual UI Instancias

Pantalla WF_Instancias:

### Izquierda:

* grilla instancias

### Derecha:

* Datos (JSON técnico)
* Documentos (Caso)
* Auditoría documental
* Logs

👉 Hoy:
Docs depende de Datos (mal diseño UX)

👉 Objetivo próximo:
Separar:

* MostrarDatos(instId)
* MostrarDocumentos(instId)

---

## 6. Estructura real documentos (confirmada)

Docs vienen de:

### A) DatosContexto

estructura:

estado.biz.case.rootDoc
estado.biz.case.attachments[]

### B) WF_InstanciaDocumento

(auditoría)

Ambos se deben mostrar.

---

## 7. Decisiones UX tomadas

✔ Sacar botón Reej.
✔ Agregar botón Docs
✔ Docs muestra SOLO:

* Documentos
* Auditoría documental

✔ NO mostrar JSON

---

## 8. Próximo objetivo técnico claro

Implementar:

MostrarDocumentos(instId)

que:

1. NO llama MostrarDatos
2. Carga:

BindDocsFromDatosContexto(json)
BindDocAudit(instId)

3. Oculta:

pnlDatos
pnlLogs

👉 Eso es lo único pendiente.

---

## 9. Estado emocional del proyecto (importante)

Problema real hoy:

* respuestas lentas
* cambios innecesarios
* repetición

👉 Regla acordada:

“Si sabemos que algo se va a necesitar después, se hace completo ahora.”

---

# FIN CONTEXTPACK
