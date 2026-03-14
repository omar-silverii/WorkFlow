# CONTEXTPACK — Workflow Studio / Omar Silverii
## Sesión de cierre — 13/03/2026

---

# 1. Estado general del proyecto

Proyecto: **Workflow Studio**  
Stack principal:
- ASP.NET WebForms (.NET Framework 4.8)
- C#
- SQL Server
- Bootstrap local
- `DefaultConnection`
- seguridad RBAC propia del sistema

Arquitectura a respetar:
- no inventar tablas o capas si no hacen falta
- no cambiar la arquitectura sin necesidad
- cambios mínimos y profesionales
- siempre trabajar sobre el **ZIP real**
- no asumir que el chat refleja exactamente el estado del código
- no enviar “archivo completo” si no se verificó antes el archivo actual del ZIP
- preferir formato **DONDE DICE → REEMPLAZAR POR**
- no reescribir lo que ya funciona
- no volver atrás sobre decisiones ya cerradas

---

# 2. Estado funcional ya cerrado antes de esta sesión

## Pendientes / Cerradas
Esto quedó definido y no debe volver a discutirse:
- **Pendientes = estado actual**
- **Cerradas = estado final**
- **nunca mezcladas**

## Gerencia
- Pendientes muestra responsable actual real
- Cerradas muestra solo instancias finalizadas
- queda pendiente futuro refinamiento de columna **Cerrada por**

## Backtrack / retroceso
Quedó funcionando:
- combo **Volver a**
- etapas humanas previas válidas
- opción **Inicio**
- corrección del bug del salto a Inicio sin romper lo demás

No volver a replantear esa parte salvo bug real detectado sobre ZIP.

---

# 3. Objetivo principal trabajado en esta sesión

Se avanzó sobre dos iniciativas relacionadas con **adjuntos**:

1. **Configuración por Definición**
   - permitir definir si una definición/workflow admite carga manual de adjuntos
   - informar el “destino lógico” de esos adjuntos

2. **Eliminación excepcional de adjuntos desde Instancias**
   - para adjuntos mal cargados
   - controlado por permiso
   - con motivo obligatorio
   - con trazabilidad fuerte en `WF_InstanciaLog`

---

# 4. Decisiones funcionales cerradas en esta sesión

## 4.1 Pantalla de Adjuntos por definición
Se incorporó el concepto de una pantalla por definición para configurar adjuntos.

Esa pantalla:
- **NO** es para el usuario operativo final
- la usa el administrador funcional / administrador del workflow
- define:
  - si la carga manual está habilitada
  - destino lógico del adjunto
  - texto/descripción funcional del destino

### Criterio funcional acordado
Esta pantalla **no** define permisos de eliminación.  
Solo define:
- si el workflow permite adjuntar
- con qué finalidad documental/funcional se interpretan esos adjuntos

## 4.2 Eliminación excepcional desde WF_Instancias
Se trabajó específicamente el **punto 2** sin tocar lo que ya funcionaba en `WF_Tarea_Detalle`.

### Se decidió:
- no usar un “soft delete visible”
- si se elimina un adjunto:
  - **desaparece de Docs**
  - **no vuelve a aparecer**
  - se elimina del estado actual de la instancia
  - se intenta borrar el archivo físico
  - la auditoría queda en `WF_InstanciaLog`

### Criterio cerrado
No mostrar adjuntos eliminados en la vista normal.  
Separación conceptual:
- **Docs = estado actual**
- **Logs = historial de lo que pasó**

Esto quedó acordado como criterio correcto y no volver atrás salvo necesidad fuerte.

---

# 5. Implementación real lograda en esta sesión

## 5.1 Seguridad
Se definió y se usó el permiso:
- `ADJUNTOS_ELIMINAR_INSTANCIA`

Ese permiso es el que habilita a un usuario a eliminar adjuntos desde `WF_Instancias`.

### Importante
La pantalla de “Adjuntos por definición” **no** otorga ese permiso.  
El permiso se administra por la seguridad del sistema.

## 5.2 WF_Instancias
Se agregó/el terminó de conectar el flujo para eliminar adjuntos desde la vista de instancia.

### Problemas detectados y resueltos durante la sesión
1. El `Repeater` **no tenía** enlazado:
   - `OnItemCommand="rptDocs_ItemCommand"`
   - por eso el click nunca llegaba al code-behind

2. Se estaba usando confirmación JS con texto precargado:
   - `"Carga incorrecta"`
   - eso generaba confusión
   - se decidió quitar ese texto precargado y dejar el motivo vacío

