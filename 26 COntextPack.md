CONTEXTPACK — Workflow Studio / cierre de sesión actual
Estado general

Se trabajó sobre dos líneas principales:

RBAC por alcance (proceso/sector)

Extractor especial para factura electrónica argentina

1. RBAC por alcance — estado actual
Qué se confirmó

El RBAC base por página ya funciona.

Existe administración de:

usuarios

roles

permisos

asignaciones

Se agregó una primera aproximación visual para “rol → alcances”.

Qué quedó claro

Este tema NO está resuelto funcionalmente y NO debe seguir implementándose sin análisis previo.

Problema detectado

La idea actual de “proceso / sector / alcance” quedó confusa:

no termina de definirse qué es Proceso para el usuario

no termina de definirse qué es Sector

no está claro si el comportamiento sería:

inclusión

exclusión

o ambas

tampoco quedó clara la UX correcta:

qué combos mostrar

qué valores deberían aparecer

si deben verse todos los permisos o solo los ya asignados

si el alcance aplica al rol, al usuario, o a ambos

Decisión cerrada

No seguir con RBAC por alcance por ahora.
Dejarlo pendiente para futura sesión / futuro ContextPack con análisis funcional más profundo.

2. Factura electrónica argentina — nueva línea de trabajo
Contexto

Se conversó que en el país del usuario las facturas electrónicas podrían representar aproximadamente el 80% de las capturas del sistema.

Conclusión tomada

Sí conviene avanzar con un extractor especial para ese tipo documental.

Decisión importante

No crear un nodo nuevo aparte.
La mejor opción acordada fue:

seguir usando doc.load

pero permitir que el DocTipo defina qué motor de extracción usa

3. Nuevo DocTipo acordado
DocTipo propuesto

Código: FACTURA_ELECTRONICA_AR

Nombre: Factura electrónica AFIP

ContextPrefix: factura

Activo: sí

Criterio importante

Este DocTipo debe ir a un extractor especial, no al sistema normal de reglas/regex.

4. Cambio de arquitectura acordado para DocTipos
Se definió agregar a WF_DocTipo

Una columna nueva:

MotorExtraccion

Valores previstos

REGLAS

FACTURA_AR

Comportamiento esperado

si MotorExtraccion = REGLAS
→ funciona como hoy, con reglas/regex

si MotorExtraccion = FACTURA_AR
→ doc.load debe llamar a un extractor especial para factura electrónica argentina

Importante

Esta decisión fue tomada para:

no romper nada de lo existente

no reemplazar regex actual

mantener compatibilidad total

permitir crecer después con más motores especiales

5. Archivos / zonas a tocar en la próxima sesión
Base de datos

WF_DocTipo

agregar columna MotorExtraccion

UI / ABM

WF_DocTipo.aspx

WF_DocTipo.aspx.cs

WF_DocTipo.aspx.designer.cs

Runtime

DocumentProcessing/HDocLoad.cs

Nuevo archivo

DocumentProcessing/FacturaElectronicaArExtractor.cs

Proyecto

Intranet.WorkflowStudio.WebForms.csproj

agregar Compile Include del nuevo extractor

6. Enfoque acordado para el extractor
No usar “IA libre”

La idea no es un modelo genérico que “invente”.

Enfoque acordado

Construir un extractor especial controlado, apoyado sobre:

texto extraído

estructura del documento

anclas estables

validaciones

deduplicación de copias ORIGINAL / DUPLICADO / TRIPLICADO

salida JSON estructurada

items[]

Aclaración importante

Se aclaró al usuario que el JSON mostrado en el chat fue ilustrativo/manual, no resultado de un extractor ya funcionando.

7. Requisito funcional importante del usuario

Para facturas:

los campos a recuperar siempre son todos

no solo algunos

incluyendo:

cabecera

emisor

receptor

CAE

fechas

importes

totales

y todos los items[]

8. V1 del extractor especial

Se propuso una V1 mínima que recupere, como base:

tipo de comprobante

letra

código AFIP

punto de venta

número de comprobante

fecha de emisión

período facturado

fecha de vencimiento de pago

emisor

receptor

condición IVA

CAE

vencimiento CAE

subtotal

otros tributos

total

items[]

warnings

confidence simple

Importante

Todavía no quedó cerrada su implementación real dentro del ZIP porque el chat se mezcló / se degradó.

9. Problema operativo de esta sesión

Hubo degradación del chat:

respuestas mezcladas

código mezclado en el chat

y además apareció error de que la sesión del intérprete había caducado

Regla práctica para retomar

Conviene:

cerrar esta sesión

abrir una nueva

pasar:

este ContextPack

el ZIP actualizado del proyecto

10. Reglas de trabajo que siguen vigentes

trabajar siempre sobre código real del ZIP

no inventar

cambios mínimos y precisos

no reescribir lo que ya funciona

no mandar “archivo completo” si no se verificó el archivo real actual

preferir formato:

DONDE DICE → REEMPLAZAR POR

si algo no cierra, decirlo

si falta una pieza, pedirla

no reabrir decisiones ya cerradas sin bug real del ZIP

11. Qué quedó pendiente inmediato para próxima sesión
Tema A — prioritario

Retomar el extractor especial de factura electrónica argentina:

revisar ZIP real actualizado

confirmar estado real de:

WF_DocTipo.aspx

WF_DocTipo.aspx.cs

HDocLoad.cs

.csproj

aplicar los cambios mínimos para:

MotorExtraccion

branch en doc.load

extractor especial FACTURA_AR

probar con PDF real

Tema B — NO continuar ahora

RBAC por alcance proceso/sector
Queda congelado hasta análisis funcional posterior.