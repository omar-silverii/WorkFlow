-- 1) Cabecera del workflow
IF OBJECT_ID('dbo.Workflow', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Workflow (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name        NVARCHAR(200) NOT NULL,
        IsActive    BIT NOT NULL DEFAULT(1),
        CreatedAt   DATETIME NOT NULL DEFAULT(GETDATE()),
        CreatedBy   NVARCHAR(100) NULL
    );
END
GO

-- 2) Versiones del workflow (cada vez que lo guardás desde el editor)
IF OBJECT_ID('dbo.WorkflowVersion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkflowVersion (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WorkflowId      INT NOT NULL,
        VersionNumber   INT NOT NULL,
        JsonPayload     NVARCHAR(MAX) NOT NULL,
        CreatedAt       DATETIME NOT NULL DEFAULT(GETDATE()),
        CreatedBy       NVARCHAR(100) NULL,
        CONSTRAINT FK_WorkflowVersion_Workflow
            FOREIGN KEY (WorkflowId) REFERENCES dbo.Workflow(Id)
    );
    -- Para que no se repita el número de versión por workflow
    CREATE UNIQUE INDEX IX_WorkflowVersion_1
        ON dbo.WorkflowVersion(WorkflowId, VersionNumber);
END
GO

-- 3) Nodos de una versión
IF OBJECT_ID('dbo.WorkflowNode', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkflowNode (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WorkflowVersionId INT NOT NULL,
        NodeId          NVARCHAR(50) NOT NULL,   -- el id que viene del canvas (n123...)
        NodeType        NVARCHAR(200) NOT NULL,  -- ej: http.request
        Label           NVARCHAR(200) NULL,
        ParamsJson      NVARCHAR(MAX) NULL,
        CONSTRAINT FK_WorkflowNode_WorkflowVersion
            FOREIGN KEY (WorkflowVersionId) REFERENCES dbo.WorkflowVersion(Id)
    );
    CREATE INDEX IX_WorkflowNode_1 ON dbo.WorkflowNode(WorkflowVersionId);
END
GO

-- 4) Aristas de una versión
IF OBJECT_ID('dbo.WorkflowEdge', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkflowEdge (
        Id              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WorkflowVersionId INT NOT NULL,
        FromNodeId      NVARCHAR(50) NOT NULL,
        ToNodeId        NVARCHAR(50) NOT NULL,
        Condition       NVARCHAR(50) NOT NULL DEFAULT('always'),
        CONSTRAINT FK_WorkflowEdge_WorkflowVersion
            FOREIGN KEY (WorkflowVersionId) REFERENCES dbo.WorkflowVersion(Id)
    );
    CREATE INDEX IX_WorkflowEdge_1 ON dbo.WorkflowEdge(WorkflowVersionId);
END
GO
ALTER TABLE dbo.WF_Instancia
ADD CreadoPor NVARCHAR(100) NULL;
GO
--  PolizasDemo
IF OBJECT_ID('dbo.PolizasDemo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PolizasDemo (
        Id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Numero      NVARCHAR(50) NOT NULL,
        Asegurado   NVARCHAR(200) NOT NULL,
        FechaAlta   DATETIME NOT NULL DEFAULT(GETDATE())
    );
END
GO