SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.WF_IngresoRuta', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_IngresoRuta
    (
        Id               int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_WF_IngresoRuta PRIMARY KEY,
        Codigo           nvarchar(80) NOT NULL,
        Nombre           nvarchar(200) NOT NULL,
        CanalCodigo      nvarchar(80) NULL,
        PatronArchivo    nvarchar(260) NULL,
        Extension        nvarchar(20) NULL,
        Prioridad        int NOT NULL
            CONSTRAINT DF_WF_IngresoRuta_Prioridad DEFAULT(100),
        WF_DefinicionId  int NOT NULL,
        Activo           bit NOT NULL
            CONSTRAINT DF_WF_IngresoRuta_Activo DEFAULT(1),
        FechaCreacion    datetime NOT NULL
            CONSTRAINT DF_WF_IngresoRuta_FechaCreacion DEFAULT(GETDATE()),
        FechaActualizacion datetime NULL
    );

    CREATE UNIQUE INDEX UX_WF_IngresoRuta_Codigo
        ON dbo.WF_IngresoRuta(Codigo);

    CREATE INDEX IX_WF_IngresoRuta_Resolver
        ON dbo.WF_IngresoRuta(Activo, CanalCodigo, Prioridad DESC)
        INCLUDE (PatronArchivo, Extension, WF_DefinicionId, Nombre);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_WF_IngresoRuta_Definicion'
)
BEGIN
    ALTER TABLE dbo.WF_IngresoRuta WITH CHECK
    ADD CONSTRAINT FK_WF_IngresoRuta_Definicion
        FOREIGN KEY (WF_DefinicionId)
        REFERENCES dbo.WF_Definicion(Id);
END;

IF OBJECT_ID(N'dbo.WF_IngresoDocumento', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_IngresoDocumento
    (
        Id                 bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_WF_IngresoDocumento PRIMARY KEY,
        IngressId          nvarchar(40) NOT NULL,
        CanalCodigo        nvarchar(80) NOT NULL,
        ArchivoNombre      nvarchar(260) NOT NULL,
        Extension          nvarchar(20) NULL,
        RutaActual         nvarchar(1000) NOT NULL,
        Estado             nvarchar(40) NOT NULL
            CONSTRAINT DF_WF_IngresoDocumento_Estado DEFAULT(N'RECIBIDO'),
        WF_IngresoRutaId   int NULL,
        WF_DefinicionId    int NULL,
        WF_InstanciaId     bigint NULL,
        DocTipoCodigo      nvarchar(80) NULL,
        OrigenDecision     nvarchar(30) NULL,
        Confianza          decimal(5,2) NULL,
        MotivoDecision     nvarchar(1000) NULL,
        DecisionPor        nvarchar(120) NULL,
        UltimoError        nvarchar(max) NULL,
        FechaIngreso       datetime NOT NULL
            CONSTRAINT DF_WF_IngresoDocumento_FechaIngreso DEFAULT(GETDATE()),
        FechaDecision      datetime NULL,
        FechaInstancia     datetime NULL,
        FechaActualizacion datetime NOT NULL
            CONSTRAINT DF_WF_IngresoDocumento_FechaActualizacion DEFAULT(GETDATE())
    );

    CREATE UNIQUE INDEX UX_WF_IngresoDocumento_IngressId
        ON dbo.WF_IngresoDocumento(IngressId);

    CREATE INDEX IX_WF_IngresoDocumento_Estado_Fecha
        ON dbo.WF_IngresoDocumento(Estado, FechaIngreso DESC)
        INCLUDE (CanalCodigo, ArchivoNombre, WF_DefinicionId, WF_InstanciaId, OrigenDecision);

    CREATE INDEX IX_WF_IngresoDocumento_Instancia
        ON dbo.WF_IngresoDocumento(WF_InstanciaId)
        WHERE WF_InstanciaId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_WF_IngresoDocumento_Ruta'
)
BEGIN
    ALTER TABLE dbo.WF_IngresoDocumento WITH CHECK
    ADD CONSTRAINT FK_WF_IngresoDocumento_Ruta
        FOREIGN KEY (WF_IngresoRutaId)
        REFERENCES dbo.WF_IngresoRuta(Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_WF_IngresoDocumento_Definicion'
)
BEGIN
    ALTER TABLE dbo.WF_IngresoDocumento WITH CHECK
    ADD CONSTRAINT FK_WF_IngresoDocumento_Definicion
        FOREIGN KEY (WF_DefinicionId)
        REFERENCES dbo.WF_Definicion(Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_WF_IngresoDocumento_Instancia'
)
BEGIN
    ALTER TABLE dbo.WF_IngresoDocumento WITH CHECK
    ADD CONSTRAINT FK_WF_IngresoDocumento_Instancia
        FOREIGN KEY (WF_InstanciaId)
        REFERENCES dbo.WF_Instancia(Id);
END;

IF COL_LENGTH(N'dbo.WF_IngresoDocumento', N'DecisionPor') IS NULL
BEGIN
    ALTER TABLE dbo.WF_IngresoDocumento
    ADD DecisionPor nvarchar(120) NULL;
END;

IF OBJECT_ID(N'dbo.WF_Permiso', N'U') IS NOT NULL
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.WF_Permiso
    WHERE PermisoKey = N'INGRESO_DOCUMENTAL'
)
BEGIN
    INSERT INTO dbo.WF_Permiso(PermisoKey, Nombre, Descripcion, Activo)
    VALUES
    (
        N'INGRESO_DOCUMENTAL',
        N'Ingreso documental',
        N'Permite consultar, clasificar y enrutar documentos antes de iniciar un workflow.',
        1
    );
END;

COMMIT TRANSACTION;

SELECT
    N'OK' AS Resultado,
    OBJECT_ID(N'dbo.WF_IngresoRuta', N'U') AS WF_IngresoRutaObjectId,
    OBJECT_ID(N'dbo.WF_IngresoDocumento', N'U') AS WF_IngresoDocumentoObjectId;
