select * from WF_Instancia where id = 	110422
select * from WF_Instancia where id = 	110424
select * from WF_InstanciaLog where WF_Instanciaid = 	110424
select * from WF_Tarea  where WF_Instanciaid = 	110424

select * from WF_UsuarioRol
select * from WF_UserPermiso where UserKey = 'OMARD\USUARIO1' And Activo = 1
select * from WF_UserPermiso where UserKey = 'OMARD\OMARD' And Activo = 1
/*
delete WF_Instancia where id = 110422
delete WF_Instancia where id = 110423
delete WF_InstanciaLog where WF_Instanciaid = 110423
delete WF_Tarea  where WF_Instanciaid = 	110423
*/

SELECT *
FROM WF_Adjunto
WHERE WF_InstanciaId = 110423
ORDER BY Id DESC

INSERT INTO WF_UserPermiso (UserKey, PermisoKey, Activo)
SELECT u.UserKey, 'Dashboard', 1
FROM WF_User u
WHERE NOT EXISTS (
    SELECT 1 
    FROM WF_UserPermiso p
    WHERE p.UserKey = u.UserKey
    AND p.PermisoKey = 'Dashboard'
);



SELECT TOP (500)
    Id,
    WF_DefinicionId,
    Estado,
    FechaInicio,
    FechaFin,
    DatosContexto
FROM WF_Instancia
WHERE WF_DefinicionId = 7125
ORDER BY Id DESC;

select * from WF_Tarea
WF_Gerente_Tareas_MisTareas 'OMARD\OMARD'
WF_Gerente_Tareas_MisTareas 'OMARD\USUARIO1'
select * from WF_UserPermiso
select * from WF_UsuarioRol
110416

select dbo.WF_UserHasPermiso('OMARD\USUARIO1', 'TAREAS_GERENCIA', NULL, NULL)

SELECT TOP 1 *
FROM WF_Instancia
ORDER BY Id DESC


INSERT INTO WF_Tarea
(
    WF_InstanciaId,
	NodoId,
	NodoTipo,
    NodoId,
    Titulo,
    Estado,
    UsuarioAsignado,
    FechaCreacion
)
VALUES
(
    -- poné el Id de instancia
    110421,
	'nTarea1',
	'human.task',
    'TEST',
    'Tarea de prueba',
    'Pendiente',
    'OMARD\USUARIO2',
    GETDATE()
)
110421
Instancia creada y ejecutada.
WF_DefinicionId = 22
WF_InstanciaId  = 110421

Revisá WF_Tarea / WF_Instancia para ver el estado.

Instancia 110422




select * from WF_UserPermiso where UserKey = 'OMARD\USUARIO2'And Activo = 1
select * from WF_UsuarioRol

DECLARE @u nvarchar(200) = N'OMARD\USUARIO3';

SELECT
    dbo.WF_UserHasPermiso(@u, 'TAREAS_GERENCIA', NULL, NULL) AS TieneGerencia,
    dbo.WF_UserHasPermiso(@u, 'VER_GERENTE', NULL, NULL)     AS TieneVerGerente; -- para detectar inconsistencias

SELECT
  t.Id,
  t.RolDestino,
  t.Estado,
  t.ScopeKey AS TareaScope,
  i.ScopeKey AS InstScope,
  COALESCE(NULLIF(LTRIM(RTRIM(t.ScopeKey)),''),
           NULLIF(LTRIM(RTRIM(i.ScopeKey)),''),
           'GLOBAL') AS ScopeResuelto,
  i.ProcesoKey,
  dbo.WF_UserHasPermiso(N'OMARD\USUARIO3','TAREAS_GERENCIA', i.ProcesoKey,
    COALESCE(NULLIF(LTRIM(RTRIM(t.ScopeKey)),''),
             NULLIF(LTRIM(RTRIM(i.ScopeKey)),''),
             'GLOBAL')) AS PasaFiltro
FROM dbo.WF_Tarea t
JOIN dbo.WF_Instancia i ON i.Id = t.WF_InstanciaId
WHERE t.Id = 70127;