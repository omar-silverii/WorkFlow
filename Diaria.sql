select * from WF_Instancia where id = 	110417
select * from WF_Instancia where id = 	110417
select * from WF_InstanciaLog where WF_Instanciaid = 	110417
select * from WF_Tarea  where WF_Instanciaid = 	110417

/*
delete WF_Instancia where id = 110415
delete WF_Instancia where id = 110416
delete WF_InstanciaLog where WF_Instanciaid = 110416
delete WF_Tarea  where WF_Instanciaid = 	110416
*/

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
WF_Gerente_Tareas_MisTareas 'OMARD\USUARIO3'
select * from WF_UserPermiso
select * from WF_UsuarioRol
110416

select dbo.WF_UserHasPermiso('OMARD\USUARIO3', 'TAREAS_GERENCIA', NULL, NULL)


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