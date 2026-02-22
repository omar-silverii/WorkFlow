# Workflow Studio – Context Pack
## Estado del Proyecto – Febrero 2026

Autor: Omar Silverii  
Stack: ASP.NET WebForms (.NET 4.8) + SQL Server  
Base de datos única – connectionString: DefaultConnection  
Namespace runtime obligatorio: Intranet.WorkflowStudio.Runtime  

---

# 1. Principios del Proyecto

- Sistema profesional, genérico y reutilizable.
- No hardcodear casos específicos.
- No romper compatibilidad existente.
- Cambios mínimos, profesionales y coherentes.
- El usuario funcional NO debe escribir regex manualmente.
- Todo debe funcionar con UNA sola base y UNA sola conexión.

---

# 2. Arquitectura Actual del Motor

Tablas principales:
- WF_Definicion
- WF_Instancia
- WF_InstanciaLog
- WF_DocTipo
- WF_DocTipoReglaExtract

Convenciones de contexto:

- input.* → entrada cruda (archivo cargado)
- biz.{prefix}.* → modelo de negocio normalizado del documento
- payload.* → respuestas temporales de integraciones
- wf.* → datos internos del workflow

Ejemplo:
biz.np.numero  
biz.np.monto.estimado  

---

# 3. Field Registry Global

Se implementó:

window.WF_FieldPicker.open({...})

- Búsqueda por path
- Namespaces dinámicos
- UX profesional
- No hardcoded
- Migrado en control.if y otros inspectores

Objetivo:
Todos los nodos que usen campos deben usar el picker.

---

# 4. Nodo Simplificado: doc.load (Modo Simple)

Reemplaza:

- file.read
- util.docTipo.resolve
- doc.extract

Ahora:

doc.load:
- Lee archivo (.txt / .docx / .pdf)
- Extrae texto
- Carga input.*
- Resuelve DocTipo
- Aplica reglas
- Población automática de biz.{prefix}.*

Salidas:

input.filename
input.ext
input.text
input.hasText
input.textLen
input.modeUsed
input.sizeBytes

y

biz.{prefix}.*

---

# 5. Mejoras Técnicas Implementadas

## Word
ExtractWord devuelve texto con \r\n por párrafos reales.

## PDF
- No se usa DocumentLayoutAnalysis.
- Normalización manual:
  - Limpieza de espacios
  - Manejo de saltos
  - Unión de palabras con guión
  - Normalización CRLF

---

# 6. WF_DocTipoReglas.aspx

Objetivo:
El usuario selecciona texto y el sistema genera el regex.

Flujo:

1. Usuario selecciona texto en preview.
2. Se llena:
   - Ejemplo
   - HintContext
3. En el servidor:
   BuildRegex(tipoDato, ejemplo, hintContext)
4. Se guarda Regex generado.

NO se genera regex en SQL.
NO escribe regex el usuario.

Preview soporta:
- .txt
- .docx
- .pdf
(vía Api/DocPreview.ashx)

---

# 7. Problemas Detectados y Resueltos

✔ Word devolvía texto plano sin saltos → corregido  
✔ PDF requería namespace inexistente → corregido  
✔ Motivo se capturaba mal → corregido en BuildRegex  
✔ control.if migrado a FieldPicker  
✔ Namespace runtime unificado  

---

# 8. Problema Pendiente Crítico

Soporte de múltiples ítems en un documento.

Ejemplo:

Código:
Descripción:
Cantidad:
Monto:

repetidos varias veces.

Actualmente:
Solo soporta un único item.

Objetivo futuro:

Soportar:

biz.{prefix}.items[0].codigo
biz.{prefix}.items[0].descripcion
biz.{prefix}.items[0].cantidad

Implementación probable:
- Regex.Matches
- Reglas con modo "repetible"
- Guardar lista en biz.{prefix}.items[] sin romper campos simples

Compatibilidad obligatoria:
Reglas simples deben seguir funcionando igual.

---

# 9. Reglas de Trabajo

- NO inventar infraestructura inexistente.
- NO cambiar nombres existentes.
- NO proponer arquitectura nueva si no es necesario.
- Siempre trabajar sobre el ZIP real.
- Cambios mínimos y profesionales.
- Pensar UX primero.
- Simplificar siempre que sea posible.

---

# Estado Actual: Estable y funcional.
Siguiente etapa: Soporte de items[].
