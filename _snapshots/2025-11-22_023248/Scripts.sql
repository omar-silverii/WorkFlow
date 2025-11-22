/* 1) Definiciones de workflow (los “dibujos”) */
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_Definicion')
BEGIN
    CREATE TABLE dbo.WF_Definicion (
        Id             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Codigo         NVARCHAR(50)  NOT NULL,      -- ej: 'ADMISION_SINIESTRO'
        Nombre         NVARCHAR(200) NOT NULL,      -- ej: 'Admisión de Siniestros'
        Version        INT           NOT NULL DEFAULT (1),
        JsonDef        NVARCHAR(MAX) NOT NULL,      -- acá va el JSON del canvas
        Activo         BIT           NOT NULL DEFAULT (1),
        FechaCreacion  DATETIME      NOT NULL DEFAULT (GETDATE()),
        CreadoPor      NVARCHAR(100) NULL
    );

    CREATE UNIQUE INDEX IX_WF_Definicion_Codigo_Version
        ON dbo.WF_Definicion (Codigo, Version);
END
GO

/* 2) Instancias (cada ejecución concreta del workflow) */
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_Instancia')
BEGIN
    CREATE TABLE dbo.WF_Instancia (
        Id               BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WF_DefinicionId  INT NOT NULL,
        Estado           NVARCHAR(30) NOT NULL DEFAULT ('Pendiente'),
        -- datos que vienen del sistema de pólizas / siniestros
        DatosEntrada     NVARCHAR(MAX) NULL,
        -- el motor puede ir guardando contexto (payload, variables, etc.)
        DatosContexto    NVARCHAR(MAX) NULL,
        FechaInicio      DATETIME NOT NULL DEFAULT (GETDATE()),
        FechaFin         DATETIME NULL
    );

    ALTER TABLE dbo.WF_Instancia
      ADD CONSTRAINT FK_WF_Instancia_Def
      FOREIGN KEY (WF_DefinicionId)
      REFERENCES dbo.WF_Definicion (Id);
END
GO

/* 3) Logs de ejecución (opcional pero muy útil para ver qué paso por cada nodo) */
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_InstanciaLog')
BEGIN
    CREATE TABLE dbo.WF_InstanciaLog (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WF_InstanciaId  BIGINT NOT NULL,
        FechaLog        DATETIME NOT NULL DEFAULT (GETDATE()),
        Nivel           NVARCHAR(20) NOT NULL DEFAULT ('Info'),
        Mensaje         NVARCHAR(4000) NULL,
        NodoId          NVARCHAR(100) NULL,
        NodoTipo        NVARCHAR(100) NULL,
        Datos           NVARCHAR(MAX) NULL
    );

    ALTER TABLE dbo.WF_InstanciaLog
      ADD CONSTRAINT FK_WF_InstanciaLog_Inst
      FOREIGN KEY (WF_InstanciaId)
      REFERENCES dbo.WF_Instancia (Id);
END
GO
