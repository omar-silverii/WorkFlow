/*

PRUEBA LIMPIA (SQL) — Paso a paso

Paso A) Elegir una tarea de prueba (pool Pendiente)

Esto elige una tarea pool que NO esté asignada y NO esté cerrada:

*/

DECLARE @TareaId bigint;

SELECT TOP (1)
    @TareaId = Id
FROM dbo.WF_Tarea
WHERE Estado = 'Pendiente'
  AND (AsignadoA IS NULL OR LTRIM(RTRIM(AsignadoA)) = '')
ORDER BY Id DESC;

SELECT @TareaId AS TareaIdPrueba;

/*
Paso B) “Reset” limpio de esa tarea (dejarla elegible)

Esto hace que el escalamiento sea repetible:

select * from WF_Tarea where id = 10070

*/

DECLARE @TareaId bigint = 10070;  -- <-- reemplazá por el TareaIdPrueba que te dio arriba

UPDATE dbo.WF_Tarea
SET
    Estado = 'Pendiente',
    Resultado = NULL,
    FechaCierre = NULL,
    AsignadoA = NULL,
    UsuarioAsignado = NULL,
    FechaVencimiento = DATEADD(minute, -10, GETDATE()),
    Datos = JSON_MODIFY(
              JSON_MODIFY(
                JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalamientoEncolado', NULL),
                '$.escalamientoEncoladoEn', NULL
              ),
              '$.escalamientoEncoladoMotivo', NULL
           )
WHERE Id = @TareaId;

SELECT Id, Estado, FechaVencimiento, AsignadoA, Datos
FROM dbo.WF_Tarea
WHERE Id = @TareaId;


/*
Paso C) Limpiar cola wf.escalamiento SOLO de esa tarea (opcional, ultra prolijo)

Así evitás “consumir otro mensaje viejo”:

select * from WF_Queue where CorrelationId = '10070'
*/

DECLARE @TareaId bigint = 10070; -- <-- mismo

DELETE FROM dbo.WF_Queue
WHERE Queue = 'wf.escalamiento'
  AND CorrelationId = CONVERT(nvarchar(100), @TareaId);

/*
Paso D) Ejecutar el SP que encola
despues de corre lo de abajo 
✅ Acá tenés que ver un mensaje nuevo y que en el payload aparezca tu tarea.

IMPORTANTE: Anotá el Id del mensaje (ej: 25).

antes de esto ejecuto 

UPDATE dbo.WF_Tarea
SET Datos = JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalamientoEncolado', NULL)
WHERE Id = 10070;
go
UPDATE dbo.WF_Tarea
SET 
    Datos = JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalamientoEncoladoEn', NULL)
WHERE Id = 10070;
go
UPDATE dbo.WF_Tarea
SET 
    Datos = JSON_MODIFY(ISNULL(Datos,'{}'), '$.escalamientoEncoladoMotivo', NULL)
WHERE Id = 10070;


SELECT
    Id,
    Estado,
    FechaVencimiento,
    AsignadoA,
    Datos
FROM dbo.WF_Tarea
WHERE Estado = 'Pendiente'
  AND FechaVencimiento IS NOT NULL
  AND FechaVencimiento < GETDATE()
  AND (AsignadoA IS NULL OR LTRIM(RTRIM(AsignadoA)) = '')
  AND (
        Datos IS NULL
     OR JSON_VALUE(Datos,'$.escalamientoEncolado') IS NULL
     OR JSON_VALUE(Datos,'$.escalamientoEncolado') <> 'true'
  );





*/

