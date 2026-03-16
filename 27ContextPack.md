# Workflow Studio — ContextPack (cierre de sesión)
Fecha de cierre: 16/03/2026

## 1. Proyecto y reglas de trabajo

Proyecto: **Workflow Studio**  
Stack principal:
- ASP.NET WebForms (.NET Framework 4.8)
- SQL Server (`DefaultConnection`)
- motor propio de workflows
- editor visual + runtime + handlers + persistencia de instancias/logs

Reglas de trabajo acordadas con Omar:
- **no inventar nada**
- **no cambiar arquitectura sin necesidad**
- **no reescribir cosas que ya funcionan**
- **siempre trabajar sobre el ZIP/código real**
- **no mandar “archivo completo” si no se verificó el archivo real actual**
- **no asumir que el código quedó exactamente como en el chat**
- preferir formato **DONDE DICE → REEMPLAZAR POR**
- si algo no cierra, decirlo
- si falta una pieza, pedirla
- no mezclar teoría larga con una corrección puntual
- proponer la mejor opción de entrada
- no volver atrás sobre temas cerrados
- cuando algo ya funciona, no discutirlo otra vez salvo bug real del ZIP

Regla crítica:
- cuando estemos trabajando sobre una parte real del proyecto, todo debe salir del ZIP/código real o de algo explícitamente pedido.
- no introducir wrappers, helpers o estructuras “nuevas” como si ya existieran en el proyecto sin dejarlo clarísimo.

## 2. Estado funcional importante que sigue congelado

### RBAC por alcance
Este tema **NO está resuelto** y **NO debe retomarse** salvo pedido explícito de Omar.

Motivos:
- no está bien definido qué significa “Proceso” para el usuario
- no está bien definido qué significa “Sector”
- no está clara la lógica de inclusión/exclusión
- la UX funcional todavía no está cerrada

## 3. Foco trabajado en esta sesión

### Objetivo
Implementar y validar soporte para un **extractor especial de factura electrónica argentina** dentro de `doc.load`.

### Decisión arquitectónica ya tomada
No crear nodo nuevo.  
Seguir usando `doc.load` y resolver por `MotorExtraccion`.

### Modelo acordado
En `WF_DocTipo` existe la columna:
- `MotorExtraccion`

Valores:
- `REGLAS`
- `FACTURA_AR`

Comportamiento:
- si `MotorExtraccion = REGLAS` → sigue como hoy
- si `MotorExtraccion = FACTURA_AR` → `doc.load` usa extractor especial de factura argentina

## 4. Estado real encontrado al revisar el ZIP

Se verificó en el ZIP real que ya estaba bastante adelantado:
- `WF_DocTipo.aspx` ya contempla `MotorExtraccion`
- `WF_DocTipo.aspx.cs` ya guarda/lee `MotorExtraccion`
- `WF_DocTipo.aspx.designer.cs` ya tiene el control correspondiente
- `DocumentProcessing/HDocLoad.cs` ya resuelve `MotorExtraccion`
- `HDocLoad.cs` ya bifurca:
  - `FACTURA_AR` → extractor especial
  - resto → reglas actuales
- el `.csproj` ya incluía `DocumentProcessing/FacturaElectronicaArExtractor.cs`

Lo que faltaba realmente era la implementación funcional del extractor.

## 5. DocTipo usado y configurado

Se dio de alta/confirmó el DocTipo:

- `Codigo = FACTURA_ELECTRONICA_AR`
- `Nombre = Factura electrónica AFIP`
- `ContextPrefix = factura`
- `MotorExtraccion = FACTURA_AR`

La tabla `WF_DocTipo` ya tenía `MotorExtraccion` y no hizo falta tocar SQL para eso.

## 6. Archivo principal trabajado

Archivo central de esta sesión:
- `DocumentProcessing/FacturaElectronicaArExtractor.cs`

Se trabajó muchas iteraciones sobre ese archivo, hasta validar una factura PDF real.

## 7. Hallazgo clave durante el debugging

El problema real no era el runtime ni `doc.load`.
El extractor sí estaba entrando y ejecutando.

Se usó debug para inspeccionar el texto real extraído del PDF y se encontró que el layout llegaba así:

- `Punto de Venta: Comp. Nro:`
- siguiente línea: `00003 00000097`

Y el detalle así:

- encabezado partido en varias líneas:
  - `Precio Unit. % Bonif Imp. Bonif. Subtotal`
  - `Código Producto / Servicio Cantidad U. Medida`
  - `unidades`
- renglón del item:
  - `Honorarios por Programacion 1,00 232050,00 0,00 0,00 232050,00`

Además el PDF venía repetido como:
- `ORIGINAL`
- `DUPLICADO`
- `TRIPLICADO`

Esto obligó a:
- tomar solo la primera copia útil
- resolver `numero` leyendo líneas partidas
- ajustar el parser de `items[]`
- corregir regex de importes para aceptar valores como `232050,00` sin puntos de miles

## 8. Resultado final validado en esta sesión

### Factura real validada
PDF real tipo **C**

Resultado final validado en logs:

#### Cabecera
- `biz.factura.tipoComprobante = C`
- `biz.factura.letra = C`
- `biz.factura.numero = 00003-00000097`
- `biz.factura.puntoVenta = 00003`
- `biz.factura.numeroComprobante = 00000097`
- `biz.factura.fecha = 30/05/2023`
- `biz.factura.periodoDesde = 01/03/2023`
- `biz.factura.periodoHasta = 30/04/2023`
- `biz.factura.vencimientoPago = 31/05/2023`
- `biz.factura.cae = 73229835423122`
- `biz.factura.caeVencimiento = 09/06/2023`
- `biz.factura.validacionBasicaOk = True`

