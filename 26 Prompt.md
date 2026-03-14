PROMPT — Arranque nueva sesión Workflow Studio / Factura electrónica AR

Voy a pasarte:

este ContextPack

el ZIP actualizado del proyecto

Necesito que trabajes así, sin desviarte:

Paso 1

Confirmá que leíste completo el ContextPack.

Paso 2

Esperá el ZIP antes de proponer cambios.

Paso 3

Cuando tengas el ZIP, revisá primero los archivos reales y recién después proponé modificaciones.

Reglas obligatorias

no inventar nada

no cambiar arquitectura sin necesidad

no reescribir cosas que ya funcionan

cambios mínimos y precisos

siempre sobre código real del ZIP

no mandar “archivo completo” si no verificaste el archivo real actual

no asumir que el código quedó exactamente como en el chat

preferir formato: DONDE DICE → REEMPLAZAR POR

si algo no cierra, decilo

si falta una pieza, pedila

no mezclar teoría larga con código si estamos corrigiendo algo puntual

proponer siempre la mejor versión de entrada

no volver atrás sobre temas ya cerrados

cuando algo ya funciona, no discutirlo otra vez salvo bug real del ZIP

Estado funcional importante que debés recordar
1. RBAC por alcance

Este tema NO está resuelto y NO debe seguir implementándose ahora.

No reabrirlo salvo que yo te lo pida explícitamente.

Quedó pendiente porque:

no está bien definido qué significa Proceso para el usuario

no está bien definido qué significa Sector

no está claro si la lógica es inclusión, exclusión o ambas

la UX todavía no está cerrada

2. Nuevo foco de trabajo

La prioridad ahora es:

crear soporte para un extractor especial de factura electrónica argentina

Decisiones ya tomadas sobre este tema
DocTipo nuevo

Debemos trabajar con un DocTipo nuevo:

Código: FACTURA_ELECTRONICA_AR

Nombre: Factura electrónica AFIP

ContextPrefix: factura

Arquitectura elegida

No crear un nodo nuevo aparte.

La decisión correcta ya tomada es:

seguir usando doc.load

agregar en WF_DocTipo una columna:

MotorExtraccion

usar valores:

REGLAS

FACTURA_AR

Comportamiento esperado

si MotorExtraccion = REGLAS

sigue funcionando todo como hoy

si MotorExtraccion = FACTURA_AR

doc.load debe usar un extractor especial de factura argentina

Archivos que probablemente haya que revisar primero

WF_DocTipo.aspx

WF_DocTipo.aspx.cs

WF_DocTipo.aspx.designer.cs

DocumentProcessing/HDocLoad.cs

Intranet.WorkflowStudio.WebForms.csproj

Y probablemente crear:

DocumentProcessing/FacturaElectronicaArExtractor.cs

Criterio para el extractor especial

No quiero una “IA libre” que invente cosas.

Quiero una solución profesional y controlada:

basada en texto

estructura

anclas estables

validaciones

deduplicación ORIGINAL / DUPLICADO / TRIPLICADO

salida estructurada

items[]

Requisito funcional importante

En factura electrónica:

los campos a recuperar son todos
incluyendo:

cabecera

emisor

receptor

CAE

fechas

importes

totales

items[]

Cómo quiero que respondas cuando tengas el ZIP
1. Diagnóstico

Qué encontraste realmente en el código actual del ZIP.

2. Estado

Qué ya está, qué falta, qué quedó a medio aplicar.

3. Propuesta mínima

Qué archivos y métodos hay que tocar, sin romper lo ya existente.

4. Cambios

En formato claro, idealmente:

DONDE DICE → REEMPLAZAR POR

Prioridad al arrancar la próxima sesión

Tu prioridad será:

revisar el estado real del ZIP

confirmar el estado actual de DocTipos y doc.load

seguir con el soporte de:

MotorExtraccion

FACTURA_AR

extractor especial de factura electrónica AR

Y no volver al tema de RBAC por alcance salvo pedido explícito mío.