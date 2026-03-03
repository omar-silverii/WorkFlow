# WORKFLOW STUDIO – CONTEXT PACK (MODO DEMO)

Autor: Omar Silverii  
Proyecto: Workflow Studio  
Stack: ASP.NET WebForms (.NET 4.8)  
Base: SQL Server (DefaultConnection única)  
Arquitectura: MotorFlujoMinimo + Runtime + Handlers + Inspectors JS  

---

# ESTADO ACTUAL DEL PROYECTO

## ✔ Motor funcionando
- util.start
- util.logger
- control.if
- control.switch
- control.delay
- util.subflow (con anti-recursión)
- data.sql
- doc.load (PDF/DOCX/TXT)
- doc.search
- doc.attach

## ✔ Persistencia
- WF_Definicion (JsonDef)
- WF_Instancia
- WF_InstanciaLog
- WF_DocTipo
- WF_DocTipoReglaExtract
- WF_Queue (si existe en esta versión)

## ✔ Pipeline documental
doc.load:
- Normaliza texto
- Pobla input.*
- Pobla biz.{prefijo}.*
- Maneja warnings
- Detecta PDF sin texto

## ✔ UI
- WorkflowUI.aspx (editor canvas)
- WF_Definiciones.aspx
- WF_Instancias.aspx
- WF_Gerente_Tareas.aspx
- WF_DocTipoReglas.aspx

---

# REGLAS INMUTABLES

❌ No tocar Entidades  
❌ No tocar Solo Activas  
❌ No tocar Total  
❌ No rehacer arquitectura  
❌ No inventar tablas  
❌ No cambiar nombres  
❌ No introducir Apryse  
❌ No introducir nuevas dependencias  

✔ Cambios mínimos  
✔ Código completo listo para pegar  
✔ Basado siempre en ZIP real  
✔ Profesional  
✔ Sin placeholders  

---

# OBJETIVO INMEDIATO

Preparar DEMO estable del sistema.

No innovar.
No experimentar.
No integrar SDKs.
No refactor masivo.

Solo asegurar estabilidad y discurso.

---

# ESTRATEGIA DEMO

Enfocar en:

1) Editor visual
2) Ingreso documento
3) Extracción a biz.*
4) Decisión (IF)
5) Logs
6) Vista de instancias

Mostrar:

- Trazabilidad
- Flexibilidad
- Reutilización
- No hardcode
- Persistencia en SQL
- Motor desacoplado

---

# PUNTOS SENSIBLES (NO ARRIESGAR)

- PDFs escaneados
- Condiciones complejas
- Queries SQL editables en vivo
- Subflows no probados hoy
- Switch con múltiples ramas nuevas
- Cambios en runtime antes de demo

---

# CHECKLIST PRE DEMO

- Compilar limpio
- Probar 1 documento exitoso
- Probar 1 documento con warning
- Abrir sesión IIS previamente
- Abrir pantallas necesarias
- Limpiar instancias de prueba viejas
- Crear 1 instancia “lista” para mostrar log

---

# FRASE CLAVE DE SEGURIDAD

"El sistema está diseñado para ser extensible sin romper lo existente."

---

# RECORDATORIO ARQUITECTÓNICO

- Inspectors JS definen UI
- Handlers C# ejecutan lógica
- Runtime coordina
- JSON define el flujo
- SQL persiste estado
- Nada está hardcodeado

---

# MODO ACTUAL

Demo Stability Mode.