#### Emisor
- `biz.factura.emisor.nombre = SILVERII OMAR DARIO`
- `biz.factura.emisor.cuit = 23175875379`
- `biz.factura.emisor.condicionIva = Responsable Monotributo`
- `biz.factura.emisor.ingresosBrutos = exento`
- `biz.factura.emisor.fechaInicioActividades = 01/03/2008`
- `biz.factura.emisor.direccion = Roca 865 - Remedios De Escalada, Buenos Aires`

#### Receptor
- `biz.factura.receptor.nombre = EDI SA`
- `biz.factura.receptor.cuit = 30656893830`
- `biz.factura.receptor.condicionIva = IVA Responsable Inscripto`
- `biz.factura.receptor.direccion = Tucuman 540 Piso:4 Dpto:D - Capital Federal, Ciudad de Buenos Aires`

#### Importes
- `biz.factura.subtotal = 232050.00`
- `biz.factura.otrosTributos = 0.00`
- `biz.factura.total = 232050.00`

#### Items
- `biz.factura.itemsCount = 1`

Item 0:
- `descripcion = Honorarios por Programacion`
- `cantidad = 1.00`
- `unidadMedida = unidades`
- `precioUnitario = 232050.00`
- `bonificacionPorcentaje = 0.00`
- `bonificacionImporte = 0.00`
- `subtotal = 232050.00`

## 9. JSON mínimo de prueba usado conceptualmente

Se probó con un workflow mínimo de este estilo:
- `util.start`
- `doc.load` con:
  - `mode = pdf`
  - `docTipoCodigo = FACTURA_ELECTRONICA_AR`
- `util.logger` para cabecera
- `util.logger` para emisor/receptor/importes
- `util.logger` para item 0
- `util.end`

Punto importante:
- el logger soportó `${biz.factura.items[0].campo}` en esta prueba

## 10. Qué quedó firme y no debe tocarse salvo bug real

Para esta V1 del extractor:
- **no tocar más**:
  - parseo de `numero`
  - parseo de `CAE`
  - parseo de `fecha`
  - parseo de `items[]` para esta factura
  - parseo de subtotal/otros/total
  - parseo de emisor/receptor ya validado en este caso

Esta versión debe considerarse **base estable** para este PDF tipo C probado.

## 11. Qué NO se puede seguir probando todavía

No avanzar todavía con:
- factura A
- factura B
- múltiples ítems
- percepciones
- IVA discriminado
- otros tributos reales más complejos

Motivo:
- Omar todavía no tiene más comprobantes reales para probar.

### Dejar asentado
Cuando Omar consiga modelos reales adicionales, retomar pruebas con:
- tipo A
- tipo B
- múltiples ítems
- percepciones / otros tributos
- variantes reales de layout PDF

## 12. Segundo tema abierto en esta sesión: migración SQL Server

Tema detectado:
migrar/compatibilizar la base actual de:
- **Microsoft SQL Server Express (64-bit) 16.0.1170.5**

hacia:
- SQL Server 2016
- SQL Server 2005

### Hallazgos importantes
1. **No restaurar/attach hacia abajo**
   - una base creada en motor más nuevo no puede simplemente “attacharse” en versiones más viejas
   - si intentaron attach del `.mdf`, eso ya puede explicar el error

2. **SQL Server 2016**
   - `datetime2(0)` **sí es compatible**
   - por lo tanto, si falló en 2016, el problema probablemente no era `datetime2`
   - en el script revisado apareció un problema claro para 2016:
     - `OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF`
     - eso es de versiones más nuevas y puede romper en 2016

3. **SQL Server 2005**
   - `datetime2(0)` **no existe**
   - además el script usa cosas incompatibles con 2005:
     - `TRY_CONVERT`
     - `CONCAT`
     - `JSON_VALUE`
     - `ISJSON`
     - `JSON_QUERY`
     - `OPTIMIZE_FOR_SEQUENTIAL_KEY`

### Conclusión sobre SQL
- para 2016: hacer una versión del script compatible
- para 2005: ya no es un ajuste menor; requiere rama de compatibilidad real

### Estado de este tema
Quedó **pendiente**, porque Omar no tenía a mano el error exacto de attach/migración ni todo el material para seguir.

## 13. Qué archivo SQL se revisó

Omar pasó el script SQL que había enviado para correr en destino.  
Conclusión:
- ese script era el material correcto para revisar primero
- permitió detectar incompatibilidades reales con 2016 y 2005

## 14. Próximos pasos razonables cuando se retome

### Línea A — Facturas
Cuando haya más comprobantes reales:
1. probar tipo A
2. probar tipo B
3. probar múltiples ítems
4. probar percepciones / otros tributos
5. endurecer el extractor solo sobre casos reales

### Línea B — SQL
Cuando Omar vuelva con el tema SQL:
1. traer error exacto de attach / restore / import
2. traer scripts reales o esquema real si hace falta
3. empezar por compatibilidad **SQL 2016**
4. después evaluar una rama **SQL 2005**

## 15. Estado final al cierre
Esta sesión cerró con un resultado importante:
- extractor `FACTURA_AR` **validado correctamente** con una factura PDF real tipo C
- se logró cabecera completa, emisor, receptor, importes e items
- se dejó identificado que el próximo avance depende de conseguir más comprobantes reales
- se dejó además abierto el análisis de compatibilidad SQL 2016 / 2005 para retomarlo más adelante