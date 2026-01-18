/*
Cómo generar una prueba limpia (y válida) para correr el API
Te dejo un script “de una sola pasada” que:
Crea una tarea vencida y pendiente (sin flags)
Ejecuta WF_Tarea_Escalar_Pendientes → encola el mensaje
Te muestra cuál es el @TareaIdOriginal
Vos llamás al API con ese tareaId (NO con la nueva)
Pegalo tal cual en SSMS (en la misma DB que usa DefaultConnection)
*/


DECLARE @WF_InstanciaId bigint = 1;   -- poné una instancia válida
DECLARE @Rol nvarchar(200) = N'RRHH';
DECLARE @ScopeKey nvarchar(200) = NULL;

DECLARE @TareaIdOriginal bigint;

-- 1) Crear tarea pendiente vencida (sin "escalamientoEncolado")
INSERT INTO dbo.WF_Tarea
(
  WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion,
  RolDestino, UsuarioAsignado, Estado, Resultado,
  FechaCreacion, FechaVencimiento, FechaCierre, Datos, ScopeKey, AsignadoA
)
VALUES
(
  @WF_InstanciaId, N'TEST', N'test.task', N'PRUEBA SLA CONTROLADA', N'',
  @Rol, NULL, N'Pendiente', NULL,
  GETDATE(), DATEADD(minute, -10, GETDATE()), NULL, NULL, @ScopeKey, NULL
);

SET @TareaIdOriginal = SCOPE_IDENTITY();

SELECT @TareaIdOriginal AS TareaIdOriginalCreada;

-- 2) Ejecutar el SP que encola (debe insertar en WF_Queue con CorrelationId = tarea original)
EXEC dbo.WF_Tarea_Escalar_Pendientes;

-- 3) Verificar que quedó mensaje en wf.escalamiento (Processed=0)
SELECT TOP 5 *
FROM dbo.WF_Queue
WHERE Queue = 'wf.escalamiento'
  AND CorrelationId = CAST(@TareaIdOriginal AS nvarchar(100))
ORDER BY Id DESC;


/*
📌 Ahora TU llamada correcta al API es:

https://localhost:44350/Api/Generico.ashx?action=worker.escalamiento.run&tareaId=<TareaIdOriginalCreada>

No uses 10076 para consumir el mensaje, usá el Id original que te imprimió el script.
✅ Qué tenés que mirar después de correr el API (para “ver qué hizo”)
Pegá esto cambiando @Orig por el Id original:
{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":55,"tareaId":10079,"instanciaId":1,"rolDestino":"RRHH","nuevoRol":"GERENCIA","escaladoReal":true,"tareaNuevaId":10080,"alreadyExisted":false,"notifEnqueued":true,"queueReverted":false,"error":null}
*/

DECLARE @Orig bigint = 10083;

-- 1) Debe quedar la original Completada/Escalada
SELECT Id, Estado, Resultado, RolDestino, FechaCreacion, FechaCierre, OrigenTareaId, Datos
FROM dbo.WF_Tarea
WHERE Id = @Orig;

-- 2) Debe aparecer la nueva tarea con OrigenTareaId = @Orig
SELECT TOP 10 Id, Estado, RolDestino, Resultado, FechaCreacion, OrigenTareaId, Datos
FROM dbo.WF_Tarea
WHERE OrigenTareaId = @Orig
ORDER BY Id DESC;

-- 3) Debe haberse creado la notificación
SELECT TOP 10 *
FROM dbo.WF_Queue
WHERE Queue='wf.notificaciones'
  AND CorrelationId = CAST(@Orig AS nvarchar(100))
ORDER BY Id DESC;


--Que el mensaje de escalamiento consumido quedó procesado
--Esperado: Processed=1, ProcessedAt con hora, Attempts>=1, LastError NULL.
DECLARE @Orig bigint = 10083;

