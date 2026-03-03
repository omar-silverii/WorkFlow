# Workflow Studio – Context Pack (Febrero 2026)
Autor: Omar Silverii
Proyecto: Workflow Studio (Intranet – Gestión Documental)
Framework: .NET Framework 4.8 – ASP.NET WebForms
Base de Datos: SQL Server (DefaultConnection única)
Repositorio: GitHub privado

---

# 1. VISIÓN DEL PROYECTO

Workflow Studio es un motor profesional de workflows visuales integrado a un sistema de Gestión Documental empresarial.

Objetivo:
- Orquestar procesos documentales
- Extraer datos estructurados
- Crear Entidades (Casos)
- Generar tareas humanas
- Mantener trazabilidad completa

El sistema debe ser:
- Genérico
- Profesional
- Reutilizable
- No orientado a un caso puntual

---

# 2. ARQUITECTURA ACTUAL

## Motor
- MotorFlujoMinimo
- WorkflowRuntime
- Handlers en App_Code/Handlers
- Inspectors JS para UI
- Snapshot de estado vía EntidadService

## Tablas principales

WF_Definicion
WF_Instancia
WF_InstanciaLog
WF_Tarea
WF_Entidad
WF_EntidadIndice
WF_EntidadItem

Una sola base de datos.
Una sola connectionString: DefaultConnection.

---

# 3. ENTIDADES (ESTADO ACTUAL – CERRADO)

✔ Estado efectivo calculado vía tarea pendiente.
✔ Solo activas = tiene tarea pendiente.
✔ Total ya funciona correctamente.
✔ Snapshot funciona correctamente.
✔ Índices funcionan.
✔ KPIs alineados con la grilla.

NO volver a tocar:
- Lógica de Solo activas
- Lógica de Total
- Lógica de EstadoActual

Este módulo queda cerrado.

---

# 4. DOC.LOAD (Consolidado)

doc.load reemplaza:
- file.read
- docTipo.resolve
- doc.extract

Carga:
- input.*
- biz.*
- items[]
- índices

Extracción:
- Regex generadas desde WF_DocTipoReglas
- BuildRegex en servidor
- Preview funcional

Pendiente futuro:
- Soporte items[] múltiples vía Regex.Matches

---

# 5. SUBFLOW (Ya profesional)

util.subflow:
- Anti-recursión
- callStack
- depth
- outputs:
    subflow.instanceId
    subflow.childState
    subflow.ref
    subflow.estado
    subflow.logs

Funciona correctamente.

---

# 6. PRÓXIMA FASE

Integración SDK Apryse (PDF avanzado).

Objetivo:
- Reemplazar lectura básica PDF
- Mejorar extracción
- Posible análisis estructural
- Mejor precisión documental

Se recibirá:
- ZIP actualizado del proyecto
- ZIP SDK Apryse Framework 4.5.1 (compatible con 4.8)

---

# 7. REGLAS ABSOLUTAS

1. No inventar tablas.
2. No cambiar nombres.
3. No rehacer arquitectura.
4. No tocar lo ya cerrado.
5. Siempre trabajar sobre el ZIP real.
6. Cambios mínimos y profesionales.
7. Entregar métodos completos listos para pegar.
8. No enviar pseudo-código.

---

# 8. ESTADO EMOCIONAL DEL PROYECTO

Este proyecto es importante.
Se busca hacerlo bien, sólido y vendible.
Se debe evitar repetir temas cerrados.
Se debe mantener coherencia.

---

FIN DEL CONTEXTO ACTUAL