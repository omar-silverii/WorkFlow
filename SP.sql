CREATE OR ALTER PROCEDURE dbo.WF_Instancia_ActualizarBandejas
    @InstanciaId bigint
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DefId int;
    DECLARE @ProcesoKey nvarchar(50);
    DECLARE @Empresa nvarchar(200);
    DECLARE @DatosContexto nvarchar(max);

    SELECT
        @DefId = i.WF_DefinicionId,
        @DatosContexto = i.DatosContexto
    FROM dbo.WF_Instancia i
    WHERE i.Id = @InstanciaId;

    -- ProcesoKey = WF_Definicion.Codigo (genérico, sin hardcode)
    SELECT
        @ProcesoKey = d.Codigo
    FROM dbo.WF_Definicion d
    WHERE d.Id = @DefId;

    -- Empresa desde JSON
    SET @Empresa = JSON_VALUE(@DatosContexto, '$.estado.input.empresa');

    -- Resolver ScopeKey por reglas
    DECLARE @ScopeKey nvarchar(50) = NULL;

    SELECT TOP 1
        @ScopeKey = r.ScopeKey
    FROM dbo.WF_ScopeRegla r
    WHERE (r.WF_DefinicionId IS NULL OR r.WF_DefinicionId = @DefId)
      AND (
            (r.MatchTipo = 'equals'   AND JSON_VALUE(@DatosContexto, r.CampoJson) = r.MatchValor)
         OR (r.MatchTipo = 'contains' AND JSON_VALUE(@DatosContexto, r.CampoJson) LIKE '%' + r.MatchValor + '%')
      )
    ORDER BY r.Id DESC;

    IF (@ScopeKey IS NULL) SET @ScopeKey = 'GLOBAL';

    UPDATE dbo.WF_Instancia
    SET ProcesoKey = @ProcesoKey,
        EmpresaKey = @Empresa,
        ScopeKey   = @ScopeKey
    WHERE Id = @InstanciaId;
END
GO
/*
EXEC dbo.WF_Instancia_ActualizarBandejas @InstanciaId = 10190;
SELECT Id, ProcesoKey, EmpresaKey, ScopeKey FROM dbo.WF_Instancia WHERE Id = 10190;


1) “Pendientes de mi scope”
SELECT TOP 200
  i.Id, i.ProcesoKey, i.EmpresaKey, i.ScopeKey, i.Estado, i.FechaInicio, i.CreadoPor
FROM dbo.WF_Instancia i
WHERE i.Estado = 'EnCurso'
  AND i.ScopeKey = @MiScopeKey
ORDER BY i.FechaInicio DESC;

INSERT INTO dbo.WF_User(UserKey, DisplayName) VALUES ('omard\omard', 'Omar');
INSERT INTO dbo.WF_UserPermiso(UserKey, Permiso, VerTodo)
VALUES ('omard\omard', 'GERENTE_DASH', 1);

INSERT INTO dbo.WF_UserPermiso(UserKey, Permiso, ScopeKey, VerTodo)
VALUES ('usuario\finanzas1', 'GERENTE_DASH', 'FINANZAS', 0);

INSERT INTO dbo.WF_UserPermiso(UserKey, Permiso, VerTodo)
VALUES ('omard\omard', 'ADMIN', 1);

select * from WF_User
select * from WF_UserPermiso
*/


CREATE OR ALTER PROCEDURE dbo.WF_Gerente_EnCurso_MiAlcance
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
	-- Bandeja “En curso (Mi alcance)”
    -- Si tiene VerTodo=1, devolvemos todo (sin filtrar scope)
    IF EXISTS (
        SELECT 1
        FROM dbo.WF_UserPermiso p
        WHERE p.UserKey=@UserKey
          AND p.Permiso='GERENTE_DASH'
          AND p.Activo=1
          AND p.VerTodo=1
    )
    BEGIN
        SELECT TOP 200
            i.Id, i.ProcesoKey, i.EmpresaKey, i.ScopeKey, i.Estado, i.FechaInicio, i.CreadoPor
        FROM dbo.WF_Instancia i
        WHERE i.Estado='EnCurso'
        ORDER BY i.FechaInicio DESC;
        RETURN;
    END

    -- Si NO es VerTodo, filtra por scopes permitidos (y opcionalmente proceso)
    SELECT TOP 200
        i.Id, i.ProcesoKey, i.EmpresaKey, i.ScopeKey, i.Estado, i.FechaInicio, i.CreadoPor
    FROM dbo.WF_Instancia i
    WHERE i.Estado='EnCurso'
      AND EXISTS (
          SELECT 1
          FROM dbo.WF_UserPermiso p
          WHERE p.UserKey=@UserKey
            AND p.Permiso='GERENTE_DASH'
            AND p.Activo=1
            AND (p.ScopeKey IS NULL OR p.ScopeKey = i.ScopeKey)
            AND (p.ProcesoKey IS NULL OR p.ProcesoKey = i.ProcesoKey)
      )
    ORDER BY i.FechaInicio DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.WF_Gerente_Historial
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;
	--Bandeja “Historial”
    IF EXISTS (
        SELECT 1
        FROM dbo.WF_UserPermiso p
        WHERE p.UserKey=@UserKey
          AND p.Permiso='GERENTE_DASH'
          AND p.Activo=1
          AND p.VerTodo=1
    )
    BEGIN
        SELECT TOP 200
            i.Id, i.ProcesoKey, i.EmpresaKey, i.ScopeKey, i.Estado, i.FechaInicio, i.FechaFin, i.CreadoPor
        FROM dbo.WF_Instancia i
        WHERE i.Estado IN ('Finalizado','Error')
        ORDER BY ISNULL(i.FechaFin, i.FechaInicio) DESC;
        RETURN;
    END

    SELECT TOP 200
        i.Id, i.ProcesoKey, i.EmpresaKey, i.ScopeKey, i.Estado, i.FechaInicio, i.FechaFin, i.CreadoPor
    FROM dbo.WF_Instancia i
    WHERE i.Estado IN ('Finalizado','Error')
      AND EXISTS (
          SELECT 1
          FROM dbo.WF_UserPermiso p
          WHERE p.UserKey=@UserKey
            AND p.Permiso='GERENTE_DASH'
            AND p.Activo=1
            AND (p.ScopeKey IS NULL OR p.ScopeKey = i.ScopeKey)
            AND (p.ProcesoKey IS NULL OR p.ProcesoKey = i.ProcesoKey)
      )
    ORDER BY ISNULL(i.FechaFin, i.FechaInicio) DESC;
END
GO

/*
INSERT INTO dbo.WF_EscalamientoRegla
    (ProcesoKey, RolOrigen, RolDestino, MinutosVencida)
VALUES
    ('ORDEN_COMPRA', 'JEFE_AREA', 'GERENCIA', 0),
    ('ORDEN_COMPRA', 'GERENCIA',  'DIRECTOR', 120);
*/
CREATE TABLE dbo.WF_EscalamientoRegla
(
    Id              bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProcesoKey      nvarchar(50) NOT NULL,     -- usa WF_Instancia.ProcesoKey
    RolOrigen       nvarchar(200) NOT NULL,
    RolDestino      nvarchar(200) NOT NULL,
    MinutosVencida  int NOT NULL DEFAULT(0),   -- 0 = apenas vence
    Activo          bit NOT NULL DEFAULT(1),
    CreatedAt       datetime NOT NULL DEFAULT(GETDATE())
);

CREATE INDEX IX_WF_EscalamientoRegla_Main
ON dbo.WF_EscalamientoRegla (ProcesoKey, RolOrigen, Activo);