SELECT TOP 5 Id, Queue, CorrelationId, Processed, ProcessedAt, Attempts, LastError, CreatedAt
FROM dbo.WF_Queue
WHERE Queue='wf.escalamiento'
  AND CorrelationId = CAST(@Orig AS nvarchar(100))
ORDER BY Id DESC;

--Que la nueva tarea NO heredó flags de “escalado” (y sí tiene auditoría)
--Esperado: OrigenTareaId = 10079 EscaladoFlag debería ser NULL (porque la nueva no está “escalada”, solo creada por escalamiento
--OrigenJson debería ser 10079
DECLARE @Orig bigint = 10083;

SELECT 
  t2.Id,
  t2.OrigenTareaId,
  JSON_VALUE(ISNULL(t2.Datos,'{}'),'$.origenEscalamiento.tareaId') AS OrigenJson,
  JSON_VALUE(ISNULL(t2.Datos,'{}'),'$.escalado') AS EscaladoFlag
FROM dbo.WF_Tarea t2
WHERE t2.OrigenTareaId = @Orig;

------------------------------------------------------------------------------------------------------------


/* para borrar todas las pruebas

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

------------------------------------------------------------
-- 0) Identificar tareas de prueba (originales y derivadas)
------------------------------------------------------------
;WITH Base AS (
    SELECT t.Id
    FROM dbo.WF_Tarea t
    WHERE (t.NodoId = 'TEST'
        OR t.NodoTipo = 'test.task'
        OR t.Titulo LIKE 'PRUEBA SLA%')
),
AllTestTasks AS (
    -- Originales
    SELECT Id FROM Base
    UNION
    -- Derivadas por escalamiento real
    SELECT t2.Id
    FROM dbo.WF_Tarea t2
    WHERE t2.OrigenTareaId IN (SELECT Id FROM Base)
)
SELECT 'Tareas a borrar' AS Info, COUNT(*) AS Cantidad
FROM AllTestTasks;

------------------------------------------------------------
-- 1) Borrar Queue relacionada (CorrelationId = tarea original)
--    y también por payload que contenga tareaId (best effort)
------------------------------------------------------------
-- a) por CorrelationId exacto
DELETE q
FROM dbo.WF_Queue q
WHERE q.Queue IN ('wf.escalamiento','wf.notificaciones')
  AND TRY_CONVERT(bigint, q.CorrelationId) IN (SELECT Id FROM AllTestTasks);

SELECT 'WF_Queue borradas por CorrelationId' AS Info, @@ROWCOUNT AS Cantidad;

-- b) best-effort por payload (por si algún CorrelationId no coincide)
DELETE q
FROM dbo.WF_Queue q
WHERE q.Queue IN ('wf.escalamiento','wf.notificaciones')
  AND (
        q.Payload LIKE '%"tareaId":%'
        AND EXISTS (
            SELECT 1
            FROM AllTestTasks t
            WHERE q.Payload LIKE '%"tareaId":' + CAST(t.Id AS varchar(20)) + '%'
               OR q.Payload LIKE '%"tareaId":"' + CAST(t.Id AS varchar(20)) + '"%'
        )
      );

SELECT 'WF_Queue borradas por Payload' AS Info, @@ROWCOUNT AS Cantidad;

------------------------------------------------------------
-- 2) Borrar tareas derivadas primero, luego originales
------------------------------------------------------------
-- a) Derivadas (tienen OrigenTareaId apuntando a base)
DELETE t2
FROM dbo.WF_Tarea t2
WHERE t2.OrigenTareaId IN (
    SELECT Id
    FROM dbo.WF_Tarea t
    WHERE (t.NodoId = 'TEST'
        OR t.NodoTipo = 'test.task'
        OR t.Titulo LIKE 'PRUEBA SLA%')
);

SELECT 'WF_Tarea derivadas borradas' AS Info, @@ROWCOUNT AS Cantidad;

-- b) Base (las originales de prueba)
DELETE t
FROM dbo.WF_Tarea t
WHERE (t.NodoId = 'TEST'
    OR t.NodoTipo = 'test.task'
    OR t.Titulo LIKE 'PRUEBA SLA%');

SELECT 'WF_Tarea originales borradas' AS Info, @@ROWCOUNT AS Cantidad;

COMMIT;

SELECT 'OK - Limpieza completada' AS Resultado;
*/



SELECT DB_NAME() AS DbActual;

SELECT 
  p.name,
  p.modify_date
FROM sys.procedures p
WHERE p.name = 'WF_Tarea_Escalar_CrearNueva';

SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.WF_Tarea_Escalar_CrearNueva')) AS DefSP;



DECLARE @Orig bigint = 10083;

SELECT
  t2.Id,
  t2.OrigenTareaId,
  JSON_VALUE(t2.Datos,'$.origenEscalamiento.tareaId') AS OrigenJson,
  JSON_QUERY(t2.Datos,'$.origenEscalamiento') AS OrigenObj,
  t2.Datos
FROM dbo.WF_Tarea t2
WHERE t2.OrigenTareaId = @Orig;



Drop procedure WF_Tarea_Escalar_CrearNueva
GO
Create PROCEDURE [dbo].[WF_Tarea_Escalar_CrearNueva]
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

    IF EXISTS (SELECT 1 FROM dbo.WF_Tarea WHERE Id=@TareaIdOriginal AND Estado <> 'Pendiente')
    BEGIN
        ROLLBACK;
        RAISERROR('La tarea ya no está Pendiente.', 16, 1);
        RETURN;
    END

    DECLARE @JsonBase nvarchar(max) =
        CASE WHEN ISJSON(@Datos) = 1 THEN @Datos ELSE N'{}' END;

    -- 1) Cerrar original
    UPDATE dbo.WF_Tarea
       SET Estado = 'Completada',
           Resultado = 'Escalada',
           FechaCierre = @Ahora,
           Datos = JSON_MODIFY(
                    JSON_MODIFY(
                      JSON_MODIFY(
                        JSON_MODIFY(@JsonBase, '$.escalado', 'true'),
                        '$.escaladoEn', CONVERT(varchar(33), @Ahora, 126)
                      ),
                      '$.escaladoMotivo', ISNULL(@Motivo,'SLA vencido')
                    ),
                    '$.escaladoPor', ISNULL(@Usuario,'system')
                  )
     WHERE Id = @TareaIdOriginal;

    -- 2) JSON nueva tarea
    DECLARE @JsonNueva nvarchar(max) = @JsonBase;

    -- limpiar flags del original
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escalado', NULL);
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escaladoEn', NULL);
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escaladoMotivo', NULL);
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escaladoPor', NULL);

    -- NO heredar “encolado” del original
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escalamientoEncolado', NULL);
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escalamientoEncoladoEn', NULL);
    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.escalamientoEncoladoMotivo', NULL);

    -- ✅ ORIGEN como objeto completo (robusto)
    DECLARE @OrigenObj nvarchar(max) =
        N'{' +
        N'"tareaId":"' + CONVERT(nvarchar(50), @TareaIdOriginal) + N'",' +
        N'"motivo":"' + STRING_ESCAPE(ISNULL(@Motivo,'SLA vencido'), 'json') + N'",' +
        N'"rolOriginal":"' + STRING_ESCAPE(ISNULL(@RolDestinoOriginal,''), 'json') + N'",' +
        N'"rolNuevo":"' + STRING_ESCAPE(ISNULL(@NuevoRolDestino,''), 'json') + N'",' +
        N'"creadaEn":"' + CONVERT(nvarchar(33), @Ahora, 126) + N'",' +
        N'"creadaPor":"' + STRING_ESCAPE(ISNULL(@Usuario,'system'), 'json') + N'"' +
        N'}';

    SET @JsonNueva = JSON_MODIFY(@JsonNueva, '$.origenEscalamiento', JSON_QUERY(@OrigenObj));

    -- si por alguna razón quedara inválido, cortamos (no insertamos basura)
    IF ISJSON(@JsonNueva) <> 1
    BEGIN
        ROLLBACK;
        RAISERROR('JsonNueva inválido luego de construir origenEscalamiento.', 16, 1);
        RETURN;
    END

    -- 3) Crear nueva tarea
    DECLARE @NuevaTareaId bigint;

    INSERT INTO dbo.WF_Tarea
        (WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion,
         RolDestino, UsuarioAsignado, Estado, Resultado,
         FechaCreacion, FechaVencimiento, FechaCierre, Datos, ScopeKey, AsignadoA,
         OrigenTareaId)
    VALUES
        (@WF_InstanciaId, @NodoId, @NodoTipo, @Titulo, @Descripcion,
         @NuevoRolDestino, NULL, 'Pendiente', NULL,
         @Ahora, NULL, NULL,
         @JsonNueva,
         @ScopeKey, NULL,
         @TareaIdOriginal);

    SET @NuevaTareaId = SCOPE_IDENTITY();

    COMMIT;

    SELECT
        @TareaIdOriginal AS TareaOriginalId,
        @NuevaTareaId    AS TareaNuevaId,
        @RolDestinoOriginal AS RolOriginal,
        @NuevoRolDestino AS RolNuevo;
