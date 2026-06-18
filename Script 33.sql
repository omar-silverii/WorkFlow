/*
    FIX19 — Lectura de notificaciones por usuario

    Objetivo:
    - Mantener dbo.WF_Notificacion como cabecera/mensaje/destino general.
    - Registrar la lectura individual en dbo.WF_NotificacionLectura.
    - Evitar que una notificación dirigida a rol quede leída para todos cuando la lee un solo usuario.

    Ejecutar sobre la misma base DefaultConnection donde ya existe dbo.WF_Notificacion.
*/

IF OBJECT_ID('dbo.WF_Notificacion', 'U') IS NULL
BEGIN
    RAISERROR('No existe dbo.WF_Notificacion. Ejecutar primero el script de FIX17/notificaciones.', 16, 1);
    RETURN;
END;

IF OBJECT_ID('dbo.WF_NotificacionLectura', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_NotificacionLectura
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WF_NotificacionLectura PRIMARY KEY,
        NotificacionId  BIGINT NOT NULL,
        Usuario         NVARCHAR(200) NOT NULL,
        FechaLeido      DATETIME NOT NULL CONSTRAINT DF_WF_NotificacionLectura_FechaLeido DEFAULT (GETDATE())
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_WF_NotificacionLectura_WF_Notificacion'
      AND parent_object_id = OBJECT_ID('dbo.WF_NotificacionLectura')
)
BEGIN
    ALTER TABLE dbo.WF_NotificacionLectura
    ADD CONSTRAINT FK_WF_NotificacionLectura_WF_Notificacion
        FOREIGN KEY (NotificacionId)
        REFERENCES dbo.WF_Notificacion (Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_WF_NotificacionLectura_Notificacion_Usuario'
      AND object_id = OBJECT_ID('dbo.WF_NotificacionLectura')
)
BEGIN
    CREATE UNIQUE INDEX UX_WF_NotificacionLectura_Notificacion_Usuario
    ON dbo.WF_NotificacionLectura (NotificacionId, Usuario);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_WF_NotificacionLectura_Usuario'
      AND object_id = OBJECT_ID('dbo.WF_NotificacionLectura')
)
BEGIN
    CREATE INDEX IX_WF_NotificacionLectura_Usuario
    ON dbo.WF_NotificacionLectura (Usuario, FechaLeido DESC)
    INCLUDE (NotificacionId);
END;

/*
    Migración de lecturas existentes del modelo anterior.
    - Si LeidoPor existe, se conserva como usuario que leyó.
    - Si no hay LeidoPor pero era notificación directa, se usa UsuarioDestino.
    - Las notificaciones por rol leídas globalmente dejan de ocultarse para todo el rol:
      solo quedan leídas para el usuario que figura en LeidoPor, si existe.
*/
INSERT INTO dbo.WF_NotificacionLectura (NotificacionId, Usuario, FechaLeido)
SELECT
    N.Id,
    X.UsuarioLectura,
    ISNULL(N.FechaLeido, GETDATE())
FROM dbo.WF_Notificacion N
CROSS APPLY
(
    SELECT UsuarioLectura = NULLIF(LTRIM(RTRIM(COALESCE(NULLIF(N.LeidoPor, ''), NULLIF(N.UsuarioDestino, '')))), '')
) X
WHERE
    ISNULL(N.Leido, 0) = 1
    AND X.UsuarioLectura IS NOT NULL
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.WF_NotificacionLectura L
        WHERE L.NotificacionId = N.Id
          AND L.Usuario = X.UsuarioLectura
    );

PRINT 'FIX19 aplicado: dbo.WF_NotificacionLectura creada/verificada y lecturas existentes migradas por usuario.';