EXEC dbo.WF_Tarea_Escalar_Pendientes;
/*
SELECT
    Id,
    Datos
FROM dbo.WF_Tarea
WHERE Id = 10070

select * from WF_Setting

UPDATE dbo.WF_Setting
SET Value = '{
  "OPERADOR": "SUPERVISOR",
  "SUPERVISOR": "GERENTE",
  "RECECEPCION": "SUPERVISOR",
  "RRHH": "GERENCIA",
  "JEFE_AREA": "GERENCIA"
}'
WHERE SettingKey = 'wf.escalamiento.roleMap'
  AND ScopeKey IS NULL;

-- Forzamos un map global que incluya RRHH -> GERENCIA
IF EXISTS (SELECT 1 FROM dbo.WF_Setting WHERE SettingKey='wf.escalamiento.roleMap' AND ScopeKey IS NULL)
BEGIN
    UPDATE dbo.WF_Setting
    SET Value = '{ "RRHH":"GERENCIA" }', Activo=1, UpdatedAt=GETDATE()
    WHERE SettingKey='wf.escalamiento.roleMap' AND ScopeKey IS NULL;
END
ELSE
BEGIN
    INSERT INTO dbo.WF_Setting(SettingKey, ScopeKey, Value, Activo)
    VALUES ('wf.escalamiento.roleMap', NULL, '{ "RRHH":"GERENCIA" }', 1);
END
GO

DECLARE @Ahora datetime = GETDATE();

INSERT INTO dbo.WF_Tarea
(
  WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion,
  RolDestino, UsuarioAsignado, Estado, Resultado,
  FechaCreacion, FechaVencimiento, FechaCierre, Datos, ScopeKey, AsignadoA
)
VALUES
(
  1, -- poné una instanciaId válida si querés (puede ser cualquiera existente)
  'test.node', 'human.task',
  'PRUEBA SLA RRHH',
  'Tarea creada para test aislado de escalamiento',
  'RRHH',
  NULL,
  'Pendiente',
  NULL,
  DATEADD(MINUTE,-10,@Ahora),
  DATEADD(MINUTE,-5,@Ahora),   -- vencida
  NULL,
  '{}',
  NULL,
  NULL
);

SELECT CAST(SCOPE_IDENTITY() AS bigint) AS TareaPruebaId;

DECLARE @TareaId bigint = 10071;

SELECT
  Id, Estado, RolDestino, FechaVencimiento, AsignadoA,
  JSON_VALUE(ISNULL(Datos,'{}'),'$.escalamientoEncolado') AS escalamientoEncolado
FROM dbo.WF_Tarea
WHERE Id = @TareaId;

EXEC dbo.WF_Tarea_Escalar_Pendientes;


DECLARE @TareaId bigint = 10071;

SELECT TOP 5 *
FROM dbo.WF_Queue
WHERE Queue='wf.escalamiento'
  AND CorrelationId = CAST(@TareaId AS nvarchar(100))
ORDER BY Id DESC;

https://localhost:44350/Api/Generico.ashx?action=worker.escalamiento.run
https://localhost:44350/Api/Generico.ashx?action=worker.escalamiento.run&tareaId=10071

{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":31,"tareaId":59,"instanciaId":184,"rolDestino":"JEFE_AREA","nuevoRol":null,"escaladoReal":false,"tareaNuevaId":0,"error":"No hay rol de escalamiento para rolDestino=\u0027JEFE_AREA\u0027"}

DECLARE @TareaId bigint = 10071;

-- Original
SELECT
  Id, Estado, Resultado, FechaCierre, RolDestino, Datos
FROM dbo.WF_Tarea
WHERE Id = @TareaId;

-- Nueva (referenciada desde Datos.origenEscalamiento.tareaId)
SELECT TOP 10
  Id, Estado, Resultado, FechaCreacion, RolDestino, Datos
FROM dbo.WF_Tarea
WHERE JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') = CAST(@TareaId AS nvarchar(50))
ORDER BY Id DESC;
--


*/
SELECT TOP 5
  Id, Queue, CorrelationId, CreatedAt, Processed, Attempts, LastError, Payload
FROM dbo.WF_Queue
WHERE Queue='wf.escalamiento'
ORDER BY Id DESC;

