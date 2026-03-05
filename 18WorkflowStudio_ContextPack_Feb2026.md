# WORKFLOW STUDIO – CONTEXT PACK
## Estado del Proyecto – Febrero 2026
Autor: Omar Silverii
Plataforma: ASP.NET WebForms (.NET 4.8)
BD: SQL Server (DefaultConnection única)
Motor: Workflow Studio propio
Arquitectura: MotorFlujoMinimo + Runtime + Handlers

---

# 1. VISIÓN DEL PROYECTO

Workflow Studio ya no es una demo.
Es un producto real de Gestión Documental + Workflow Empresarial.

Objetivo:
- Modelar procesos empresariales mediante grafos.
- Asignar tareas humanas por Rol o Usuario.
- Persistir instancias.
- Permitir auditoría.
- Permitir RBAC completo.
- Integrarse al sistema de Gestión Documental existente.
- Ser vendible como producto profesional.

---

# 2. MOTOR ACTUAL

## Componentes Clave

- WF_Definicion
- WF_Instancia
- WF_InstanciaLog
- WF_Tarea
- WF_DocTipo
- WF_DocTipoReglaExtract

## Handlers actuales importantes

- util.start
- util.end
- util.logger
- data.sql
- control.if
- control.delay
- util.subflow
- doc.load
- doc.search
- doc.attach
- control.parallel
- control.loop
- TAREA HUMANA (nodo real existente en el ZIP)

NO se insertan tareas manualmente.
Las tareas humanas se crean mediante el nodo correspondiente del motor.

---

# 3. RBAC – MODELO ACTUAL

## Tablas activas

- WF_Permiso
- WF_Rol
- WF_UsuarioRol
- WF_RolPermiso
- WF_UserPermiso (override)

## Permisos vigentes

- ADMIN
- DASH
- DOC_ABM
- ENTIDADES_ABM
- INSTANCIAS
- SEGURIDAD_ABM
- TAREAS_GERENCIA
- TAREAS_MIS
- WF_ADMIN

## Objetivo RBAC

Simular empresa real:
- Usuarios ven solo sus tareas.
- Gerentes ven bandejas de su rol.
- Seguridad controla acceso por página.
- ADMIN es superusuario total.

RBAC actualmente funcionando correctamente.

---

# 4. OBJETIVO NUEVO (PRIORIDAD MÁXIMA)

## 🚨 VOLVER ATRÁS EN EL FLUJO (RECHAZO CONTROLADO)

Necesidad real del negocio:

Cuando una TAREA HUMANA rechaza algo:

- Puede pedir:
  - Incorporar un documento faltante
  - Completar un dato
  - Corregir información
  - Adjuntar evidencia
- El flujo debe:
  - Volver al nodo llamador
  - Registrar qué se pidió
  - Permitir múltiples rechazos
  - Reingresar al circuito normal
  - Mantener auditoría completa
  - No romper la instancia
  - No crear duplicados inconsistentes

Debe poder:
- Volver atrás N veces
- Saber quién pidió la corrección
- Saber qué se pidió
- Volver al circuito de validación
- Continuar hasta aprobación final

---

# 5. REGLAS IMPORTANTES

- No reinventar arquitectura.
- No crear hacks.
- No insertar SQL directo para simular tareas.
- Usar nodos reales del motor.
- Mantener consistencia con WF_Tarea.
- No romper compatibilidad actual.
- Una sola base.
- Una sola connection string.
- No asumir infraestructura inexistente.

---

# 6. PROBLEMA A RESOLVER EN PRÓXIMA SESIÓN

Diseñar arquitectura profesional para:

## 🔁 MECANISMO DE RETROCESO CONTROLADO

Posibles enfoques a evaluar:

- Modelo de pila (call stack de nodos)
- Guardar nodo anterior en WF_Tarea.Datos
- Marcar estado especial "Rechazado"
- Crear nueva tarea apuntando al nodo original
- Sistema de “Loop Controlado”
- Sistema de “Revisión requerida”
- Edge condicional dinámico

Debe definirse:

- Cómo se guarda el nodo llamador
- Cómo se identifica el retorno
- Cómo se evita bucle infinito
- Cómo se audita el ciclo completo
- Cómo se refleja en UI

---

# 7. ESTADO ACTUAL DEL PROYECTO

- RBAC funcionando
- Seguridad por página estable
- Topbar coherente
- Logout funcionando
- Usuarios 1–4 configurados
- Roles empresariales definidos
- Tareas visibles por usuario correctamente
- Base sólida lista para avanzar

---

# 8. SIGUIENTE PASO REAL

No probar usuarios.
No probar permisos.

Diseñar:
👉 Arquitectura profesional para volver atrás en tareas humanas.

Debe ser:
- Escalable
- Limpia
- Profesional
- Vendible
- Sin parches

---

FIN CONTEXTPACK