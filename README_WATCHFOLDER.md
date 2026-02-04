# Dispatcher (A) - Watch Folder

## Objetivo
Ejecutar **una instancia por cada archivo** que aparezca en una carpeta de entrada (Watch Folder).

- Entrada: carpeta `Input`
- Salida OK: carpeta `Processed`
- Salida error: carpeta `Error`

El dispatcher **no interpreta** el documento: solo detecta archivos, arma un `input` y dispara el workflow por `WF_Definicion.Key`.

---

## 1) Qué se agregó al repo
Carpeta: `Dispatcher.WatchFolder/`

Proyecto nuevo (.NET Framework 4.8):
- `Dispatcher.WatchFolder.csproj`
- `Program.cs`
- `WatchFolderOptions.cs`
- `WatchFolderDispatcher.cs`
- `App.config`
- `packages.config`

Además:
- `Samples/WatchFolder/Inbox/OC/` con 5 archivos de ejemplo.

---

## 2) Configuración (App.config)
Abrí `Dispatcher.WatchFolder/App.config` y completá:

### connectionStrings
Copiá el mismo `DefaultConnection` del `Web.config` del sitio:
- `name="DefaultConnection"`

### appSettings
- `WatchFolder.Input` : carpeta a monitorear
- `WatchFolder.Processed` : mover acá si terminó OK
- `WatchFolder.Error` : mover acá si falló
- `Workflow.Key` : **WF_Definicion.Key** a ejecutar (por defecto `DEMO.ORDEN_COMPRA.E2E`)
- `Workflow.InputField` : nombre del campo a enviar en `input` (por defecto `filePath`)
- `WatchFolder.Pattern` : filtro (ej: `*.txt`)
- `WatchFolder.PollSeconds` : intervalo de polling
- `WatchFolder.StableChecks` + `StableDelayMs` : chequeo simple para evitar leer archivos “a medio copiar”
- `WatchFolder.MoveAfter` : si `true`, mueve el archivo al final

---

## 3) Cambio mínimo recomendado en el workflow (DEMO.ORDEN_COMPRA.E2E)
Para que el workflow lea **el archivo detectado**, el nodo `file.read` no debe tener un path fijo.

En el nodo `file.read` cambiá:

- Antes:
  - `path = "C:\\temp\\OC.txt"`

- Después:
  - `path = "${input.filePath}"`

> El dispatcher siempre envía `input.filePath` (y también `input.fileName`).

---

## 4) Cómo correr la demo (paso a paso)
1. Compilar solución (restaurar NuGet).
2. Crear estas carpetas (o usar las que pongas en App.config):
   - `C:\\WorkflowStudio\\Inbox\\OC`
   - `C:\\WorkflowStudio\\Processed\\OC`
   - `C:\\WorkflowStudio\\Error\\OC`
3. Copiar 1 o más archivos `.txt` a la carpeta **Inbox** (podés usar los de `Samples/WatchFolder/Inbox/OC`).
4. Ejecutar el proyecto: `Dispatcher.WatchFolder` (Console).
5. Por cada archivo detectado:
   - Se crea una instancia del workflow configurado,
   - Se ejecuta,
   - Se mueve el archivo a `Processed` o `Error`.

---

## 5) Qué ver en la web
En `WF_Instancias.aspx` vas a ver las instancias creadas por el dispatcher.
El usuario “dueño” de la instancia queda como `watchfolder`.

---

## Notas
- Esto es un **worker mínimo** (polling). Profesionalmente, luego se puede evolucionar a:
  - `FileSystemWatcher` + cola interna,
  - Servicio Windows,
  - Control de duplicados/hashes,
  - Reintentos por archivo y DLQ (error folder con metadatos).
