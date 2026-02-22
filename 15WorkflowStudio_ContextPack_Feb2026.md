# Workflow Studio – Context Pack (Febrero 2026)
Autor: Omar Silverii
Proyecto: Workflow Studio (Intranet)
Stack: ASP.NET WebForms (.NET 4.8) + SQL Server + JS + Bootstrap

---

# 1. OBJETIVO ACTUAL

Implementar correctamente extracción estructurada de documentos (DocTipo),
con soporte para:

- Campos únicos (empresa, numero, fecha, solicitante, sector, motivo, aprobada)
- Bloques repetibles items[]
- Regla especial items[].__block (ItemBlock)
- Extracción robusta de múltiples ítems por documento

Este punto es CRÍTICO para el sistema.

---

# 2. ARQUITECTURA GENERAL

## 2.1 Backend

- ASP.NET WebForms (.NET 4.8)
- C#
- SQL Server
- Una sola conexión: DefaultConnection
- No múltiples bases
- Windows Auth

## 2.2 Tablas relevantes

WF_DocTipo
WF_DocTipoReglas
WF_Definicion
WF_Instancia
WF_InstanciaLog

WF_DocTipoReglas contiene:
- Id
- DocTipoCodigo
- Campo
- Regex
- Grupo
- Orden
- Activo
- TipoDato
- Ejemplo
- HintContext
- Modo (Assisted)

---

# 3. ESTADO ACTUAL DE DOC TIPO

## 3.1 UI

WF_DocTipoReglas.aspx ya estabilizado:
- Layout correcto
- No problemas CSS
- No problemas checkbox
- Topbar corregido
- Bootstrap consistente
- ItemBlock UI implementado
- Grupo=0 automático para ItemBlock

No tocar CSS nuevamente.

---

# 4. BuildRegex()

Actualmente existe:

private string BuildRegex(string tipoDato, string ejemplo, string hintContext)

Genera regex por contexto.
Funciona correctamente para campos únicos.

Limitación:
No distingue ItemBlock porque no recibe "campo".

Pendiente:
Modificar firma a:

BuildRegex(string campo, string tipoDato, string ejemplo, string hintContext)

Y si campo == "items[].__block"
devolver regex multilínea fija para bloque.

---

# 5. PROBLEMA ACTUAL (CRÍTICO)

items[].codigo
items[].descripcion
items[].cantidad
items[].monto

Se están ejecutando contra TODO el documento,
no contra cada bloque de ítem.

Resultado:
- Devuelve valores inconsistentes
- Toma match del segundo ítem en algunos casos
- No arma array items[] correctamente

Conclusión:
El motor NO está haciendo:

Regex.Matches() sobre items[].__block
+
Iteración por bloque

---

# 6. COMPORTAMIENTO DESEADO

Proceso correcto:

1. Ejecutar reglas únicas normalmente (Match sobre texto completo)

2. Si existe regla items[].__block:
   - Ejecutar Regex.Matches() sobre texto completo
   - Por cada bloque:
       - Ejecutar reglas items[].campo dentro del bloque
       - Armar Dictionary<string, object>
       - Agregar a List<Dictionary<string, object>>

3. Guardar resultado en:

biz["items"] = List<Dictionary<string, object>>

---

# 7. REGEX ITEMBLOCK DEFINIDO

Regex recomendado:

(?ms)^\s*Item solicitado:\s*\r?\n.*?(?=^\s*Item solicitado:\s*\r?\n|^\s*Motivo:\s*\r?\n|^\s*Aprobación requerida:\s*\r?\n|\z)

Grupo=0 (match completo)

---

# 8. REGEX CAMPOS INTERNOS DE ITEM

Aplicados SOBRE EL BLOQUE:

Código:
(?m)^\s*Código\s*:\s*([^\r\n]+)

Descripción:
(?m)^\s*Descripción\s*:\s*([^\r\n]+)

Cantidad:
(?m)^\s*Cantidad\s*:\s*([^\r\n]+)

Monto:
(?m)^\s*Monto\s*Estimado\s*:\s*([^\r\n]+)

---

# 9. WORKFLOW DE PRUEBA

Workflow mínimo para validar:

util.start
doc.load (DocTipo = NOTA_PEDIDO)
util.logger (log biz.items)

Debe mostrar:

biz.items = [
  { codigo:..., descripcion:..., cantidad:..., monto:... },
  { ... }
]

---

# 10. REGLAS DE ORO DEL PROYECTO

- NO cambiar arquitectura
- NO renombrar tablas
- NO rehacer motor
- Cambios mínimos y profesionales
- Siempre trabajar sobre código real del ZIP
- No suposiciones
- No inventar infraestructura nueva

---

# 11. ESTADO FINAL ANTES DE CERRAR SESIÓN

UI estable
DocTipoReglas estable
BuildRegex funcional
ItemBlock creado en UI
Motor aún NO soporta iteración por bloque

PRÓXIMO PASO:
Modificar motor de extracción para soportar items[].__block