/*

✅ Paso E) Ejecutar el worker (una vez)

Abrís:
https://localhost:44350/Api/Generico.ashx?action=worker.escalamiento.run

*/
{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":29,"tareaId":57,"instanciaId":182,"rolDestino":"JEFE_AREA","nuevoRol":null,"escaladoReal":false,"tareaNuevaId":0,"error":"No hay rol de escalamiento para rolDestino=\u0027JEFE_AREA\u0027"}
{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":27,"tareaId":6,"instanciaId":34,"rolDestino":"Recepcion","nuevoRol":null,"escaladoReal":false,"tareaNuevaId":0,"error":"No hay rol de escalamiento para rolDestino=\u0027Recepcion\u0027"}
{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":28,"tareaId":8,"instanciaId":35,"rolDestino":"RRHH","nuevoRol":null,"escaladoReal":false,"tareaNuevaId":0,"error":"No hay rol de escalamiento para rolDestino=\u0027RRHH\u0027"}
/*
✅ Paso F) Verificaciones finales (las 3 que mandan)
1) Mensaje procesado
25	wf.escalamiento	10070	{"tipo":"SLA_ESCALADO","tareaId":10070,"instanciaId":191,"rolDestino":"FINANZAS","scopeKey":"","titulo":"Aprobación Finanzas","fechaVencimiento":"2025-12-30T07:52:51.350","escaladoEn":"2025-12-30T08:09:07.733"}	2025-12-30 08:09:07.733	NULL	10	0	NULL	0	NULL
25	1	2025-12-30 08:12:10.823	1	NULL
*/

SELECT Id, Processed, ProcessedAt, Attempts, LastError
FROM dbo.WF_Queue
WHERE Id = 35;  -- <-- Id del mensaje

/*
2) Tarea original cerrada como Escalada

*/

SELECT Id, Estado, Resultado, FechaCierre, RolDestino, Datos
FROM dbo.WF_Tarea
WHERE Id = 33; -- <-- tu tarea

/*
✅ Esperado: Estado='Completada', Resultado='Escalada', FechaCierre NOT NULL.

3) Nueva tarea creada por escalamiento (hija)
*/
SELECT TOP 10
  Id, WF_InstanciaId, Estado, RolDestino, FechaCreacion, Datos
FROM dbo.WF_Tarea
WHERE JSON_VALUE(Datos, '$.origenEscalamiento.tareaId') = '10071'
ORDER BY Id DESC;


{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":43,"tareaId":10071,"instanciaId":1,"rolDestino":"RRHH","nuevoRol":null,"escaladoReal":false,"tareaNuevaId":0,"error":"No hay rol de escalamiento para rolDestino=\u0027RRHH\u0027"}
nuevo 4 select para ver que paso
1) Ver el mensaje consumido (ya procesado)

SELECT *
FROM dbo.WF_Queue
WHERE Id = 43;

Vas a ver:
Processed=1
ProcessedAt seteado
Attempts=1


2) Ver la tarea original (10071) y si cambió algo

SELECT
  Id, Estado, Resultado, FechaCierre, RolDestino, AsignadoA, FechaVencimiento,
  Datos
FROM dbo.WF_Tarea
WHERE Id = 10071;

📌 Importante: con tu worker actual, si nuevoRol=null, NO ejecuta WF_Tarea_Escalar_CrearNueva, entonces:
la tarea no se cierra
no se crea tarea nueva
solo queda el flag que puso WF_Tarea_Escalar_Pendientes en Datos (escalado=true etc.)

3) Ver si generó notificación (wf.notificaciones)
Como el worker siempre encola notificación aunque no escale real, confirmalo:

SELECT TOP 10 *
FROM dbo.WF_Queue
WHERE Queue='wf.notificaciones'
  AND CorrelationId='10071'
ORDER BY Id DESC;

✅ Debería aparecer una fila nueva.

4) Ver si existe tarea nueva creada por escalamiento (probablemente NO)

SELECT TOP 10
  Id, Estado, Resultado, FechaCreacion, RolDestino, Datos
FROM dbo.WF_Tarea
WHERE JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') = '10071'
ORDER BY Id DESC;

Esto te confirma sin dudas si se creó una nueva.



SELECT TOP 20
  SettingKey, ScopeKey, Value, Activo
FROM dbo.WF_Setting
WHERE SettingKey LIKE 'wf.escalamiento%'
ORDER BY SettingKey, ScopeKey;




INSERT INTO dbo.WF_Queue (Queue, CorrelationId, Payload, CreatedAt, DueAt, Priority, Processed, Attempts)
VALUES (
 'wf.escalamiento',
 '10071',
 '{"tipo":"SLA_ESCALADO","tareaId":10071,"instanciaId":1,"rolDestino":"RRHH","scopeKey":"","titulo":"PRUEBA SLA RRHH","fechaVencimiento":"2025-12-30T09:57:19.573","escaladoEn":"2025-12-30T11:54:53.560"}',
 GETDATE(), NULL, 10, 0, 0
);