3. La búsqueda del adjunto a eliminar era demasiado frágil
   - se reforzó para ubicar correctamente el item
   - y además se encontró un bug concreto:
     - en pantalla el nombre podía venir de `fileName` **o** `nombre`
     - pero en el borrado se buscaba solo por `fileName`
   - eso impedía encontrar algunos adjuntos reales

4. Se detectó un error de código intermedio:
   - se llamó a `snapshot` cuando esa variable ya no existía
   - se corrigió usando el objeto clonado correcto

## 5.3 Resultado final validado por Omar
Se probó y quedó validado que:

- el adjunto **desaparece de Docs**
- al refrescar **no vuelve a aparecer**
- el archivo físico **desaparece de `App_Data/WFUploads/...`**
- queda log en `WF_InstanciaLog`

### Log real de prueba validado
Ejemplo confirmado:
- fecha/hora
- nivel Warn
- “Adjunto eliminado manualmente”
- archivo
- tarea origen
- usuario que lo subió
- usuario que lo eliminó
- motivo

Eso confirma que la funcionalidad quedó cerrada y funcionando.

---

# 6. Qué quedó funcionando y NO debe tocarse sin motivo

## No tocar sin bug real:
- lógica actual de `WF_Tarea_Detalle` que ya funciona
- eliminación normal en tarea actual pendiente
- backtrack ya corregido
- criterio Docs actuales / Logs históricos
- permiso `ADJUNTOS_ELIMINAR_INSTANCIA`
- eliminación excepcional desde `WF_Instancias` que ya quedó probada

---

# 7. Reglas de trabajo reforzadas en esta sesión

Omar volvió a marcar algo muy importante:

## Regla crítica
**No volver atrás** innecesariamente.

Si se conoce la mejor solución:
- proponerla **de entrada**
- no dejar “mejoras para después” si ya se sabe que hay que hacerlas
- evitar iteraciones evitables
- necesitamos ganar tiempo

## Otra regla importante
Cuando algo ya funciona:
- **no volver a discutirlo**
- no mezclarlo con el nuevo punto
- ir directo al objetivo actual

## Y otra más
Cuando Omar pide algo puntual:
- no dar teoría larga
- no expandirse de más
- ir al punto exacto

---

# 8. Estado actual de adjuntos luego de esta sesión

## Ya resuelto
### Por definición
- existe configuración funcional de adjuntos por workflow/definición

### Por instancia
- existe eliminación excepcional desde `WF_Instancias`
- con permiso
- con motivo
- con log
- con eliminación real del estado actual
- con borrado físico del archivo si existe

## Criterio UX confirmado
El usuario no tiene que “adivinar” qué está eliminando.  
Se mejoró la confirmación mostrando:
- nombre del archivo
- tarea origen
- contexto de la instancia

---

# 9. Qué quedó pendiente para próximas sesiones

Esto no se cerró todavía y puede ser parte de la próxima sesión, según prioridad:

## 9.1 Mejoras de UX/seguridad sobre adjuntos
Posibles próximos pasos:
- mejorar aún más la experiencia visual del panel Docs
- resaltar claramente la instancia seleccionada
- refinar textos de confirmación
- revisar si conviene mostrar además nombre de instancia/estado/fecha en el panel derecho
- eventualmente pasar de `prompt/confirm` a modal propio más profesional

## 9.2 Unificación de modelo de borrado
Evaluar si conviene alinear totalmente:
- borrado normal en `WF_Tarea_Detalle`
- borrado excepcional en `WF_Instancias`

Pero **solo** si realmente suma y sin romper lo ya validado.

## 9.3 Seguridad por alcance
Sigue pendiente revisar mejor el modelo de permisos por:
- `ProcesoKey`
- `ScopeKey`

Pero sin depender por ahora de eso para demos.

---

# 10. Estado emocional / operativo a recordar
Omar marcó varias veces que:
- cuando el chat empieza a “mezclarse” o degradarse, conviene cerrar sesión
- va a subir el proyecto a GitHub y luego pasar el ZIP actualizado
- en la próxima sesión hay que trabajar rápido, sin reexplicar lo ya resuelto

---

# 11. Resumen ejecutivo de esta sesión
En esta sesión se cerró correctamente la **eliminación excepcional de adjuntos desde instancias**, con estas características:

- permiso específico
- identificación correcta del adjunto
- motivo obligatorio
- confirmación al usuario
- eliminación del estado actual
- archivo físico borrado
- auditoría completa en `WF_InstanciaLog`

Y además quedó asentado el criterio funcional:

- **Docs = adjuntos actuales**
- **Logs = historial**

Ese es el resultado más importante de esta sesión.