END








CREATE OR ALTER PROCEDURE dbo.WF_Tarea_Historial
    @TareaId bigint
AS
BEGIN
    SET NOCOUNT ON;

    IF @TareaId IS NULL OR @TareaId <= 0
    BEGIN
        SELECT CAST(0 AS int) AS ok, 'TareaId inválido' AS error;
        RETURN;
    END

    ;WITH BackChain AS (
        -- arrancamos desde la tarea indicada
        SELECT
            t.Id,
            t.OrigenTareaId,
            0 AS lvl
        FROM dbo.WF_Tarea t
        WHERE t.Id = @TareaId

        UNION ALL

        -- subimos hacia la raíz
        SELECT
            t2.Id,
            t2.OrigenTareaId,
            bc.lvl + 1
        FROM dbo.WF_Tarea t2
        INNER JOIN BackChain bc ON bc.OrigenTareaId = t2.Id
    ),
    Root AS (
        SELECT TOP (1) Id AS RootId
        FROM BackChain
        ORDER BY lvl DESC
    ),
    FwdChain AS (
        -- desde raíz hacia adelante
        SELECT
            t.Id,
            t.OrigenTareaId,
            0 AS nivel
        FROM dbo.WF_Tarea t
        CROSS JOIN Root r
        WHERE t.Id = r.RootId

        UNION ALL

        -- siguiente tarea: la que tenga OrigenTareaId = Id actual
        SELECT
            t2.Id,
            t2.OrigenTareaId,
            fc.nivel + 1
        FROM dbo.WF_Tarea t2
        INNER JOIN FwdChain fc ON t2.OrigenTareaId = fc.Id
    )
    SELECT
        1 AS ok,
        t.Id,
        t.WF_InstanciaId,
        t.NodoId,
        t.NodoTipo,
        t.Titulo,
        t.RolDestino,
        t.Estado,
        t.Resultado,
        t.FechaCreacion,
        t.FechaVencimiento,
        t.FechaCierre,
        t.AsignadoA,
        t.ScopeKey,
        t.OrigenTareaId,
        fc.nivel AS Nivel,
        JSON_QUERY(t.Datos, '$.origenEscalamiento') AS OrigenEscalamientoObj,
        t.Datos
    FROM FwdChain fc
    JOIN dbo.WF_Tarea t ON t.Id = fc.Id
    ORDER BY fc.nivel ASC
    OPTION (MAXRECURSION 100);
END
GO
