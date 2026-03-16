CONTEXTPACK — Workflow Studio
Cierre de etapa exitoso / retoma futura
1. Estado general al cierre de esta etapa

Workflow Studio está funcionando en los servidores de la empresa.

Se hizo el recorrido completo:

desarrollo/pruebas desde casa

generación de scripts

migración lógica

puesta en el entorno de trabajo

validación operativa exitosa

Migración SQL resuelta

La migración desde:

SQL Server Express 16.x

hacia:

SQL Server 2016

quedó resuelta mediante:

Generate Scripts

un script de Schema only

un script de Data only

No se usó:

attach

restore de backup a versión más vieja

La línea correcta para repetir este proceso, si vuelve a ser necesario, es:

migración lógica por scripts

apuntando explícitamente a SQL Server 2016

y enviando los .sql por internet al otro entorno

Resultado actual

la base quedó operativa

el sistema quedó funcionando en los servidores de la empresa

la migración a SQL 2016 puede considerarse resuelta para este caso

2. Estado funcional importante ya consolidado
FACTURA_ELECTRONICA_AR

Esto quedó funcionando y debe considerarse cerrado salvo bug real o nuevos comprobantes reales.

Línea técnica correcta ya tomada

seguir usando doc.load

usar WF_DocTipo.MotorExtraccion

valores:

REGLAS

FACTURA_AR

si MotorExtraccion = FACTURA_AR, ejecutar extractor especial

DocTipo ya creado

Codigo = FACTURA_ELECTRONICA_AR

Nombre = Factura electrónica AFIP

ContextPrefix = factura

MotorExtraccion = FACTURA_AR

Archivos relevantes ya encaminados y no tocar a ciegas

WF_DocTipo.aspx

WF_DocTipo.aspx.cs

WF_DocTipo.aspx.designer.cs

DocumentProcessing/HDocLoad.cs

.csproj

Foco real de esa línea

DocumentProcessing/FacturaElectronicaArExtractor.cs

Estado validado

Se validó correctamente una factura PDF real tipo C, incluyendo:

cabecera

CAE

fechas

emisor

receptor

importes

items

itemsCount

validacionBasicaOk

Regla para el futuro

No revisar más FACTURA_ELECTRONICA_AR salvo que ocurra una de estas tres cosas:

aparezca un bug real

llegue un comprobante nuevo real

haga falta ampliar casuística por nuevas variantes reales

3. Temas que NO deben reabrirse sin pedido explícito
RBAC por alcance

Este tema:

NO está resuelto

NO debe seguir implementándose ahora

NO reabrir salvo pedido explícito de Omar

4. Regla principal de trabajo al retomar
Siempre manda el ZIP real

Si hay diferencia entre:

lo hablado en chat

lo que “parece”

lo que quedó en memoria

y lo que está en el ZIP

manda siempre el ZIP real.

Reglas obligatorias

no inventar nada

no cambiar arquitectura sin necesidad

no reescribir cosas que ya funcionan

hacer cambios mínimos y precisos

trabajar siempre sobre código real del ZIP

no mandar “archivo completo” si no se verificó el archivo real actual

no asumir que el código quedó exactamente como en el chat

preferir formato:

DONDE DICE → REEMPLAZAR POR

si algo no cierra, decirlo

si falta una pieza, pedirla

no mezclar teoría larga con código si se corrige algo puntual

proponer de entrada la mejor opción

no volver atrás sobre temas ya cerrados

cuando algo ya funciona, no discutirlo otra vez salvo bug real del ZIP

5. Estado técnico/profesional del proyecto a recordar

Workflow Studio debe seguir construyéndose como un sistema:

serio

general

profesional

reutilizable

apto para uso real en empresa

Criterio de avance:

despacio y seguro

sin romper lo que ya funciona

priorizando estabilidad sobre apuro

siempre con visión de producto digno y utilizable

6. Líneas futuras que siguen abiertas
Línea 1 — nuevos comprobantes de factura

Retomar cuando Omar consiga comprobantes reales de:

factura A

factura B

múltiples ítems

percepciones / otros tributos reales

layouts reales distintos

Línea 2 — compatibilidad SQL Server 2005

Queda pendiente revisar la compatibilidad/migración hacia SQL Server 2005 cuando Omar aporte:

errores reales

scripts reales

o escenario concreto de migración

Ya detectado previamente:

datetime2(0) rompe en 2005

pueden romper también funciones/elementos modernos como:

TRY_CONVERT

CONCAT

JSON_VALUE

ISJSON

JSON_QUERY

OPTIMIZE_FOR_SEQUENTIAL_KEY

Línea 3 — evolución futura del producto

Sin decidir ahora el orden, quedan como universo de trabajo futuro:

mejoras de UX operativa

evolución de tareas humanas

mejoras de backtrack

robustez documental

documentación funcional/técnica de nodos

mejoras vendibles del producto

Pero no elegir automáticamente una línea nueva sin que Omar lo indique al arrancar.

7. Estado emocional/proyecto al cierre

Este cierre de etapa es importante porque:

el sistema ya corrió en servidores reales de la empresa

la migración salió bien

la línea de factura electrónica quedó estable

se confirmó que el proyecto puede avanzar de forma seria

El criterio para la próxima etapa debe seguir siendo:
calma, precisión, y construcción profesional.