SELECT Id, Estado, Resultado, FechaCierre, RolDestino, Datos
FROM dbo.WF_Tarea
WHERE Id = 10071;

SELECT 
  Id, Estado, RolDestino, FechaCreacion, Datos
FROM dbo.WF_Tarea
WHERE JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') = '10071'
ORDER BY Id DESC;

SELECT Id, RolDestino, Estado,
       JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') AS OrigenTareaId,
       Datos
FROM dbo.WF_Tarea
WHERE Id = 10072;

SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.WF_Tarea_Escalar_CrearNueva')) AS SP_Texto;

DECLARE @Nueva bigint;

INSERT INTO dbo.WF_Tarea
(WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion, RolDestino, Estado, FechaCreacion, FechaVencimiento, Datos, ScopeKey)
VALUES
(1, 'N_TEST', 'human.task', 'PRUEBA SLA RRHH 2', 'test', 'RRHH', 'Pendiente', GETDATE(), DATEADD(MINUTE, -5, GETDATE()), N'{}', '');

SET @Nueva = SCOPE_IDENTITY();
SELECT @Nueva AS TareaIdCreada;

SELECT TOP 10
  Id, Estado, RolDestino, FechaCreacion,
  JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') AS OrigenTareaId,
  Datos
FROM dbo.WF_Tarea
WHERE JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') IS NOT NULL
ORDER BY Id DESC;


UPDATE dbo.WF_Tarea
SET Datos =
    JSON_MODIFY(
      JSON_MODIFY(ISNULL(Datos,'{}'), '$.origenEscalamiento.tareaId', '10071'),
      '$.origenEscalamiento.motivo', 'SLA vencido'
    )
WHERE Id = 10072;

EXEC sp_helptext N'dbo.WF_Tarea_Escalar_CrearNueva';



--/////////////////////////////////////////////////////////////////////////////////

Prueba limpia y 100% controlada (sin “descontrol”)

La forma profesional de probar esto es: dejamos 1 solo mensaje pendiente en wf.escalamiento, y lo consumimos.

Paso 0: vaciar la cola de escalamiento (solo para test)

(no borro nada; lo marco procesado para que no lo agarre el worker)

UPDATE dbo.WF_Queue
SET Processed = 1,
    ProcessedAt = ISNULL(ProcessedAt, GETDATE())
WHERE Queue = 'wf.escalamiento'
  AND Processed = 0;

Paso 1: creás una tarea vencida de prueba (RRHH)
DECLARE @TareaId bigint;

INSERT INTO dbo.WF_Tarea
(WF_InstanciaId, NodoId, NodoTipo, Titulo, Descripcion, RolDestino, Estado, FechaCreacion, FechaVencimiento, Datos, ScopeKey)
VALUES
(1, 'N_TEST', 'human.task', 'PRUEBA SLA RRHH CONTROLADA', 'test', 'RRHH', 'Pendiente', GETDATE(), DATEADD(MINUTE,-10,GETDATE()), N'{}', '');

SET @TareaId = SCOPE_IDENTITY();
SELECT @TareaId AS TareaIdCreada;

Paso 2: corrés el SP que encola (WF_Tarea_Escalar_Pendientes)
EXEC dbo.WF_Tarea_Escalar_Pendientes;

Paso 3: confirmás que hay 1 solo mensaje pendiente y cuál es
SELECT TOP 10 Id, Queue, CorrelationId, Payload, CreatedAt, Priority, Processed
FROM dbo.WF_Queue
WHERE Queue='wf.escalamiento'
ORDER BY Id DESC;

SELECT COUNT(*) AS Pendientes
FROM dbo.WF_Queue
WHERE Queue='wf.escalamiento' AND Processed=0;


✅ Debe dar Pendientes = 1.

Paso 4: ahora sí, corrés el worker (tu endpoint)

Ahí el worker no puede agarrar otro mensaje, porque no hay otro pendiente.

Paso 5: verificás qué creó el SP (la tarea nueva con origenEscalamiento)

