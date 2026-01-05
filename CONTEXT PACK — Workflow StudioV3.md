📦 CONTEXT PACK — Workflow Studio

Documento interno para continuidad de sesiones (ChatGPT)
Autor del proyecto: Omar Silverii
Stack principal: ASP.NET WebForms (.NET 4.8) + SQL Server + JS puro

1. 🎯 Objetivo del proyecto

Workflow Studio es un motor de workflows visual y ejecutable orientado a intranets empresariales, con estas características clave:

Editor visual drag & drop (canvas + toolbox)

Persistencia del workflow en JSON

Motor de ejecución server-side (no low-code falso)

Separación estricta:

UI (inspectors JS)

Motor

Handlers

Pensado para:

Procesamiento documental

Automatización de flujos administrativos

Integraciones (HTTP, SQL, archivos, colas, email)

Inspiración conceptual: n8n / Camunda, pero sin frameworks externos

2. 🧠 Principios de diseño (MUY IMPORTANTES)

Estos puntos NO SE DISCUTEN, son reglas del proyecto:

❌ NO hay nodos de transformación

Nada de “convertir”, “parsear”, “formatear” en nodos

Todo eso vive en el handler correspondiente

🇦🇷 Cultura Argentina

Montos tipo "154.000,00"

Fechas "dd/MM/yyyy"

Comparaciones numéricas se resuelven en código (HIf)

🧩 Un nodo = una responsabilidad

doc.extract → extraer

control.if → decidir

file.write → escribir

Nada de mezclar lógica

🧼 Motor limpio

MotorFlujoMinimo.cs NO debe contener lógica de negocio

Solo:

Orquestación

Routing

Estado

Todo lo demás → App_Code/Handlers/H*.cs

🧠 El JSON del workflow es declarativo

Nunca se “arregla” el JSON

Si algo molesta → se arregla el Inspector o el Handler

3. 🗂️ Arquitectura general
3.1 Capas
┌───────────────────────────────┐
│ UI (WorkflowUI.aspx)          │
│ - Canvas                      │
│ - Inspectors JS               │
└───────────────▲───────────────┘
                │ JSON
┌───────────────┴───────────────┐
│ MotorFlujoMinimo.cs           │
│ - Ejecuta nodos               │
│ - Maneja edges                │
│ - ContextoEjecucion           │
└───────────────▲───────────────┘
                │ ctx + params
┌───────────────┴───────────────┐
│ Handlers (App_Code/Handlers)  │
│ - HIf                         │
│ - HFileWrite                  │
│ - HDocExtract                 │
│ - etc                         │
└───────────────────────────────┘

4. ⚙️ MotorFlujoMinimo.cs (ROL REAL)
El motor NO HACE NEGOCIO

Solo:

Valida workflow

Ejecuta nodos secuencialmente

Resuelve edges según:

Etiqueta (true, false, always, etc.)

Mantiene ContextoEjecucion

ContextoEjecucion

Estado : Dictionary<string, object>

ExpandString("${input.codigo}")

ResolverPath("input.monto_estimado")

SetPath("payload.id", valor)

📌 Estado es el contrato universal entre nodos

5. 🧱 Nodos importantes (estado actual)
5.1 doc.load

Carga archivo (txt, pdf, etc.)

Extrae texto

Expone:

input.text

input.rawText

5.2 doc.extract

Modos soportados:

Regex inline (nuevo)

Legacy rules desde SQL (docTipo)

Reglas SQL:

Se cargan con useDbRules = true

docTipoId define qué reglas aplicar

Resultado:

input.codigo

input.monto_estimado

etc.

✔ Keys normalizadas para usar en ${input.xxx}

5.3 control.if (CRÍTICO)

Evalúa expresiones tipo:

${input.monto_estimado} > 1000

Implementación clave en HIf.cs

Parsea números con:

es-AR

fallback invariant

Convierte:

"154.000,00" → 154000.00

No compara strings cuando hay números

👉 NO se usan nodos transformadores

5.4 file.write (REFACTORIZADO)
Nuevo comportamiento estándar

Si viene content

Se expande con ${...}

Se escribe directo

Si NO viene content

Usa origen (legacy)

Esto permite:

Empresa: ${input.empresa}
Monto: ${input.monto_estimado}
Fecha: ${input.fecha}


📌 content es ahora el camino principal

6. 🧩 Inspectors JS (ROL CLAVE)

Los inspectors:

Definen la UX

Definen qué params existen

Evitan tocar JSON a mano

Ejemplos ya implementados correctamente:

doc.extract

textarea para rulesJson

validación JSON

file.write

textarea para content

control.if

input limpio para expresión

📌 Si algo no se ve → el problema es el inspector

7. 🧪 Estado actual del proyecto (FUNCIONA)

✔ Extracción documental
✔ Comparaciones argentinas
✔ Escritura de archivos con templates
✔ Branching correcto
✔ Logs claros
✔ Sin nodos basura

Ejemplo real funcionando:

Monto: 154.000,00
If > 1000 → True
file.write → OK

8. 🚀 Próximos pasos (cuando se retome)

Limpieza final de MotorFlujoMinimo.cs

Quitar handlers embebidos

Mover TODO a Handlers/

Persistencia formal:

WF_Definicion

WF_Instancia

WF_InstanciaLog

Runtime async

Cola real (WF_Queue)

Human Tasks (UI)

9. 🧠 Cómo usar este documento

👉 Cuando abras una sesión nueva:

Pegás TODO este documento

Decís:

“Este es el Context Pack de Workflow Studio”

A partir de ahí:

No se reexplica nada

Se trabaja directo