# PROMPT — Arranque nueva sesión Workflow Studio de Omar Silverii

Voy a pasarte:
1. este **ContextPack**
2. el **ZIP actualizado del proyecto**

Necesito que trabajes así, sin desviarte:

---

## Paso 1
Confirmá que **leíste completo** el ContextPack.

## Paso 2
Esperá el **ZIP** antes de proponer cambios.

## Paso 3
Cuando tengas el ZIP, revisá primero los archivos reales y recién después proponé modificaciones.

---

# Reglas obligatorias

- no inventar nada
- no cambiar arquitectura sin necesidad
- no reescribir cosas que ya funcionan
- cambios mínimos y precisos
- siempre sobre código real del ZIP
- no mandar “archivo completo” si no verificaste el archivo real actual
- no asumir que el código quedó exactamente como en el chat
- preferir formato: **DONDE DICE → REEMPLAZAR POR**
- si algo no cierra, decilo
- si falta una pieza, pedila
- no mezclar teoría larga con código si estamos corrigiendo algo puntual
- proponer siempre la **mejor versión de entrada**, no una intermedia para volver a tocar después
- no volver atrás sobre temas ya cerrados
- cuando algo ya funciona, no discutirlo de nuevo salvo bug real del ZIP

---

# Contexto funcional que debés recordar

## Pendientes / Cerradas
Esto ya está definido:
- **Pendientes = estado actual**
- **Cerradas = estado final**
- **nunca mezcladas**

## Backtrack
Ya quedó corregido y funcionando:
- combo **Volver a**
- etapas humanas previas válidas
- opción **Inicio**
- corrección del bug de volver a Inicio

No rehacer eso salvo que en el ZIP aparezca un bug real nuevo.

---

# Estado del proyecto

Workflow Studio ya tiene:
- editor visual
- runtime persistido
- tareas humanas
- seguridad / RBAC
- instancias y logs
- backtrack funcional
- capa documental Standard
- DocTipos y reglas
- soporte validado para múltiples ítems
- circuitos de negocio demostrables

No estamos discutiendo si “puede funcionar”.
Estamos cerrando producto usable, demostrable y vendible.

---

# Lo más importante que se cerró en la última sesión

## Adjuntos por definición
Se trabajó una pantalla para que una definición/workflow configure:
- si admite carga manual de adjuntos
- destino lógico
- texto funcional/descriptivo

Esa pantalla es para el administrador funcional del workflow, no para el usuario operativo.

## Eliminación excepcional de adjuntos desde instancias
Se cerró este punto y quedó funcionando.

### Criterio cerrado
Cuando se elimina un adjunto desde `WF_Instancias`:
- desaparece de Docs
- no vuelve a aparecer
- se elimina del estado actual de la instancia
- se intenta borrar el archivo físico
- queda auditoría en `WF_InstanciaLog`

### Concepto acordado
- **Docs = estado actual**
- **Logs = historial**

No volver a proponer “soft delete visible” salvo pedido explícito.

---

# Detalles técnicos importantes de la última sesión

## Permiso
Se definió / usó:
- `ADJUNTOS_ELIMINAR_INSTANCIA`

Eso habilita a eliminar adjuntos desde `WF_Instancias`.

## Problemas detectados y resueltos
Recordar porque ya nos hicieron perder tiempo:

1. El `Repeater` `rptDocs` no tenía enlazado:
   - `OnItemCommand="rptDocs_ItemCommand"`
   - y por eso no llegaba al server

2. El JS tenía texto precargado:
   - `"Carga incorrecta"`
   - eso confundía
   - debe evitarse esa precarga

3. La búsqueda del adjunto a eliminar era frágil
   - además se detectó un bug concreto:
     - en la UI el nombre podía venir de `fileName` o `nombre`
     - en el borrado se buscaba solo por `fileName`

4. Hubo una entrega intermedia donde aparecía `snapshot` sin existir
   - eso ya fue detectado y corregido

## Resultado validado por prueba real
Quedó probado:
- el adjunto desaparece de Docs
- al refrescar no vuelve
- el archivo físico desaparece
- queda log real en `WF_InstanciaLog`

---

# Qué NO quiero en la próxima sesión

- no me expliques de nuevo lo ya resuelto
- no me propongas volver atrás sobre adjuntos eliminados visibles
- no mezcles la lógica ya cerrada de `WF_Tarea_Detalle` con el nuevo punto, salvo que el ZIP muestre bug real
- no me des una solución intermedia si ya sabés la mejor
- no me hagas perder tiempo en teoría si el problema está localizado

---

# Cómo quiero que respondas cuando tengas el ZIP

Quiero que me respondas así:

## 1. Diagnóstico
Qué encontraste realmente en el código actual del ZIP.

## 2. Estado
Qué quedó bien, qué quedó a medio aplicar, qué compila, qué no.

## 3. Propuesta mínima
Qué archivos y métodos hay que tocar, sin rehacer lo que ya funciona.

## 4. Cambios
En formato claro, idealmente:
**DONDE DICE → REEMPLAZAR POR**

---

# Estilo de trabajo obligatorio

- respuestas cortas cuando el punto es claro
- no extenderte innecesariamente
- no volver a discutir decisiones cerradas
- si sabés la mejor opción, dámela directamente
- si el chat empieza a degradarse o mezclarse, asumir que puede convenir cerrar sesión y seguir con ZIP nuevo

---

# Prioridad al arrancar la próxima sesión

Cuando te pase el ZIP, tu prioridad es:
- revisar el estado real del proyecto
- confirmar que lo cerrado en adjuntos sigue bien
- y continuar desde el nuevo punto que te marque, **sin retroceder sobre lo ya validado**

---

# Recordatorio final
Necesitamos ganar tiempo.
Trabajá como si ya conocieras:
- la arquitectura
- el contexto
- lo que ya quedó cerrado
- y las reglas de Omar para no volver atrás ni tocar dos veces lo mismo.