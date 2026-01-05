CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Tomar
    @TareaId   int,
    @UserKey   nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserKeyFull  nvarchar(200) = LTRIM(RTRIM(ISNULL(@UserKey, '')));
    DECLARE @UserKeyShort nvarchar(200) =
        CASE
            WHEN CHARINDEX('\', @UserKeyFull) > 0
                THEN RIGHT(@UserKeyFull, LEN(@UserKeyFull) - CHARINDEX('\', @UserKeyFull))
            ELSE @UserKeyFull
        END;

    UPDATE dbo.WF_Tarea
    SET
        AsignadoA        = @UserKeyFull,
        UsuarioAsignado = @UserKeyShort
    WHERE Id = @TareaId
      AND Estado = 'Pendiente'
      AND (AsignadoA IS NULL OR AsignadoA = '');

    IF @@ROWCOUNT = 0
    BEGIN
        RAISERROR('La tarea ya fue tomada o no está disponible.', 16, 1);
        RETURN;
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.WF_Gerente_Tareas_Cerradas_Mis
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserKeyFull nvarchar(200) = LTRIM(RTRIM(ISNULL(@UserKey, '')));
    DECLARE @UserKeyShort nvarchar(200) =
        CASE
            WHEN CHARINDEX('\', @UserKeyFull) > 0 THEN RIGHT(@UserKeyFull, LEN(@UserKeyFull) - CHARINDEX('\', @UserKeyFull))
            ELSE @UserKeyFull
        END;

    SELECT TOP 200
        t.Id,
        t.WF_InstanciaId,
        t.Titulo,
        t.Descripcion,
        t.RolDestino,
        t.AsignadoA,
        t.UsuarioAsignado,
        t.ScopeKey,
        t.Estado       AS TareaEstado,
        t.Resultado    AS TareaResultado,
        t.FechaCreacion,
        t.FechaVencimiento,
        t.FechaCierre  AS FechaCierre
    FROM dbo.WF_Tarea t
    WHERE t.Estado = 'Completada'
      AND (
            t.AsignadoA IN (@UserKeyFull, @UserKeyShort)
         OR t.UsuarioAsignado IN (@UserKeyFull, @UserKeyShort)
      )
    ORDER BY t.FechaCierre DESC, t.FechaCreacion DESC;
END
GO




/*
dbo.WF_Gerente_Tareas_MisTareas 'OMARD\omard'

UPDATE WF_Tarea
SET AsignadoA = 'OMARD\omard'
WHERE Id = 3

DECLARE @U nvarchar(200) = N'OMARD\omard';

SELECT ur.Usuario, ur.Rol
FROM dbo.WF_UsuarioRol ur
WHERE ur.Usuario IN (
    @U,
    CASE WHEN CHARINDEX('\', @U) > 0 THEN RIGHT(@U, LEN(@U) - CHARINDEX('\', @U)) ELSE @U END
)
ORDER BY ur.Rol;

select * from WF_UsuarioRol
SELECT * FROM dbo.WF_Tarea t WHERE t.Estado = 'Pendiente'


SELECT TOP 50
    t.RolDestino,
    COUNT(*) AS Cantidad
FROM dbo.WF_Tarea t
WHERE t.Estado = 'Pendiente'
  AND (t.AsignadoA IS NULL OR LTRIM(RTRIM(t.AsignadoA)) = '')
GROUP BY t.RolDestino
ORDER BY COUNT(*) DESC;

sp_help WF_Tarea

*/



/*
dbo.WF_Gerente_Tareas_MisTareas 'OMARD\omard'
*/
CREATE OR ALTER PROCEDURE dbo.WF_Gerente_Tareas_MisTareas
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserKeyFull nvarchar(200) = LTRIM(RTRIM(ISNULL(@UserKey, '')));
    DECLARE @UserKeyShort nvarchar(200) =
        CASE
            WHEN CHARINDEX('\', @UserKeyFull) > 0 THEN RIGHT(@UserKeyFull, LEN(@UserKeyFull) - CHARINDEX('\', @UserKeyFull))
            ELSE @UserKeyFull
        END;

    SELECT TOP 200
        t.Id,
        t.WF_InstanciaId,
        t.Titulo,
        t.Descripcion,
        t.RolDestino,
        t.AsignadoA,
        t.UsuarioAsignado,
        t.ScopeKey,
        t.Estado AS TareaEstado,
        t.FechaCreacion,
        t.FechaVencimiento,
		t.FechaCierre AS FechaCierre,
		t.Datos
    FROM dbo.WF_Tarea t
    WHERE t.Estado = 'Pendiente'
      AND (
            t.AsignadoA IN (@UserKeyFull, @UserKeyShort)
         OR t.UsuarioAsignado IN (@UserKeyFull, @UserKeyShort)
      )
    ORDER BY t.FechaVencimiento ASC, t.FechaCreacion DESC;
END
GO

/*
dbo.WF_Gerente_Tareas_PorMiRol 'OMARD\omard'
*/
CREATE OR ALTER PROCEDURE dbo.WF_Gerente_Tareas_PorMiRol
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserKeyFull nvarchar(200) = LTRIM(RTRIM(ISNULL(@UserKey, '')));
    DECLARE @UserKeyShort nvarchar(200) =
        CASE
            WHEN CHARINDEX('\', @UserKeyFull) > 0 THEN RIGHT(@UserKeyFull, LEN(@UserKeyFull) - CHARINDEX('\', @UserKeyFull))
            ELSE @UserKeyFull
        END;

    -- Si el usuario no tiene scopes cargados, lo dejamos ver todo (modo simple)
    DECLARE @TieneScopes bit = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.WF_UsuarioScope s WHERE s.Usuario IN (@UserKeyFull, @UserKeyShort)
    ) THEN 1 ELSE 0 END;

    SELECT TOP 200
        t.Id,
        t.WF_InstanciaId,
        t.Titulo,
        t.Descripcion,
        t.RolDestino,
        t.AsignadoA,
        t.UsuarioAsignado,
        t.ScopeKey,
        t.Estado AS TareaEstado,
        t.FechaCreacion,
        t.FechaVencimiento,
		t.FechaCierre AS FechaCierre,
		t.Datos
    FROM dbo.WF_Tarea t
    WHERE t.Estado = 'Pendiente'
      AND (t.AsignadoA IS NULL OR t.AsignadoA = '')  -- no asignada a una persona
      AND EXISTS (
          SELECT 1
          FROM dbo.WF_UsuarioRol ur
          WHERE ur.Usuario IN (@UserKeyFull, @UserKeyShort)
            AND ur.Rol = t.RolDestino
      )
      AND (
            @TieneScopes = 0
         OR EXISTS (
              SELECT 1
              FROM dbo.WF_UsuarioScope us
              WHERE us.Usuario IN (@UserKeyFull, @UserKeyShort)
                AND us.ScopeKey = t.ScopeKey
         )
      )
    ORDER BY t.FechaVencimiento ASC, t.FechaCreacion DESC;
END
GO

/*
dbo.WF_Gerente_Tareas_Pendientes_MiAlcance 'OMARD\omard'
*/
CREATE OR ALTER PROCEDURE dbo.WF_Gerente_Tareas_Pendientes_MiAlcance
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

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
            t.Id,
            t.WF_InstanciaId,
            i.ProcesoKey,
            i.EmpresaKey,
            t.ScopeKey,
            t.Titulo,
            t.RolDestino,
            t.AsignadoA,
            t.Estado AS TareaEstado,
            t.FechaCreacion,
            t.FechaVencimiento,
			t.FechaCierre AS FechaCierre,
			t.Datos
        FROM dbo.WF_Tarea t
        JOIN dbo.WF_Instancia i ON i.Id = t.WF_InstanciaId
        WHERE t.Estado='Pendiente'
		AND (t.AsignadoA IS NULL OR LTRIM(RTRIM(t.AsignadoA)) = '')
        ORDER BY t.FechaVencimiento ASC, t.FechaCreacion DESC;
        RETURN;
    END

    SELECT TOP 200
        t.Id,
        t.WF_InstanciaId,
        i.ProcesoKey,
        i.EmpresaKey,
        t.ScopeKey,
        t.Titulo,
        t.RolDestino,
        t.AsignadoA,
        t.Estado AS TareaEstado,
        t.FechaCreacion,
        t.FechaVencimiento,
		t.Datos
    FROM dbo.WF_Tarea t
    JOIN dbo.WF_Instancia i ON i.Id = t.WF_InstanciaId
    WHERE t.Estado='Pendiente'
	AND (t.AsignadoA IS NULL OR LTRIM(RTRIM(t.AsignadoA)) = '')
      AND EXISTS (
          SELECT 1
          FROM dbo.WF_UserPermiso p
          WHERE p.UserKey=@UserKey
            AND p.Permiso='GERENTE_DASH'
            AND p.Activo=1
            AND (p.ScopeKey IS NULL OR p.ScopeKey = t.ScopeKey)
            AND (p.ProcesoKey IS NULL OR p.ProcesoKey = i.ProcesoKey)
      )
    ORDER BY t.FechaVencimiento ASC, t.FechaCreacion DESC;
END
GO
/*
SELECT * 
FROM WF_UsuarioRol
WHERE Usuario LIKE '%omard%'  

omard/omard
JEFE_AREA
GERENCIA
FINANZAS

Select * from WF_Tarea Where Estado = 'Pendiente'
UPDATE WF_Tarea
SET RolDestino = 'GERENTE'   -- o el rol real que tengas
WHERE Id = 3

Select top 1 * from WF_Queue
Select top 1 * from WF_QueueMessage

select * from WF_DocTipo
*/
WF_Tarea_Get 52

CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Get
    @TareaId bigint
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        -- WF_Tarea
        t.Id                 AS TareaId,
        t.WF_InstanciaId     AS InstanciaId,
        t.NodoId,
        t.NodoTipo,
        t.Titulo,
        t.Descripcion,
        t.RolDestino,
        t.UsuarioAsignado,
        t.AsignadoA,
        t.Estado             AS TareaEstado,
        t.Resultado			 AS TareaResultado,
        t.FechaCreacion,
        t.FechaVencimiento,
        t.FechaCierre		 AS TareaFechaCierre,
        t.Datos              AS DatosTareaJson,
        t.ScopeKey           AS TareaScopeKey,

        -- WF_Instancia
        i.Estado             AS InstanciaEstado,
        i.FechaInicio,
        i.FechaFin,
        i.CreadoPor,
        i.DatosEntrada       AS DatosEntradaJson,
        i.DatosContexto      AS DatosContextoJson,
        i.ProcesoKey,
        i.ScopeKey           AS InstanciaScopeKey,
        i.EmpresaKey,
        i.DocTipoId,

        -- WF_Definicion (Proceso)
        d.Codigo             AS DefCodigo,
        d.Nombre             AS DefNombre,
        d.Version            AS DefVersion
    FROM dbo.WF_Tarea t
    JOIN dbo.WF_Instancia i   ON i.Id = t.WF_InstanciaId
    JOIN dbo.WF_Definicion d  ON d.Id = i.WF_DefinicionId
    WHERE t.Id = @TareaId;
END
GO


CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Liberar
    @TareaId bigint,
    @UserKey nvarchar(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserKeyFull nvarchar(200) = LTRIM(RTRIM(ISNULL(@UserKey, '')));
    DECLARE @UserKeyShort nvarchar(200) =
        CASE
            WHEN CHARINDEX('\', @UserKeyFull) > 0
                THEN RIGHT(@UserKeyFull, LEN(@UserKeyFull) - CHARINDEX('\', @UserKeyFull))
            ELSE @UserKeyFull
        END;

    -- Solo el dueño puede liberar
    UPDATE dbo.WF_Tarea
    SET
        AsignadoA = NULL,
        UsuarioAsignado = NULL
    WHERE Id = @TareaId
      AND Estado = 'Pendiente'
      AND (
            AsignadoA IN (@UserKeyFull, @UserKeyShort)
         OR UsuarioAsignado IN (@UserKeyFull, @UserKeyShort)
      );

    IF @@ROWCOUNT = 0
        RAISERROR('No se pudo liberar la tarea (no sos el asignado o no está pendiente).', 16, 1);
END
GO


/*
EXEC dbo.WF_Tarea_Escalar_Pendientes;

SELECT * 
FROM dbo.WF_Queue

DECLARE @Ahora datetime = GETDATE();

SELECT TOP 50
    t.Id,
    t.Estado,
    t.AsignadoA,
    t.FechaVencimiento,
    t.Datos
FROM dbo.WF_Tarea t
WHERE t.Estado = 'Pendiente'
  AND t.FechaVencimiento IS NOT NULL
  AND t.FechaVencimiento < @Ahora
  AND (t.AsignadoA IS NULL OR LTRIM(RTRIM(t.AsignadoA)) = '')
  AND (
        t.Datos IS NULL
     OR JSON_VALUE(t.Datos, '$.escalado') IS NULL
     OR JSON_VALUE(t.Datos, '$.escalado') <> 'true'
  )
ORDER BY t.FechaVencimiento ASC;


SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WF_Queue'
ORDER BY ORDINAL_POSITION;

SELECT
    DB_NAME() AS Db,
    OBJECT_ID('dbo.WF_Queue') AS ObjId,
    OBJECTPROPERTY(OBJECT_ID('dbo.WF_Queue'),'IsTable') AS IsTable,
    OBJECTPROPERTY(OBJECT_ID('dbo.WF_Queue'),'IsView')  AS IsView;

SELECT TOP 1
    Processed, ProcessedAt, Attempts
FROM dbo.WF_Queue;

SELECT Id, FechaVencimiento, GETDATE()
FROM dbo.WF_Tarea
WHERE FechaVencimiento IS NOT NULL;

SELECT Id, AsignadoA
FROM dbo.WF_Tarea
WHERE Estado='Pendiente';

SELECT Id, Datos
FROM dbo.WF_Tarea
WHERE Datos LIKE '%escalado%';

INSERT INTO dbo.WF_Queue
    (Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, Attempts)
VALUES
(
    'wf.escalamiento',
    'TEST',
    '{"test":"ok"}',
    GETDATE(),
    NULL,
    10,
    0,
    0
);

SELECT * FROM dbo.WF_Queue WHERE Queue='wf.escalamiento';



SELECT TOP 20 * 
FROM dbo.WF_Queue 
WHERE Queue = 'wf.escalamiento'
ORDER BY Id DESC;

SELECT TOP 20 Id, Datos
FROM dbo.WF_Tarea
WHERE Datos LIKE '%"escalado"%'
ORDER BY Id DESC;
*/
CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Escalar_Pendientes
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Ahora datetime = GETDATE();

    -- 1) Tomamos el lote a escalar UNA sola vez
    ;WITH CTE AS (
        SELECT TOP 50
            t.Id,
            t.WF_InstanciaId,
            t.Titulo,
            t.RolDestino,
            t.ScopeKey,
            t.FechaVencimiento,
            t.Datos
        FROM dbo.WF_Tarea t
        WHERE t.Estado = 'Pendiente'
          AND t.FechaVencimiento IS NOT NULL
          AND t.FechaVencimiento < @Ahora
          AND (t.AsignadoA IS NULL OR LTRIM(RTRIM(t.AsignadoA)) = '')
          AND (
                t.Datos IS NULL
             OR JSON_VALUE(t.Datos, '$.escalado') IS NULL
             OR JSON_VALUE(t.Datos, '$.escalado') <> 'true'
          )
        ORDER BY t.FechaVencimiento ASC
    )
    SELECT *
    INTO #Escalar
    FROM CTE;

    IF NOT EXISTS (SELECT 1 FROM #Escalar)
        RETURN;

    -- 2) Marcar escalado en Datos (JSON)
    UPDATE t
    SET Datos =
        JSON_MODIFY(
            JSON_MODIFY(
                JSON_MODIFY(ISNULL(t.Datos, '{}'), '$.escalado', 'true'),
                '$.escaladoEn', CONVERT(varchar(33), @Ahora, 126)
            ),
            '$.escaladoMotivo', 'SLA vencido'
        )
    FROM dbo.WF_Tarea t
    INNER JOIN #Escalar c ON c.Id = t.Id;

    -- 3) Encolar evento en WF_Queue
    INSERT INTO dbo.WF_Queue
        (Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, Attempts)
    SELECT
        'wf.escalamiento' AS Queue,
        CAST(c.Id AS nvarchar(100)) AS CorrelationId,
        CONCAT(
            '{',
              '"tipo":"SLA_ESCALADO",',
              '"tareaId":', c.Id, ',',
              '"instanciaId":', c.WF_InstanciaId, ',',
              '"rolDestino":"', ISNULL(REPLACE(c.RolDestino,'"','\"'), ''), '",',
              '"scopeKey":"', ISNULL(REPLACE(c.ScopeKey,'"','\"'), ''), '",',
              '"titulo":"', ISNULL(REPLACE(REPLACE(c.Titulo,'"','\"'), CHAR(10),' '), ''), '",',
              '"fechaVencimiento":"', CONVERT(varchar(33), c.FechaVencimiento, 126), '",',
              '"escaladoEn":"', CONVERT(varchar(33), @Ahora, 126), '"',
            '}'
        ) AS Payload,
        @Ahora AS CreatedAt,
        NULL AS DueAt,
        10 AS Priority,
        0 AS Processed,
        0 AS Attempts
    FROM #Escalar c;

END
GO


/*
EXEC dbo.WF_Tarea_Escalar_Pendientes;
EXEC dbo.WF_Escalamiento_ConsumirYAplicar @Take = 50;
*/
CREATE OR ALTER PROCEDURE dbo.WF_Escalamiento_ConsumirYAplicar
    @Take int = 20
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Ahora datetime = GETDATE();

    ;WITH cte AS (
        SELECT TOP (@Take)
            q.Id,
            q.Payload
        FROM dbo.WF_Queue q WITH (ROWLOCK, READPAST, UPDLOCK)
        WHERE q.Queue = 'wf.escalamiento'
          AND q.Processed = 0
          AND (q.DueAt IS NULL OR q.DueAt <= @Ahora)
        ORDER BY q.Priority DESC, q.CreatedAt, q.Id
    )
    SELECT * INTO #msgs FROM cte;

    IF NOT EXISTS (SELECT 1 FROM #msgs)
        RETURN;

    DECLARE
        @MsgId bigint,
        @Payload nvarchar(max),
        @TareaId bigint;

    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
        SELECT Id, Payload FROM #msgs;

    OPEN cur;
    FETCH NEXT FROM cur INTO @MsgId, @Payload;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        BEGIN TRY
            SET @TareaId = TRY_CONVERT(bigint, JSON_VALUE(@Payload, '$.tareaId'));

            IF @TareaId IS NOT NULL
            BEGIN
                DECLARE
                    @ProcesoKey nvarchar(50),
                    @RolActual nvarchar(200),
                    @Vence datetime;

                SELECT
                    @ProcesoKey = i.ProcesoKey,
                    @RolActual  = t.RolDestino,
                    @Vence      = t.FechaVencimiento
                FROM dbo.WF_Tarea t
                JOIN dbo.WF_Instancia i ON i.Id = t.WF_InstanciaId
                WHERE t.Id = @TareaId
                  AND t.Estado = 'Pendiente';

                IF @ProcesoKey IS NOT NULL AND @RolActual IS NOT NULL
                BEGIN
                    DECLARE @NuevoRol nvarchar(200);

                    SELECT TOP 1
                        @NuevoRol = r.RolDestino
                    FROM dbo.WF_EscalamientoRegla r
                    WHERE r.Activo = 1
                      AND r.ProcesoKey = @ProcesoKey
                      AND r.RolOrigen = @RolActual
                      AND r.MinutosVencida <= DATEDIFF(minute, @Vence, @Ahora)
                    ORDER BY r.MinutosVencida DESC;

                    IF @NuevoRol IS NOT NULL AND @NuevoRol <> @RolActual
                    BEGIN
                        UPDATE dbo.WF_Tarea
                        SET RolDestino = @NuevoRol,
                            Datos = JSON_MODIFY(
                                        ISNULL(Datos,'{}'),
                                        '$.escaladoRol',
                                        @NuevoRol
                                    )
                        WHERE Id = @TareaId;
                    END
                END
            END

            UPDATE dbo.WF_Queue
            SET Processed   = 1,
                ProcessedAt = @Ahora,
                Attempts    = Attempts + 1
            WHERE Id = @MsgId;
        END TRY
        BEGIN CATCH
            UPDATE dbo.WF_Queue
            SET Attempts = Attempts + 1,
                LastError = ERROR_MESSAGE()
            WHERE Id = @MsgId;
        END CATCH

        FETCH NEXT FROM cur INTO @MsgId, @Payload;
    END

    CLOSE cur;
    DEALLOCATE cur;
END
GO

/*
SELECT TOP 20
    Id, WF_InstanciaId, RolDestino, Estado, AsignadoA, FechaVencimiento, Datos
FROM dbo.WF_Tarea
WHERE Estado='Pendiente'
  AND FechaVencimiento IS NOT NULL
ORDER BY FechaVencimiento ASC;

6 Recepcion
8 RRHH


-- Elegí una pendiente del pool
DECLARE @TareaId bigint = 8
(
  SELECT TOP 1 Id
  FROM dbo.WF_Tarea
  WHERE Estado='Pendiente'
    AND (AsignadoA IS NULL OR LTRIM(RTRIM(AsignadoA))='')
  ORDER BY Id DESC
);

-- La vencemos y la “des-escalamos” para que entre al SP
UPDATE dbo.WF_Tarea
SET FechaVencimiento = DATEADD(minute, -10, GETDATE()),
    Datos = JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalado', NULL)
WHERE Id = @TareaId;

SELECT @TareaId AS TareaIdForzada;


*/

CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Escalar_CrearNueva
    @TareaIdOriginal  bigint,
    @NuevoRolDestino  nvarchar(200),
    @Motivo           nvarchar(200) = NULL,
    @Usuario          nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Ahora datetime = GETDATE();

    BEGIN TRAN;

    -- 1) Tomo la tarea original con lock (evita dobles escalados)
    DECLARE
        @WF_InstanciaId bigint,
        @NodoId nvarchar(100),
        @NodoTipo nvarchar(200),
        @Titulo nvarchar(400),
        @Descripcion nvarchar(max),
        @ScopeKey nvarchar(200),
        @FechaVencimiento datetime,
        @Datos nvarchar(max),
        @RolDestinoOriginal nvarchar(200);

    SELECT
        @WF_InstanciaId = t.WF_InstanciaId,
        @NodoId = t.NodoId,
        @NodoTipo = t.NodoTipo,
        @Titulo = t.Titulo,
        @Descripcion = t.Descripcion,
        @ScopeKey = t.ScopeKey,
        @FechaVencimiento = t.FechaVencimiento,
        @Datos = t.Datos,
        @RolDestinoOriginal = t.RolDestino
    FROM dbo.WF_Tarea t WITH (UPDLOCK, ROWLOCK)
    WHERE t.Id = @TareaIdOriginal;

    IF @WF_InstanciaId IS NULL
    BEGIN
        ROLLBACK;
        RAISERROR('Tarea no encontrada: %d', 16, 1, @TareaIdOriginal);
        RETURN;
    END

    -- si ya no está pendiente, no escalamos
    IF EXISTS (SELECT 1 FROM dbo.WF_Tarea WHERE Id=@TareaIdOriginal AND Estado <> 'Pendiente')
    BEGIN
        ROLLBACK;
        RAISERROR('La tarea ya no está Pendiente.', 16, 1);
        RETURN;
    END

    -- si ya fue escalada (flag), no duplicamos
    IF JSON_VALUE(ISNULL(@Datos,'{}'), '$.escalado') = 'true'
    BEGIN
        -- igual: si querés permitir reintento idempotente, acá podríamos “no hacer nada” y devolver ok
        ROLLBACK;
        RAISERROR('La tarea ya está marcada como escalada.', 16, 1);
        RETURN;
    END

    -- 2) Cerrar original (Completada + Resultado Escalada)
    UPDATE dbo.WF_Tarea
       SET Estado = 'Completada',
           Resultado = 'Escalada',
           FechaCierre = @Ahora,
           Datos = JSON_MODIFY(
                    JSON_MODIFY(
                      JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalado', 'true'),
                      '$.escaladoEn', CONVERT(varchar(33), @Ahora, 126)
                    ),
                    '$.escaladoMotivo', ISNULL(@Motivo,'SLA vencido')
                  )
     WHERE Id = @TareaIdOriginal;

    -- 3) Crear nueva tarea (Pendiente) con rol escalado
    DECLARE @NuevaTareaId bigint;

    INSERT INTO dbo.WF_Tarea
        (WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion,
         RolDestino, UsuarioAsignado, Estado, Resultado,
         FechaCreacion, FechaVencimiento, FechaCierre, Datos, ScopeKey, AsignadoA)
    VALUES
        (@WF_InstanciaId, @NodoId, @NodoTipo, @Titulo, @Descripcion,
         @NuevoRolDestino, NULL, 'Pendiente', NULL,
         @Ahora, NULL, NULL,
         JSON_MODIFY(
            JSON_MODIFY(ISNULL(@Datos,'{}'), '$.origenEscalamiento.tareaId', CONVERT(varchar(50), @TareaIdOriginal)),
            '$.origenEscalamiento.motivo', ISNULL(@Motivo,'SLA vencido')
         ),
         @ScopeKey, NULL);

    SET @NuevaTareaId = SCOPE_IDENTITY();

    COMMIT;

    SELECT
        @TareaIdOriginal AS TareaOriginalId,
        @NuevaTareaId AS TareaNuevaId,
        @RolDestinoOriginal AS RolOriginal,
        @NuevoRolDestino AS RolNuevo;
END
GO


/*
select * from WF_Setting
*/

CREATE TABLE dbo.WF_Setting
(
    Id            int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SettingKey    nvarchar(200) NOT NULL,   -- ej: 'wf.escalamiento.roleMap'
    ScopeKey      nvarchar(100) NULL,       -- opcional (futuro): por alcance
    Value         nvarchar(max) NOT NULL,   -- JSON / texto
    Activo        bit NOT NULL CONSTRAINT DF_WF_Setting_Activo DEFAULT(1),
    UpdatedAt     datetime NOT NULL CONSTRAINT DF_WF_Setting_UpdatedAt DEFAULT(getdate())
);

CREATE UNIQUE INDEX UX_WF_Setting_Key_Scope
ON dbo.WF_Setting(SettingKey, ScopeKey)
WHERE Activo = 1;
GO

CREATE OR ALTER PROCEDURE dbo.WF_Setting_Get
    @SettingKey nvarchar(200),
    @ScopeKey   nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- 1) Si hay valor específico por ScopeKey, gana
    SELECT TOP 1
        Value
    FROM dbo.WF_Setting
    WHERE Activo = 1
      AND SettingKey = @SettingKey
      AND ((@ScopeKey IS NOT NULL AND ScopeKey = @ScopeKey))
    ORDER BY UpdatedAt DESC;

    IF @@ROWCOUNT > 0 RETURN;

    -- 2) Fallback global (ScopeKey NULL)
    SELECT TOP 1
        Value
    FROM dbo.WF_Setting
    WHERE Activo = 1
      AND SettingKey = @SettingKey
      AND ScopeKey IS NULL
    ORDER BY UpdatedAt DESC;
END
GO

-- Regla global (ScopeKey NULL)
IF NOT EXISTS (SELECT 1 FROM dbo.WF_Setting WHERE SettingKey='wf.escalamiento.roleMap' AND ScopeKey IS NULL)
BEGIN
    INSERT INTO dbo.WF_Setting (SettingKey, ScopeKey, Value, Activo)
    VALUES
    (
      'wf.escalamiento.roleMap',
      NULL,
      '{
        "OPERADOR":   "SUPERVISOR",
        "SUPERVISOR": "GERENTE",
        "RECEPCION":  "SUPERVISOR"
      }',
      1
    );
END
GO
