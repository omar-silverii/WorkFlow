Hola. Vamos a continuar el proyecto “Workflow Studio” (ASP.NET WebForms .NET 4.8 + SQL Server) de Omar Silverii.

REGLAS:
- No inventes nada. No rehagas arquitectura. Cambios mínimos y profesionales.
- Trabajá SIEMPRE sobre el código real del ZIP o el código pegado.
- Hay UNA sola base de datos y UNA sola conexión: DefaultConnection.
- Si algo no existe (ej: visor /Visor.aspx placeholder) NO digas que funciona. Es intencional para demo/integración DMS.

CONTEXTO (leer primero):
1) Leé completo el archivo `WorkflowStudio_ContextPack.md` que te adjunto.
2) Después abrí y analizá el ZIP actualizado del proyecto (si está adjunto). Si no hay ZIP, pedime que lo suba.

OBJETIVO DE HOY:
- “Redondear demo”: que TODO lo que se muestra funcione, con coherencia visual (topbar unificada) y sin links falsos.
- Mantener coherencia UI: Default y WF_Gerente_Tareas son el estándar.

TAREAS EN CURSO / BUGS REALES:
- Default.aspx: KPIs/Actividad documental en 0 aunque hubo ejecuciones. BindActividadDocumental48h() no se ejecuta (breakpoint no entra). Revisar Page_Load, designer y controles runat=server.
- WF_Instancias.aspx: deep-link `?inst=90364` debe abrir EXACTAMENTE esa instancia.
- WF_Instancias.aspx: “Documentos del Caso” no aparece. Validar inserts a WF_InstanciaDocumento y flujo BindDocsFromDb.
- Topbar: WsTopbar.ascx debe resolver rutas correctamente (sin terminar en /Controls/Default.aspx). Evitar `<% ... %>` dentro de server tags. Usar ResolveUrl / NavigateUrl adecuados.
- Inspectors: doc.search y doc.attach deben tener los botones estándar (Guardar/Eliminar/Plantillas según corresponda) igual que util.docTipo.resolve, sin inventar estilos.

FORMA DE ENTREGA:
- Cada vez que modifiques algo: entregá el archivo completo para copy/paste (aspx/ascx/cs/js) y decime exactamente dónde va.
- Si falta info del ZIP, pedila explícitamente (por ej: “pasame el archivo Scripts/inspectors/… para copiar el estilo exacto”).

Empecemos por: revisar Default.aspx y Default.aspx.cs/designer para que BindActividadDocumental48h() se ejecute y los KPIs reflejen datos reales.
