# Workflow Studio — Context Pack (actualizado) — Feb 11, 2026

## 0) Objetivo inmediato (demo “todo funciona”)
- La demo debe mostrar **pantallas coherentes** (misma topbar / navegación) y que **lo que se ve funcione**.
- Integración DMS: hoy se demuestra **concepto** (doc.search / doc.attach) aunque el visor real del DMS aún no está integrado.
- Importante: NO inventar funcionalidades (ej. no asumir visor real si es placeholder).

## 1) Regla de oro (infra / BD)
- Hay **UNA sola base de datos** y **UNA sola conexión**.
- ConnectionString: **DefaultConnection** (única).
- No asumir multi-tenant, ni múltiples cadenas, ni múltiples BD.

## 2) Integración DMS (estado actual)
- La empresa es de Gestión Documental (DMS): estructura FICHERO / CARPETA / DOCUMENTOS, con índices por documento y visores por tipo.
- En la demo:
  - ViewerUrl puede estar en placeholder (ej: `/Visor.aspx`) y **puede dar 404**.
  - Esto sirve para mostrar que “hay que integrar con el visor real del DMS”.
- Pendiente acordado:
  - Cuando el proyecto reciba visto bueno, mapear ViewerUrl a visor real configurable, y ocultar/deshabilitar links placeholder si corresponde.

## 3) UX / coherencia visual (Topbar unificada)
- Se creó un UserControl para topbar: **`Controls/WsTopbar.ascx`**
- Debe estar en páginas:
  - WF_Tareas.aspx
  - WF_Tarea_Detalle.aspx
  - WF_Definiciones.aspx
  - WF_Instancias.aspx
  - WF_DocTipo.aspx
  - WF_DocTipoReglas.aspx
  - (evaluar si WorkflowUI.aspx también debe tenerlo; al menos el botón “Intranet” que vuelve a Default)
- Menú esperado (según rol/uso):
  - Inicio
  - Workflows
  - Documentos
  - Tareas
  - Administración
- Nota importante:
  - Evitar rutas relativas que terminen en `/Controls/...` (ej: `https://localhost:44350/Controls/Default.aspx`).
  - Usar URLs correctas con `ResolveUrl("~/Default.aspx")` o `NavigateUrl="~/Default.aspx"`.

### 3.1) Errores que ya pasaron (para no repetir)
- Error típico: “Las etiquetas de servidor no pueden contener construcciones `<% ... %>`”.
- Error de tipos: mezclar `HtmlAnchor` vs `asp:HyperLink`.
- Cuando se quiere marcar “active”, debe hacerse con el tipo de control correcto (si son `<a runat="server">` entonces `HtmlAnchor`; si son `<asp:HyperLink>` entonces `HyperLink`).

## 4) Default.aspx (Home) — lineamientos
- El diseño de Default es el “estándar” visual: Bootstrap local + tarjetas + layout limpio.
- Debe incluir:
  - Accesos rápidos
  - Guía rápida
  - Cards principales con **coherencia de botones** (1 botón por card según conversación)
- Se discutió reorganización:
  - “Crear / Editar Workflow”: 1 botón “Abrir editor” → WorkflowUI.aspx
  - “Definiciones de Workflow”: 1 botón “Abrir definiciones” → WF_Definiciones.aspx
  - “Ejecuciones (Instancias)”: se duda de su lugar; si está, debe abrir **listado** (no “una instancia específica”).
  - “Configuración / Catálogos”: si no hay nada real, se puede reutilizar para “Gerencia de tareas” (WF_Gerente_Tareas.aspx) o mantener “Próximamente” pero mejor evitar botones inútiles.
  - Incluir “Tareas” (WF_Tareas / WF_Gerente_Tareas) porque las tareas dependen del usuario logueado.
- Default muestra KPIs (Docs e Instancias 48h) y “Actividad documental”.
  - Problema detectado: KPIs en 0 aunque hubo ejecuciones.
  - Se puso breakpoint a `BindActividadDocumental48h()` y **nunca se detuvo**:
    - Debe ejecutarse en `Page_Load` cuando se carga Default (en cada request, al menos en el primer load).
  - Revisar que Default tenga `CodeBehind`, y que el `.designer.cs` esté bien generado (controles con `runat="server"` y build action correcta).

## 5) WF_Instancias.aspx — estado y problemas detectados
### 5.1) Deep link a instancia
- Se quiere abrir una instancia específica desde links tipo:
  - `WF_Instancias.aspx?inst=90364`
- Se implementó `IrAInstancia(instId)`:
  - Busca WF_DefinicionId y Estado de esa instancia
  - Selecciona definición en ddl
  - Ajusta “mostrar finalizados” si corresponde
  - setea txtBuscar = instId
  - recarga grilla y muestra datos de la instancia

### 5.2) Bug reportado por Omar
- Botón “Ver” en Default/Actividad documental:
  - `WF_Instancias.aspx?inst=90364` NO llevaba a esa instancia (mostraba otra).