Tomás el Id original (el TareaIdCreada del paso 1) y corrés:

DECLARE @Orig bigint = 10077;

SELECT TOP 20
  Id, Estado, RolDestino, Resultado, FechaCreacion, FechaCierre,
  JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') AS OrigenTareaId,
  Datos
FROM dbo.WF_Tarea
WHERE Id = @Orig
   OR JSON_VALUE(ISNULL(Datos,'{}'),'$.origenEscalamiento.tareaId') = CAST(@Orig AS nvarchar(50))
ORDER BY Id DESC;


✅ Resultado esperado:

La original: Estado = Completada, Resultado = Escalada, FechaCierre != NULL

Una nueva: Estado = Pendiente, RolDestino = GERENCIA (porque tu setting RRHH -> GERENCIA), y OrigenTareaId = @Orig


--//////////////////////////////////////////////////////////////
{"escalamientoEncolado":"true","escalamientoEncoladoEn":"2026-01-01T20:40:53.183","escalamientoEncoladoMotivo":"SLA vencido","escalado":"true","escaladoEn":"2026-01-01T21:02:22.453","escaladoMotivo":"SLA vencido","escaladoPor":"system"}
SELECT
  SettingKey, ScopeKey, Value, Activo
FROM dbo.WF_Setting
WHERE SettingKey = 'wf.escalamiento.roleMap'
ORDER BY CASE WHEN ScopeKey IS NULL THEN 1 ELSE 0 END, ScopeKey;

INSERT INTO dbo.WF_Setting (SettingKey, ScopeKey, Value, Activo)
VALUES ('wf.escalamiento.roleMap', '', '{ "RRHH":"GERENCIA" }', 1);

SELECT
  Id,
  Estado,
  RolDestino,
  FechaCreacion,
  ISJSON(Datos) AS DatosEsJson,
  JSON_VALUE(CASE WHEN ISJSON(Datos)=1 THEN Datos ELSE N'{}' END, '$.origenEscalamiento.tareaId') AS OrigenJson,
  Datos
FROM dbo.WF_Tarea
WHERE Id IN (10077,10078);

sp_help WF_Tarea



SELECT TOP 50
  Id, Estado, RolDestino, Resultado, FechaCreacion, FechaCierre,
  OrigenTareaId,
  JSON_VALUE(CASE WHEN ISJSON(Datos)=1 THEN Datos ELSE N'{}' END,'$.origenEscalamiento.tareaId') AS OrigenJson,
  Datos
FROM dbo.WF_Tarea
WHERE Id = @Orig
   OR OrigenTareaId = @Orig
   OR JSON_VALUE(CASE WHEN ISJSON(Datos)=1 THEN Datos ELSE N'{}' END,'$.origenEscalamiento.tareaId') = CAST(@Orig AS nvarchar(50))
ORDER BY Id DESC;



DECLARE @Orig bigint = 10078;

SELECT TOP 50
  Id, Estado, RolDestino, Resultado, FechaCreacion, FechaCierre,
  OrigenTareaId,
  JSON_VALUE(CASE WHEN ISJSON(Datos)=1 THEN Datos ELSE N'{}' END,'$.origenEscalamiento.tareaId') AS OrigenJson,
  Datos
FROM dbo.WF_Tarea
WHERE Id = @Orig OR OrigenTareaId = @Orig
ORDER BY Id DESC;


DECLARE @Orig bigint = 10076;

SELECT TOP 50
  Id, Estado, RolDestino, Resultado, FechaCreacion, FechaCierre,
  OrigenTareaId,
  JSON_VALUE(CASE WHEN ISJSON(Datos)=1 THEN Datos ELSE N'{}' END,'$.origenEscalamiento.tareaId') AS OrigenJson,
  Datos
FROM dbo.WF_Tarea
WHERE Id = @Orig OR OrigenTareaId = @Orig
ORDER BY Id DESC;



{"ok":true,"processed":1,"consumedQueue":"wf.escalamiento","producedQueue":"wf.notificaciones","msgId":49,"tareaId":10073,"instanciaId":1,"rolDestino":"RRHH","nuevoRol":"GERENCIA","escaladoReal":true,"tareaNuevaId":10075,"error":null}