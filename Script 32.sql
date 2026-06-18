
IF OBJECT_ID('dbo.WF_Notificacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_Notificacion
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,

        FechaCreacion     DATETIME NOT NULL CONSTRAINT DF_WF_Notificacion_FechaCreacion DEFAULT (GETDATE()),

        WF_InstanciaId    BIGINT NULL,
        WF_DefinicionId   INT NULL,
        NodoId            NVARCHAR(50) NULL,
        NodoTipo          NVARCHAR(100) NULL,

        Tipo              NVARCHAR(30) NOT NULL CONSTRAINT DF_WF_Notificacion_Tipo DEFAULT (N'sistema'),
        Canal             NVARCHAR(30) NOT NULL CONSTRAINT DF_WF_Notificacion_Canal DEFAULT (N'sistema'),
        Prioridad         NVARCHAR(20) NOT NULL CONSTRAINT DF_WF_Notificacion_Prioridad DEFAULT (N'normal'),

        Titulo            NVARCHAR(200) NOT NULL,
        Mensaje           NVARCHAR(MAX) NULL,

        UsuarioDestino    NVARCHAR(200) NULL,
        RolDestino        NVARCHAR(100) NULL,
        Destino           NVARCHAR(300) NULL,

        UrlAccion         NVARCHAR(500) NULL,

        Leido             BIT NOT NULL CONSTRAINT DF_WF_Notificacion_Leido DEFAULT (0),
        FechaLeido        DATETIME NULL,
        LeidoPor          NVARCHAR(200) NULL,

        CreadoPor         NVARCHAR(200) NULL,
        DatosJson         NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WF_Notificacion_Usuario_Leido' AND object_id = OBJECT_ID('dbo.WF_Notificacion'))
BEGIN
    CREATE INDEX IX_WF_Notificacion_Usuario_Leido
    ON dbo.WF_Notificacion (UsuarioDestino, Leido, FechaCreacion DESC)
    INCLUDE (Titulo, Prioridad, WF_InstanciaId, WF_DefinicionId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WF_Notificacion_Rol_Leido' AND object_id = OBJECT_ID('dbo.WF_Notificacion'))
BEGIN
    CREATE INDEX IX_WF_Notificacion_Rol_Leido
    ON dbo.WF_Notificacion (RolDestino, Leido, FechaCreacion DESC)
    INCLUDE (Titulo, Prioridad, WF_InstanciaId, WF_DefinicionId);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WF_Notificacion_Instancia' AND object_id = OBJECT_ID('dbo.WF_Notificacion'))
BEGIN
    CREATE INDEX IX_WF_Notificacion_Instancia
    ON dbo.WF_Notificacion (WF_InstanciaId, FechaCreacion DESC);
END
GO
