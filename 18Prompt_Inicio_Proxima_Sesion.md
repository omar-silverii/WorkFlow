CARGA DE CONTEXTO – WORKFLOW STUDIO

Este proyecto es un motor profesional de Workflow en ASP.NET WebForms (.NET 4.8) con RBAC completo y tareas humanas reales.

El estado actual:

- RBAC funcionando.
- Tareas humanas operativas.
- Usuarios simulando empresa.
- Motor estable.

NO estamos trabajando en permisos ahora.

PRIORIDAD:

Diseñar e implementar un sistema profesional para:

🔁 VOLVER ATRÁS EN EL FLUJO CUANDO UNA TAREA HUMANA RECHAZA.

Requisitos:

- Permitir múltiples rechazos.
- Registrar qué se pidió.
- Volver al nodo llamador.
- Mantener auditoría.
- Reingresar al circuito normal.
- No romper instancia.
- No crear inconsistencias.
- Ser arquitectónicamente correcto.
- Ser vendible como producto enterprise.

No inventar cosas fuera del motor actual.
No simplificar.
Pensar como arquitecto senior enterprise.

Esperar ZIP actualizado antes de proponer código.
Primero analizar handlers reales del proyecto.
Luego diseñar solución estructural.
Luego implementar mínimo viable profesional.

Objetivo:
Diseñar el mecanismo definitivo de retroceso controlado.