# Prompt de arranque — próxima sesión Workflow Studio

Voy a pasarte:
1. este ContextPack
2. el ZIP actualizado del proyecto

Quiero que trabajes así, sin desviarte.

## Paso 1
Confirmá que leíste completo el ContextPack.

## Paso 2
Esperá el ZIP antes de proponer cambios.

## Paso 3
Cuando tengas el ZIP, revisá primero los archivos reales y recién después proponé modificaciones.

## Reglas obligatorias
- no inventar nada
- no cambiar arquitectura sin necesidad
- no reescribir cosas que ya funcionan
- cambios mínimos y precisos
- siempre sobre código real del ZIP
- no mandar “archivo completo” si no verificaste el archivo real actual
- no asumir que el código quedó exactamente como en el chat
- preferir formato: **DONDE DICE → REEMPLAZAR POR**
- si algo no cierra, decilo
- si falta una pieza, pedila
- no mezclar teoría larga con código si estamos corrigiendo algo puntual
- proponer siempre la mejor versión de entrada
- no volver atrás sobre temas ya cerrados
- cuando algo ya funciona, no discutirlo otra vez salvo bug real del ZIP

## Estado funcional importante a recordar

### 1. RBAC por alcance
Este tema **NO está resuelto** y **NO debe seguir implementándose ahora**.  
No reabrirlo salvo pedido explícito mío.

### 2. Factura electrónica argentina
La línea correcta ya tomada es:

- seguir usando `doc.load`
- usar `WF_DocTipo.MotorExtraccion`
- valores:
  - `REGLAS`
  - `FACTURA_AR`
- si `FACTURA_AR`, ejecutar extractor especial

### 3. DocTipo ya creado
Existe el DocTipo:
- `Codigo = FACTURA_ELECTRONICA_AR`
- `Nombre = Factura electrónica AFIP`
- `ContextPrefix = factura`
- `MotorExtraccion = FACTURA_AR`

### 4. Estado real importante
En el ZIP ya estaba encaminado:
- `WF_DocTipo.aspx` / `.cs` / `.designer.cs`
- `DocumentProcessing/HDocLoad.cs`
- `.csproj`

No volver a tocar eso salvo bug real.  
El trabajo real de esta etapa estuvo y está en:

- `DocumentProcessing/FacturaElectronicaArExtractor.cs`

## Resultado ya validado y que tenés que respetar
Se validó correctamente una factura PDF real tipo C.

Resultado correcto ya probado:

### Cabecera
- tipo `C`
- letra `C`
- número `00003-00000097`
- puntoVenta `00003`
- numeroComprobante `00000097`
- fecha `30/05/2023`
- periodoDesde `01/03/2023`
- periodoHasta `30/04/2023`
- vencimientoPago `31/05/2023`
- CAE `73229835423122`
- caeVencimiento `09/06/2023`
- validacionBasicaOk `True`

### Emisor
- nombre `SILVERII OMAR DARIO`
- cuit `23175875379`
- condicionIva `Responsable Monotributo`
- ingresosBrutos `exento`
- fechaInicioActividades `01/03/2008`
- direccion `Roca 865 - Remedios De Escalada, Buenos Aires`

### Receptor
- nombre `EDI SA`
- cuit `30656893830`
- condicionIva `IVA Responsable Inscripto`
- direccion `Tucuman 540 Piso:4 Dpto:D - Capital Federal, Ciudad de Buenos Aires`

### Importes
- subtotal `232050.00`
- otrosTributos `0.00`
- total `232050.00`

### Items
- itemsCount `1`
- descripcion `Honorarios por Programacion`
- cantidad `1.00`
- unidadMedida `unidades`
- precioUnitario `232050.00`
- bonificacionPorcentaje `0.00`
- bonificacionImporte `0.00`
- subtotal `232050.00`

## Regla importante sobre FACTURA_AR
La versión actual del extractor debe considerarse **base estable para esta muestra**.

No tocar a ciegas:
- parseo de número
- parseo de CAE
- parseo de fecha
- parseo de items
- importes
- emisor/receptor de esta muestra

Solo cambiar algo si:
- el ZIP real muestra otra cosa
- aparece un bug real
- o se trae un comprobante nuevo que obliga a ampliar casuística

## Punto pendiente de facturas
Aún no hay modelos reales adicionales.

Cuando los tenga, habrá que probar:
- factura A
- factura B
- múltiples ítems
- percepciones / otros tributos reales
- variantes reales de layout

## Segundo tema pendiente: SQL Server
Quedó pendiente revisar compatibilidad/migración desde:
- SQL Server Express 16.0.1170.5

hacia:
- SQL Server 2016
- SQL Server 2005

### Lo ya detectado
- `datetime2(0)` rompe en SQL 2005
- `datetime2(0)` no debería romper en SQL 2016
- el script revisado usa `OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF`, que sí puede romper en 2016
- en 2005 además rompen cosas como:
  - `TRY_CONVERT`
  - `CONCAT`
  - `JSON_VALUE`
  - `ISJSON`
  - `JSON_QUERY`
  - `OPTIMIZE_FOR_SEQUENTIAL_KEY`
- si lo que intentaron fue **attach** de una base más nueva a una más vieja, eso ya estaba mal como método

## Cómo quiero que trabajes la próxima sesión
1. leer el ContextPack
2. esperar el ZIP
3. revisar el código real
4. decir qué encontraste realmente
5. decir qué ya está, qué falta y qué quedó a medio aplicar
6. proponer cambios mínimos
7. dar instrucciones en formato claro, idealmente:
   - **DONDE DICE → REEMPLAZAR POR**

## Prioridad al retomar
No decidas vos el tema nuevo sin revisar primero lo que yo te indique al arrancar.  
Las dos líneas hoy abiertas para el futuro son:

### Línea 1
seguir con nuevos comprobantes reales de factura cuando yo los consiga

### Línea 2
retomar análisis de compatibilidad/migración SQL 2016 / 2005 cuando yo te pase errores o scripts adicionales

## Importante
Si hay duda entre “lo que estaba en el chat” y “lo que está en el ZIP”, manda siempre el ZIP real.