PROMPT DE ARRANQUE — próxima sesión Workflow Studio

Voy a pasarte:

este ContextPack

el ZIP actualizado del proyecto

Quiero que trabajes así, sin desviarte.

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

Estado funcional importante a recordar
1. Workflow Studio ya está funcionando en la empresa

El sistema ya fue llevado con éxito al entorno de trabajo y está funcionando en los servidores de la empresa.

2. Migración SQL 2016 ya resuelta

La migración desde SQL Express 16.x a SQL Server 2016 quedó resuelta mediante scripts:

Schema only

Data only

No reabrir esta línea salvo que aparezca un nuevo caso real o un problema concreto.

3. RBAC por alcance

Este tema NO está resuelto y NO debe seguir implementándose ahora.
No reabrirlo salvo pedido explícito mío.

4. Factura electrónica argentina

La línea correcta ya tomada es:

seguir usando doc.load

usar WF_DocTipo.MotorExtraccion

valores:

REGLAS

FACTURA_AR

si FACTURA_AR, ejecutar extractor especial

5. DocTipo ya creado

Existe el DocTipo:

Codigo = FACTURA_ELECTRONICA_AR

Nombre = Factura electrónica AFIP

ContextPrefix = factura

MotorExtraccion = FACTURA_AR

6. Estado real importante

En el ZIP ya estaba encaminado:

WF_DocTipo.aspx / .cs / .designer.cs

DocumentProcessing/HDocLoad.cs

.csproj

No volver a tocar eso salvo bug real.
El trabajo real de esta etapa estuvo y está en:

DocumentProcessing/FacturaElectronicaArExtractor.cs

Resultado ya validado y que tenés que respetar

FACTURA_ELECTRONICA_AR ya está funcionando y no hace falta revisarlo más salvo:

bug real

nuevos comprobantes reales

ampliación de casuística necesaria

Líneas hoy abiertas para el futuro
Línea 1

seguir con nuevos comprobantes reales de factura cuando yo los consiga

Línea 2

retomar análisis de compatibilidad/migración SQL Server 2005 cuando yo te pase errores o scripts adicionales

Línea 3

continuar evolución general de Workflow Studio, pero solo sobre la línea que yo te indique al arrancar

Cómo quiero que trabajes la próxima sesión

leer el ContextPack

esperar el ZIP

revisar el código real

decir qué encontraste realmente

decir qué ya está, qué falta y qué quedó a medio aplicar

proponer cambios mínimos

dar instrucciones en formato claro, idealmente:

DONDE DICE → REEMPLAZAR POR

Importante

Si hay duda entre “lo que estaba en el chat” y “lo que está en el ZIP”, manda siempre el ZIP real.