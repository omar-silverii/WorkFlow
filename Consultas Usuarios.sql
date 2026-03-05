SELECT UserKey, PermisoKey
FROM dbo.WF_UserPermiso
WHERE Activo = 1
ORDER BY UserKey, PermisoKey;

DECLARE @U nvarchar(200) = 'OMARD\USUARIO4';
DECLARE @P nvarchar(100) = 'DOC_ABM';

IF EXISTS (
    SELECT 1
    FROM dbo.WF_UserPermiso up
    JOIN dbo.WF_Permiso p ON p.PermisoKey = up.PermisoKey AND p.Activo = 1
    WHERE up.Activo = 1
      AND up.UserKey = @U
      AND up.PermisoKey = @P
)
    SELECT 1 AS Tiene;
ELSE
    SELECT 0 AS Tiene;


	2) Qué tablas valida RBAC (respuesta directa)

En tu modelo RBAC actual, la validación de permisos debe salir de:

WF_UserPermiso (override por usuario)

Columnas clave: UserKey, PermisoKey, Activo

WF_UsuarioRol (roles del usuario)

Columnas clave: Usuario, RolKey, Activo

WF_RolPermiso (permisos asociados al rol)

Columnas clave: RolKey, PermisoKey, Activo

WF_Permiso (catálogo)

Columnas clave: PermisoKey, Activo


5) Qué permiso debería exigir cada página (propuesta coherente)

Usando tus keys actuales (sin inventar nuevas):

Default.aspx → DASH (o sin permiso si querés un “home” mínimo, pero vos querés control real: poné DASH)

WF_DocTipo.aspx, WF_DocTipoReglas.aspx → DOC_ABM

WF_Tareas.aspx → TAREAS_MIS

WF_Gerente_Tareas.aspx → TAREAS_GERENCIA

WF_Instancias.aspx → INSTANCIAS

WF_Seguridad.aspx → SEGURIDAD_ABM

WorkflowUI.aspx, WF_Definiciones.aspx → WF_ADMIN

WF_Entidades.aspx → ENTIDADES_ABM