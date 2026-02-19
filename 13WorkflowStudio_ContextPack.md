# Workflow Studio — Context Pack (Omar Silverii) — 2026-02-15

## Objetivo del producto (visión)
Workflow Studio es un motor de workflows empresarial, usable principalmente por **personal administrativo** (no programadores), pero con potencia “enterprise”.
- El editor es visual (toolbox + canvas + inspector).
- Los usuarios deben completar propiedades con controles simples (combos, inputs, etc.).
- El sistema NO debe depender de que el usuario escriba scripts o expresiones complejas.
- Para la demo quedan ~5 días: priorizar consistencia, UX y funcionamiento real.

## Stack / Tech
- ASP.NET WebForms (.NET 4.8) en C#.
- SQL Server (una sola BD, una sola connectionString: **DefaultConnection**).
- UI con Bootstrap local.
- Motor runtime con handlers por tipo de nodo.

## Tablas clave mencionadas (actuales/relacionadas)
- WF_Definicion (JsonDef).
- WF_Instancia.
- WF_InstanciaLog.
- (Auditoría de documentos por instancia) WF_InstanciaDocumento (ya usada para grilla de auditoría).

## Páginas clave del sistema
- WorkflowUI.aspx: editor visual, usa hfWorkflowJson y __doPostBack('WF_SAVE','') para guardar.
- WF_Definiciones.aspx: lista definiciones y ver JSON.
- WF_Instancias.aspx: lista/filtra instancias, ver Datos/Logs, reejecutar, y mostrar auditoría de documentos.
- WF_Instancias.aspx (ya corregida UX): cards “Datos”, “Logs”, “Docs (DatosContexto)”, “Auditoría (WF_InstanciaDocumento)”. Oculta cards vacías.

## Reglas de trabajo acordadas (muy importantes)
1) **NO inventar** ni contradecir decisiones anteriores.
2) Cambios mínimos y profesionales; no re-arquitecturar.
3) **No enviar “reemplazar archivo completo”** si no se leyó el archivo REAL/último del ZIP.
4) Si falta el ZIP o el archivo real: pedirlo.
5) Mantener nombres/convenios existentes (claves de nodos, rutas, tablas, connectionString).

---

# Estado actual de nodos / handlers

## doc.load (IMPLEMENTADO y probado)
Se implementó `doc.load` para cargar archivo local y extraer texto.
- Extrae texto de:
  - `.txt` (UTF-8)
  - `.docx` (OpenXml)
  - `.pdf` (PdfPig) — si es escaneo, puede quedar sin texto y devuelve warning.
- Outputs en `payload` (u outputPrefix elegido):
  - payload.filename
  - payload.ext
  - payload.sizeBytes
  - payload.text
  - payload.hasText (bool)
  - payload.textLen (int)
  - payload.warning (string si aplica)
  - payload.error (string si aplica)
- Logs vistos:
  - TXT OK con texto.
  - PDF OK pero `warning=PDF sin texto extraíble (posible escaneo / imagen).`
  - DOCX OK con texto.

## doc.search (EXISTE pero NO se integra aún)
Hay un `doc.search` previo que apunta a HTTP (DMS) y normaliza resultados.
Pero se decidió:
- **Pausar/NO cerrar implementación final** hasta encajar con datos reales del EDMS.
- Se hizo una demo alternativa “DB mode” que devolvió items (0 y 8) para pruebas.

## Nodos que faltan (según Omar)
- doc.load (listo)
- doc.search (en revisión según EDMS)
- doc.load (listo)
- code.function
- code.script
- state.vars
- doc.load ya está, doc.attach ya estaba diseñado en conversaciones previas, doc.search se estaba diseñando.

---

# Problema crítico descubierto: Consistencia JSON ↔ Inspector ↔ Motor

## Síntoma
- “El JSON NO cambia lo que está en el inspector”
- Parámetros editados en inspector no coinciden con lo que se exporta/guarda en JSON y viceversa.

## Causa probable (contrato inconsistente)
En JSON los nodos tienen:
- `Parameters`: {...}

En varios inspectors JS se usa:
- `node.params` (y guardan `node.params = next`)

Si el import/export del editor NO mapea `Parameters <-> params`, se rompe la sincronía.

## Regla de oro decidida
**Todas las variables del JSON deben cargarse en el Inspector y viceversa.**
Esto requiere:
- Definir un contrato único en UI.
- Mapeo consistente al exportar/importar JSON.
- Evitar que existan dos nombres (Parameters y params) sin sincronización.

---

# control.if — situación actual (motor y UX)

## Requisitos de negocio
- Todo debe ser usable por administrativos.
- El IF no debe verse como “para admins”, ni pedir expresiones complejas.
- Debe tener:
  - Combo de operadores: =, !=, <, <=, >, >=
  - Operadores de texto: contains / not_contains / starts_with / ends_with
  - Operadores de existencia: exists / not_exists / empty / not_empty
- El usuario debe seleccionar campo + operador + valor (cuando aplique). Nada de funciones.

## Motor actual: HIf.cs
- Soporta 2 modos:
  1) **Modo simple**: `field`, `op`, `value`
  2) **Modo legacy**: `expression` tipo `${path} OP rhs` (solo comparaciones, sin funciones)
- Hallazgo: expresiones como `contains(lower(...), 'omar')` NO están soportadas por el motor legacy.
- Se observó en logs que:
  - `${payload.score} >= 700` devolvía False aún siendo 750 (hay bug o conversión/valores; se debe revisar).
  - `contains(lower(${payload.nombre}), 'omar')` daba False aunque nombre era “Omar Silverii” (porque legacy no soporta funciones).