- Se corrigió: el handler debe leer el parámetro correcto (inst / instId) y mostrar la instancia exacta.

### 5.3) Auditoría documental y “Documentos del caso”
- Se creó tabla / lógica de auditoría documental: `WF_InstanciaDocumento`
- En WF_Instancias:
  - Toggle: `chkDocAuditDedup` (deduplicado vs histórico)
  - `BindDocAudit(instId)` muestra grilla de auditoría
  - `BindDocsFromDb(instId)` arma “Documentos del caso” desde DB (en vez de depender de DatosContexto)
- Problema visto:
  - “Nunca se carga Documentos Caso”:
    - Puede ser que no haya inserts a `WF_InstanciaDocumento` desde handlers o que el panel esté oculto.
    - También hay un tema de “deduplicado” y “instancia actual” no persistida.
- Se mejoró:
  - Guardar `_instanciaActualId` en cada selección para que el toggle refresque lo correcto.

## 6) WF_Tareas.aspx (Mis Tareas) — estado
- Página ya modificada por Omar.
- Debe tener la topbar coherente con Default/WF_Gerente_Tareas.
- Grid muestra tareas (WF_Tarea) y link “Instancia”.
- Redirección esperada:
  - `WF_Instancias.aspx?WF_DefinicionId=<defId>&inst=<instId>` (o parámetro estándar acordado)
- Mantener consistencia de querystring: no mezclar `WF_InstanciaId`, `instId`, `inst`. Elegir uno y soportar alias si viene de versiones previas.

## 7) WF_DocTipoReglas.aspx — estado
- Se agregó `<ws:Topbar .../>` (WsTopbar.ascx).
- La página usa JS (fetch a `/Api/Generico.ashx`) para:
  - doctipo.list
  - doctipo.reglas.list
  - doctipo.reglas.save
  - doctipo.reglas.test
- Importante: estilos y layout actuales se mantienen, pero la topbar debe ser consistente.
- Problema detectado:
  - errores de rutas/URL cuando el control resolvía mal (terminaba en /Controls/...).

## 8) Inspectors (WorkflowUI) — estado real
### 8.1) Comportamiento estándar esperado en inspectores
- Los nodos normalmente tienen al final:
  - Botones “Insertar plantilla” / “Guardar” / “Eliminar nodo”
  - Estilo: botones “comunes”, fondo blanco, letras negras (según el look actual del editor)
- Los nodos nuevos `doc.search` y `doc.attach` NO traían esos botones al principio.

### 8.2) Fix aplicado
- Se tomó como guía `util.docTipo.resolve` (inspector que sí tiene rowButtons + btn helpers).
- Se ajustó `doc.search` (y luego `doc.attach`) para agregar acciones (Guardar/Eliminar) siguiendo el estilo real del editor.
- Ojo: NO inventar clases bootstrap si el resto del editor usa helpers `btn()` y `rowButtons()` de `WF_Inspector`.

### 8.3) Nodo doc.search
- Parámetros principales:
  - searchUrl
  - useIntranetCredentials
  - max
  - criteria (JSON)
  - viewerUrlTemplate
  - output (default: biz.doc.search)

### 8.4) Nodo doc.attach
- Parámetros principales:
  - mode: root|attachment
  - doc: string path o object inline
  - attachToCurrentTask (bool)
  - taskId override
  - output (ack)

## 9) Pendientes importantes (lista corta y concreta)
1) **ViewerUrl/Visor.aspx es placeholder**: no decir “abre” si no existe; es a integrar con visor real del DMS.
2) **Default KPIs y Actividad**: `BindActividadDocumental48h()` debe correr al cargar Default; revisar designer/controles y el Page_Load.
3) **WF_Instancias deep-link**: `?inst=...` debe llevar siempre a la instancia correcta.
4) **Documentos Caso**: validar que `WF_InstanciaDocumento` se esté poblando realmente y que `BindDocsFromDb` se llame en el flujo correcto (MostrarDatos y toggle).
5) **Consistencia topbar** en todas las páginas clave.
6) **Nodos faltantes**: todavía quedan nodos por terminar; se continuará cuando todo lo mostrado funcione.

## 10) Cómo probar “end-to-end” (demo de verificación rápida)
- 1) Ir a Default.aspx → debe cargar KPIs/Actividad (no quedarse siempre en 0 si hay data).
- 2) Ir a WorkflowUI → cargar definición demo → ejecutar una instancia.
- 3) Ir a WF_Instancias → buscar por id → ver Datos/Logs → ver Auditoría documental (si hay insert).
- 4) Ir a WF_Tareas → abrir tarea → botón Instancia debe abrir la instancia correcta.
- 5) Probar `doc.search` y `doc.attach` en el editor:
  - Inspector muestra botones estándar
  - Guardar persiste params en JSON
