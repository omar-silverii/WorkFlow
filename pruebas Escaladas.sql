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

DECLARE @Orig bigint = 10079;

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