## Inspector actual (captura)
Se ve el inspector nuevo de control.if con:
- Campo / Operador / Valor (modo simple)
- “Modo avanzado (solo admins)” que permite expresión legacy
Pero el inspector está generando/mostrando legacy con funciones (contains/lower) que el motor NO soporta.
=> Hay que alinear inspector con motor: el modo simple debe ser el recomendado para administrativos.

---

# control.switch — error actual
El error reportado NO era de if, sino de switch.

Código actual de switch (`ManejadorSwitch`) usa:
- `bool ok = HIf.Evaluar(expr, ctx);`
Pero en un momento se cambió firma (o se intentó) a `Evaluar(string, ContextoEjecucion, out string logExpr)` y ahora switch quedó desfasado.

## Síntoma
- Error compilación: “No se ha dado ningún argumento que corresponda al parámetro requerido logExpr ...”
=> Switch debe actualizarse para llamar a la firma correcta o usar un wrapper.

## Además
Omar no encuentra el inspector de `control.switch`.
Es probable que falte el archivo `Scripts/Inspectors/inspector.control.switch.js` o esté con otra ruta/nombre.

---

# Pruebas / JSONs usados

## if_full_graph.json (grafo completo)
Se armó un grafo con múltiples IF:
- IF1 status == 200
- IF2 score >= 700
- IF3 nombre contains 'omar'
- IF4 observacion empty
- IF5 tags exists
- IF6 ciudad starts_with 'Remedios'
Y logs para cada rama.

## if_full_input.json (input)
Se usa file.read para cargar JSON en `payload`:
- payload.status = 200
- payload.score = 750
- payload.nombre = "Omar Silverii"
- payload.tags = array
- payload.ciudad = "Remedios de Escalada"
- payload.observacion = "" (vacío)

## Resultado observado
- IF1 True OK
- IF2 incorrectamente False (con 750 >= 700)
- IF3 False (legacy functions; o simple mal mapeado)
- IF4 False aunque observacion vacío (posible tratamiento)
- IF5 False aunque tags existe (en log aparecía System.Object[]; exists debería dar true si no null)
- IF6 False aunque ciudad empieza con Remedios

Esto indica:
- o el IF está evaluando contra valores distintos a los esperados
- o hay problemas de types/conversion/path resolver
- o el editor/inspector no está guardando parámetros donde el motor lee (Parameters vs params)
=> Prioridad: arreglar contrato de params y re-probar.

---

# Decisiones de producto (muy importantes)
1) El producto apunta a administrativos, por lo tanto:
   - Los nodos deben tener UI simple y guiada.
   - Evitar exponer nodos tipo `code.script`/`code.function` a usuarios normales en el catálogo.
2) Si hay capacidades “avanzadas”, deben quedar:
   - ocultas por rol (admin/IT)
   - o encapsuladas en nodos “business” (ej: “Validar documento”, “Calcular prioridad”, “Buscar cliente”), no scripts libres.
3) No se debe construir algo sabiendo que “va a duplicar” o “va a funcionar mal”.
   - Se puede hacer modo demo, pero debe estar rotulado/aislado, no dejarlo como solución final.

---

# Próximo foco (alta prioridad para la demo)
## P0 — Consistencia editor JSON ↔ inspector ↔ motor
- Definir estándar interno:
  - UI usa `node.params`
  - JSON export/import usa `Parameters`
  - mapeo automático en load/save (sin depender de cada inspector)
- Objetivo: Lo que se ve en inspector sea lo mismo que se guarda en SQL.

## P0 — Reparar control.if (comparaciones fallando)
- Validar que el motor esté leyendo los parámetros correctos (field/op/value).
- Validar ResolverPath y tipos numéricos (750 >= 700).
- Validar exists/empty sobre string vacío y arrays.
- Ajustar logs del IF para dejar evidencia (left/right/op).
- El inspector NO debe generar expresiones legacy no soportadas.

## P0 — Reparar control.switch (firma Evaluar / logExpr)
- Ajustar ManejadorSwitch para la firma actual del IF.
- Crear/confirmar inspector de switch.

## P1 — Catálogo para administrativos (ocultar nodos técnicos)
- workflow.catalog.js: definir visibilidad por rol o “Advanced”.
- Sacar del catálogo para usuarios:
  - code.script
  - code.function
  - state.vars si no tiene UX simple (o dejarlo pero bien amigable)
- Mantener nodos business: doc.load, doc.search, doc.attach, doc.extract, human.task, if, switch, etc.

---

# Archivos / rutas mencionadas recientemente
- Scripts/workflow.catalog.js (catálogo de nodos e íconos)
- Scripts/Inspectors/inspector.control.if.js (nuevo inspector mostrado en imagen)
- App_Code/Handlers/HIf.cs
- Handler switch actual: clase `ManejadorSwitch` (control.switch)
- WF_Instancias.aspx.cs (código de instancia; ya estable y funcionando)

---

# Notas finales para la próxima sesión
- Omar va a generar un ZIP nuevo y lo va a subir en nueva sesión.
- En nueva sesión, pedir/leer:
  1) ZIP completo actualizado
  2) (si existe) los inspectors actuales
  3) (si existe) el pipeline de import/export JSON del editor para corregir `params` vs `Parameters`
- Trabajar con cambios mínimos y siempre sobre archivos reales del ZIP.
