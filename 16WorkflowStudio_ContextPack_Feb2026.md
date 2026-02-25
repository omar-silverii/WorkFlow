# WORKFLOW STUDIO – CONTEXTPACK GLOBAL
Autor: Omar Silverii
Arquitectura: ASP.NET WebForms (.NET 4.8)
Base: SQL Server (DefaultConnection única)
Motor: Workflow visual con runtime propio
Fecha: Febrero 2026

---

# 1. VISIÓN DEL PRODUCTO

Workflow Studio deja de ser un simple motor técnico.
Se convierte en:

> Plataforma empresarial de gestión documental orientada a procesos.

Objetivo:
- Potente
- Sencilla para usuario final
- Sin dependencia obligatoria de DBA
- Vendible mundialmente
- Integrable con SDK documental premium (Apryse / iText)

---

# 2. DECISIÓN ARQUITECTÓNICA CLAVE

Se adopta modelo B: ENTIDAD GENÉRICA.

NO se crearán tablas por cada tipo documental.
NO dependeremos de que el cliente cree tablas específicas.

El sistema funcionará con modelo dinámico interno:

WF_Entidad
WF_EntidadItem
WF_EntidadIndice (indexación)

El Workflow gobierna estados.
La Entidad representa el caso/documento vivo.

Cada flujo crea una entidad.
Una entidad puede existir sin flujo.
El estado lo define el workflow.

---

# 3. LO YA IMPLEMENTADO (NO SE PIERDE)

✔ MotorFlujoMinimo
✔ WorkflowRuntime
✔ Editor visual (WorkflowUI.aspx)
✔ Inspectors JS
✔ doc.load (carga documental)
✔ Sistema de extracción con Regex
✔ Soporte items[] con itemBlock
✔ Soporte Template ${biz.*} con resolverPath mejorado
✔ Logger profesional
✔ WF_Instancia / WF_InstanciaLog
✔ Subflow (util.subflow)
✔ Cola SQL
✔ Delta tracking pendiente

Todo esto es infraestructura base y se mantiene.

---

# 4. MODELO FUTURO

## Entidad
- TipoEntidad (ej: NOTA_PEDIDO2)
- EstadoActual
- DataJson (resultado biz.*)
- Total
- Usuario
- Auditoría
- DocumentoOriginal
- DocumentoProcesado
- DocumentoFirmado

## Items
Guardados dinámicamente sin tablas nuevas por tipo.

---

# 5. POSICIÓN ESTRATÉGICA

Workflow = Motor de estados.
Entidad = Caso empresarial.
Apryse/iText = Potencia documental.

Objetivo:
Integrar SDK Apryse (.NET Framework 4.5.1 compatible con 4.8)
Incorporar:
- OCR industrial
- Visor embebido
- Conversión Word→PDF
- Firma digital
- Anotaciones
- Redacción
- Comparación

iText 9 para:
- Generación PDF avanzada
- Firma avanzada
- Certificación

Workflow Studio será la capa de procesos sobre Apryse.

---

# 6. FILOSOFÍA

No construir un proyecto.
Construir un producto.

No depender de DBA.
No crear tablas por cliente.
Permitir integración opcional.
Ser SaaS-friendly.
Ser multi-tenant ready.

---

# 7. PRÓXIMO PASO DEFINIDO

Implementar capa ENTIDAD sin romper lo existente.
NO eliminar motor.
NO eliminar runtime.
Agregar persistencia genérica interna.

---

FIN CONTEXTPACK