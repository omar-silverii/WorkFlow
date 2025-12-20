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

IF OBJECT_ID('dbo.PolizasIngreso', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.PolizasIngreso(
      Id            INT IDENTITY(1,1) PRIMARY KEY,
      NroPoliza     NVARCHAR(50)  NOT NULL,
      Asegurado     NVARCHAR(200) NOT NULL,
      FechaCreacion DATETIME      NOT NULL DEFAULT(GETDATE()),
      DefinicionId  INT           NULL,
      InstanciaId   BIGINT        NULL,
      Estado        NVARCHAR(50)  NULL
  );
END
GO

IF OBJECT_ID('dbo.WF_QueueMessage', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_QueueMessage
(
    Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Broker        NVARCHAR(50)  NOT NULL,          -- ej: 'sql'
    Queue         NVARCHAR(200) NOT NULL,          -- nombre lógico de la cola
    Payload       NVARCHAR(MAX) NOT NULL,          -- JSON del mensaje
    Estado        NVARCHAR(20)  NOT NULL 
                  CONSTRAINT DF_WF_QueueMessage_Estado DEFAULT ('Pendiente'),
    Intentos      INT           NOT NULL 
                  CONSTRAINT DF_WF_QueueMessage_Intentos DEFAULT (0),
    FechaCreacion DATETIME      NOT NULL 
                  CONSTRAINT DF_WF_QueueMessage_FechaCreacion DEFAULT (GETDATE())
);
END
GO

IF OBJECT_ID('dbo.WF_Queue', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_Queue
(
    Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WF_Queue PRIMARY KEY,
    QueueName       NVARCHAR(100)        NOT NULL,
    Payload         NVARCHAR(MAX)        NOT NULL,
    Estado          NVARCHAR(20)         NOT NULL DEFAULT 'Pendiente',  -- Pendiente / Procesado / Error
    Intentos        INT                  NOT NULL DEFAULT 0,
    FechaCreacion   DATETIME             NOT NULL DEFAULT GETDATE(),
    FechaDisponible DATETIME             NOT NULL DEFAULT GETDATE(),
    FechaProcesado  DATETIME             NULL,
    UltimoError     NVARCHAR(MAX)        NULL
);

CREATE INDEX IX_WF_Queue_QueueName_Estado
    ON dbo.WF_Queue (QueueName, Estado, FechaDisponible, Id);
END
GO

IF OBJECT_ID('dbo.WF_Tarea', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_Tarea
(
    Id               BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,

    WF_InstanciaId   BIGINT NOT NULL,          -- FK -> WF_Instancia.Id
    NodoId           NVARCHAR(50) NOT NULL,    -- Id del nodo en el JSON
    NodoTipo         NVARCHAR(100) NOT NULL,   -- 'human.task' (por ahora)

    Titulo           NVARCHAR(200) NOT NULL,
    Descripcion      NVARCHAR(MAX) NULL,

    RolDestino       NVARCHAR(100) NULL,       -- ej: 'Recepcion', 'RRHH', 'Medico'
    UsuarioAsignado  NVARCHAR(100) NULL,       -- login interno / legajo

    Estado           NVARCHAR(20) NOT NULL,    -- 'Pendiente','EnCurso','Completada','Cancelada','Error'
    Resultado        NVARCHAR(50) NULL,        -- ej: 'apto','no_apto','rechazado','timeout'

    FechaCreacion    DATETIME NOT NULL DEFAULT (GETDATE()),
    FechaVencimiento DATETIME NULL,
    FechaCierre      DATETIME NULL,

    Datos            NVARCHAR(MAX) NULL        -- JSON libre (extra)
);

-- Índices típicos de bandeja
CREATE INDEX IX_WF_Tarea_Instancia_Estado ON dbo.WF_Tarea (WF_InstanciaId, Estado);
CREATE INDEX IX_WF_Tarea_Usuario_Estado   ON dbo.WF_Tarea (UsuarioAsignado, Estado);
CREATE INDEX IX_WF_Tarea_Rol_Estado       ON dbo.WF_Tarea (RolDestino, Estado);

END
GO

IF OBJECT_ID('dbo.WF_DocumentoTipo', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_DocumentoTipo (
    Id INT IDENTITY PRIMARY KEY,
    Codigo VARCHAR(50) UNIQUE NOT NULL,      -- "NPC", "OC", "FACT"
    Nombre VARCHAR(200) NOT NULL,            -- "Nota de Pedido", "Orden de Compra, factura A"
    Formato VARCHAR(10) NOT NULL,            -- "pdf", "docx"
    Descripcion VARCHAR(500) NULL,
    Activo BIT NOT NULL DEFAULT 1
);
END
GO

/*
INSERT INTO WF_DocumentoTipo (Codigo, Nombre, Formato)
VALUES
('NPC', 'Nota de Pedido de Compras', 'docx'),
('OC',  'Orden de Compra', 'pdf'),
('FACT','Factura', 'pdf');
*/

IF OBJECT_ID('dbo.WF_DocumentoPlantilla', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_DocumentoPlantilla (
    Id INT IDENTITY PRIMARY KEY,
    DocumentoTipoId INT NOT NULL
        FOREIGN KEY REFERENCES WF_DocumentoTipo(Id),

    Version INT NOT NULL DEFAULT 1,

    RegexPattern NVARCHAR(MAX) NULL,
    RegexOptions VARCHAR(200) NULL,

    CamposJson NVARCHAR(MAX) NULL,   -- mapeo de fields
    FechaCreacion DATETIME NOT NULL DEFAULT GETDATE(),
    Activa BIT NOT NULL DEFAULT 1
);
END
GO


IF OBJECT_ID('dbo.WF_DocTipo', 'U') IS NULL
BEGIN
CREATE TABLE dbo.WF_DocTipo
(
    DocTipoId       INT IDENTITY(1,1) NOT NULL,
    Codigo          NVARCHAR(50)       NOT NULL, -- ej: ORDEN_COMPRA, NOTA_PEDIDO, FACTURA_VENTA
    Nombre          NVARCHAR(200)      NOT NULL, -- nombre legible
    ContextPrefix   NVARCHAR(30)       NOT NULL, -- ej: oc, np, fact

    -- Opcionales (para etapa siguiente)
    PlantillaPath   NVARCHAR(500)      NULL,     -- ruta de plantilla (docx/pdf) si aplica
    RutaBase        NVARCHAR(500)      NULL,     -- carpeta base por defecto
    EsActivo        BIT                NOT NULL CONSTRAINT DF_WF_DocTipo_EsActivo DEFAULT (1),

    -- Auditoría mínima
    CreatedAt       DATETIME2(0)       NOT NULL CONSTRAINT DF_WF_DocTipo_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt       DATETIME2(0)       NULL
);
END
GO
ALTER TABLE dbo.WF_DocTipo
    ADD CONSTRAINT PK_WF_DocTipo
        PRIMARY KEY CLUSTERED (DocTipoId);
GO

ALTER TABLE dbo.WF_DocTipo
    ADD CONSTRAINT UQ_WF_DocTipo_Codigo
        UNIQUE (Codigo);
GO

ALTER TABLE dbo.WF_DocTipo
    ADD CONSTRAINT UQ_WF_DocTipo_ContextPrefix
        UNIQUE (ContextPrefix);
GO

CREATE INDEX IX_WF_DocTipo_EsActivo
    ON dbo.WF_DocTipo (EsActivo, Codigo);
GO

IF OBJECT_ID('dbo.WF_DocTipoReglaExtract','U') IS NULL
BEGIN
    CREATE TABLE dbo.WF_DocTipoReglaExtract
    (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WF_DocTipoReglaExtract PRIMARY KEY,
        DocTipoId       INT NOT NULL,
        Campo           NVARCHAR(100) NOT NULL,
        Regex           NVARCHAR(1000) NOT NULL,
        Grupo           INT NOT NULL CONSTRAINT DF_WF_DocTipoReglaExtract_Grupo DEFAULT(1),
        Orden           INT NOT NULL CONSTRAINT DF_WF_DocTipoReglaExtract_Orden DEFAULT(1),
        Activo          BIT NOT NULL CONSTRAINT DF_WF_DocTipoReglaExtract_Activo DEFAULT(1),
        CreatedAt       DATETIME NOT NULL CONSTRAINT DF_WF_DocTipoReglaExtract_CreatedAt DEFAULT(GETDATE()),
        UpdatedAt       DATETIME NULL
    );

    CREATE INDEX IX_WF_DocTipoReglaExtract_DocTipoId_Activo_Orden
        ON dbo.WF_DocTipoReglaExtract(DocTipoId, Activo, Orden);
END
GO
/*

ALTER TABLE dbo.WF_DocTipoReglaExtract
ADD TipoDato   NVARCHAR(30)  NULL,
    Ejemplo    NVARCHAR(500) NULL,
    HintLabel  NVARCHAR(100) NULL,
    HintContext NVARCHAR(500) NULL,
    Modo       NVARCHAR(20)  NULL;


DECLARE @DocTipoId INT = 2;

;WITH Src AS
(
    SELECT *
    FROM (VALUES
        ( 10, N'Empresa',             N'EMPRESA\s+(.+?)(?=\s*ORDEN\s*DE\s*COMPRA)', 1 ),
        ( 20, N'OrdenCompraNumero',   N'ORDEN\s+DE\s+COMPRA\s+N[º°]\s*([A-Z0-9-]+)', 1 ),
        ( 30, N'Fecha',               N'Fecha:\s*(\d{2}/\d{2}/\d{4})(?=\s*Proveedor:|$)', 1 ),
        ( 40, N'ProveedorCodigo',     N'COD:\s*([A-Z0-9-]+?)(?=\s*Raz|\s*Raz[oó]n|\s*CUIT:|\s*Detalle\s+de\s+la\s+compra:|$)', 1 ),
        ( 50, N'ProveedorRazonSocial',N'Raz[oó]n\s+Social:\s*(.+?)(?=\s*CUIT:|\s*Detalle\s+de\s+la\s+compra:|$)', 1 ),
        ( 60, N'ProveedorCUIT',       N'CUIT:\s*(\d{2}-\d{8}-\d)(?=\s*Detalle\s+de\s+la\s+compra:|$)', 1 ),
        ( 70, N'Item',                N'Item:\s*(.+?)(?=\s*Cantidad:|\s*Precio\s*Unitario:|\s*Total:|\s*Condiciones:|$)', 1 ),
        ( 80, N'Cantidad',            N'Cantidad:\s*(\d+)\s*unidades(?=\s*Precio\s*Unitario:|\s*Total:|\s*Condiciones:|$)', 1 ),
        ( 90, N'PrecioUnitario',      N'Precio\s*Unitario:\s*([0-9\.]+,[0-9]{2})(?=\s*Total:|\s*Condiciones:|$)', 1 ),
        (100, N'Total',               N'Total:\s*([0-9\.]+,[0-9]{2})(?=\s*Condiciones:|\s*Entrega\s+en|\s*Pago\s+|\s*Autorizado\s+por:|$)', 1 ),
        (110, N'Entrega',             N'Entrega\s+en\s*([^\r\n]+?)(?:\.|\r?\n|\s*Pago\s+|\s*Autorizado\s+por:|$)', 1 ),
        (120, N'Pago',                N'Pago\s+([^\r\n]+?)(?:\.|\r?\n|\s*Autorizado\s+por:|$)', 1 ),
        (130, N'AutorizadoPor',       N'Autorizado\s+por:\s*(.+)$', 1 )
    ) AS V(Orden, Campo, Regex, Grupo)
)
MERGE dbo.WF_DocTipoReglaExtract AS T
USING Src AS S
ON  T.DocTipoId = @DocTipoId
AND T.Campo     = S.Campo
WHEN MATCHED THEN
    UPDATE SET
        T.Regex     = S.Regex,
        T.Grupo     = S.Grupo,
        T.Orden     = S.Orden,
        T.Activo    = 1,
        T.UpdatedAt = GETDATE()
WHEN NOT MATCHED THEN
    INSERT (DocTipoId, Campo, Regex, Grupo, Orden, Activo)
    VALUES (@DocTipoId, S.Campo, S.Regex, S.Grupo, S.Orden, 1);
GO


ALTER TABLE dbo.WF_DocTipo
ADD RulesJson NVARCHAR(MAX) NULL;

                  SELECT *                  FROM dbo.WF_DocTipo                  WHERE Codigo = 'ORDEN_COMPRA' AND EsActivo = 1
				  SELECT RulesJson          FROM dbo.WF_DocTipo                  WHERE Codigo = 'ORDEN_COMPRA' AND EsActivo = 1
select * from WF_DocTipoReglaExtract



*/
/*
/* ============================================================
   SEED (Idempotente) – WF_DocTipo
   - Upsert por Codigo
   ============================================================ */

;WITH src AS
(
    SELECT
        v.Codigo,
        v.Nombre,
        v.ContextPrefix,
        v.PlantillaPath,
        v.RutaBase,
        v.EsActivo
    FROM (VALUES
        ('NOTA_PEDIDO',  N'Nota de Pedido de Compras', N'np',   NULL, NULL, CAST(1 AS bit)),
        ('ORDEN_COMPRA', N'Orden de Compra',          N'oc',   NULL, NULL, CAST(1 AS bit)),
        ('FACTURA_VENTA',N'Factura de Venta',         N'fact', NULL, NULL, CAST(1 AS bit))
    ) v(Codigo, Nombre, ContextPrefix, PlantillaPath, RutaBase, EsActivo)
)
MERGE dbo.WF_DocTipo AS tgt
USING src
ON tgt.Codigo = src.Codigo
WHEN MATCHED THEN
    UPDATE SET
        tgt.Nombre        = src.Nombre,
        tgt.ContextPrefix = src.ContextPrefix,
        tgt.PlantillaPath = src.PlantillaPath,
        tgt.RutaBase      = src.RutaBase,
        tgt.EsActivo      = src.EsActivo,
        tgt.UpdatedAt     = SYSUTCDATETIME()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Codigo, Nombre, ContextPrefix, PlantillaPath, RutaBase, EsActivo)
    VALUES (src.Codigo, src.Nombre, src.ContextPrefix, src.PlantillaPath, src.RutaBase, src.EsActivo)
;
GO

*/
/*
INSERT INTO WF_DocumentoPlantilla
(DocumentoTipoId, RegexPattern, CamposJson)
VALUES
(
   (SELECT Id FROM WF_DocumentoTipo WHERE Codigo='NPC'),
   N'Nota de Pedido de Compras N°:\s*(?<numero>NPC-\d{4}-\d+).*?Solicitante:\s*(?<solicitante>.+?)\s*\(.*?Sector:\s*(?<sector>.+?)\s*Item solicitado:\s*Código:\s*(?<codigo>[\w-]+).*?Cantidad:\s*(?<cantidad>\d+).*?Monto Estimado:\s*(?<monto>[0-9\.,]+)',
   N'[
        {"name":"numero", "group":"numero"},
        {"name":"solicitante","group":"solicitante"},
        {"name":"sector","group":"sector"},
        {"name":"codigo","group":"codigo"},
        {"name":"cantidad","group":"cantidad", "type":"int"},
        {"name":"monto","group":"monto", "type":"decimal"}
    ]'
);
*/


SELECT * FROM WF_DocumentoTipo WHERE Codigo='NPC'

SELECT * FROM WF_DocumentoPlantilla

SELECT * FROM WF_Definicion
SELECT * FROM WF_instancia
SELECT * FROM WF_instanciaLog
SELECT * FROM WF_DocTipoReglaExtract

  SELECT Id, ISNULL(Activo, 0) AS Activo, ISNULL(JsonDef, '') AS JsonDef  FROM dbo.WF_Definicion WITH (READPAST);

/*
delete from WF_instanciaLog where WF_InstanciaId >= 135
delete from WF_instancia where id >= 133
delete from WF_Definicion where id = 58
delete from WF_DocTipoReglaExtract where id = 22
Update WF_DocTipoReglaExtract set Orden = 30 where id = 25
*/
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":328,"y":24}}},"n5":{"Id":"n5","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":"C:\\temp\\Orden de Compra.docx","mode":"word","position":{"x":240,"y":184}}},"n6":{"Id":"n6","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"input.text","rulesJson":"[\n  { \"campo\": \"Empresa\", \"regex\": \"EMPRESA\\\\s+(.+?)(?=\\\\s*ORDEN\\\\s*DE\\\\s*COMPRA)\", \"grupo\": 1 },\n\n  { \"campo\": \"OrdenCompraNumero\", \"regex\": \"ORDEN\\\\s+DE\\\\s+COMPRA\\\\s+N[º°]\\\\s*([A-Z0-9-]+)\", \"grupo\": 1 },\n\n  { \"campo\": \"Fecha\", \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})(?=\\\\s*Proveedor:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCodigo\", \"regex\": \"COD:\\\\s*([A-Z0-9-]+?)(?=\\\\s*Raz|\\\\s*Raz[oó]n|\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorRazonSocial\", \"regex\": \"Raz[oó]n\\\\s+Social:\\\\s*(.+?)(?=\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCUIT\", \"regex\": \"CUIT:\\\\s*(\\\\d{2}-\\\\d{8}-\\\\d)(?=\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Item\", \"regex\": \"Item:\\\\s*(.+?)(?=\\\\s*Cantidad:|\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Cantidad\", \"regex\": \"Cantidad:\\\\s*(\\\\d+)\\\\s*unidades(?=\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"PrecioUnitario\", \"regex\": \"Precio\\\\s*Unitario:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Total\", \"regex\": \"Total:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Condiciones:|\\\\s*Entrega\\\\s+en|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Entrega\", \"regex\": \"Entrega\\\\s+en\\\\s*([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n{ \"campo\": \"Pago\", \"regex\": \"Pago\\\\s+([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"AutorizadoPor\", \"regex\": \"Autorizado\\\\s+por:\\\\s*(.+)$\", \"grupo\": 1 }\n]\n","position":{"x":504,"y":488}}},"n7":{"Id":"n7","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":504,"y":640}}},"n22":{"Id":"n22","Type":"util.docTipo.resolve","Label":"Documento: Resolver tipo","Parameters":{"docTipoCodigo":"ORDEN_COMPRA","position":{"x":472,"y":320}}}},"Edges":[{"Id":"e11","From":"n6","To":"n7","Condition":"always"},{"Id":"e13","From":"n6","To":"n7","Condition":"always"},{"Id":"e23","From":"n1","To":"n5","Condition":"always"},{"Id":"e24","From":"n5","To":"n22","Condition":"always"},{"Id":"e25","From":"n22","To":"n6","Condition":"always"},{"Id":"e26","From":"n5","To":"n22","Condition":"always"}],"Meta":{"Name":"Prueba DocTipo"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":480,"y":48}}},"n5":{"Id":"n5","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":"C:\\temp\\Orden de Compra.docx","mode":"word","position":{"x":480,"y":336}}},"n6":{"Id":"n6","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"input.text","rulesJson":"[\n  { \"campo\": \"Empresa\", \"regex\": \"EMPRESA\\\\s+(.+?)(?=\\\\s*ORDEN\\\\s*DE\\\\s*COMPRA)\", \"grupo\": 1 },\n\n  { \"campo\": \"OrdenCompraNumero\", \"regex\": \"ORDEN\\\\s+DE\\\\s+COMPRA\\\\s+N[º°]\\\\s*([A-Z0-9-]+)\", \"grupo\": 1 },\n\n  { \"campo\": \"Fecha\", \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})(?=\\\\s*Proveedor:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCodigo\", \"regex\": \"COD:\\\\s*([A-Z0-9-]+?)(?=\\\\s*Raz|\\\\s*Raz[oó]n|\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorRazonSocial\", \"regex\": \"Raz[oó]n\\\\s+Social:\\\\s*(.+?)(?=\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCUIT\", \"regex\": \"CUIT:\\\\s*(\\\\d{2}-\\\\d{8}-\\\\d)(?=\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Item\", \"regex\": \"Item:\\\\s*(.+?)(?=\\\\s*Cantidad:|\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Cantidad\", \"regex\": \"Cantidad:\\\\s*(\\\\d+)\\\\s*unidades(?=\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"PrecioUnitario\", \"regex\": \"Precio\\\\s*Unitario:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Total\", \"regex\": \"Total:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Condiciones:|\\\\s*Entrega\\\\s+en|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Entrega\", \"regex\": \"Entrega\\\\s+en\\\\s*([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n{ \"campo\": \"Pago\", \"regex\": \"Pago\\\\s+([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"AutorizadoPor\", \"regex\": \"Autorizado\\\\s+por:\\\\s*(.+)$\", \"grupo\": 1 }\n]\n","position":{"x":504,"y":488}}},"n7":{"Id":"n7","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":504,"y":640}}},"n18":{"Id":"n18","Type":"util.docTipo.resolve","Label":"Documento: Resolver tipo","Parameters":{"docTipoCodigo":"ORDEN_COMPRA","position":{"x":456,"y":176}}}},"Edges":[{"Id":"e10","From":"n5","To":"n6","Condition":"always"},{"Id":"e11","From":"n6","To":"n7","Condition":"always"},{"Id":"e13","From":"n6","To":"n7","Condition":"always"},{"Id":"e20","From":"n1","To":"n18","Condition":"always"},{"Id":"e21","From":"n18","To":"n5","Condition":"always"}],"Meta":{"Name":"Prueba DocTipo"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":480,"y":48}}},"n5":{"Id":"n5","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":"C:\\temp\\Orden de Compra.docx","mode":"word","position":{"x":480,"y":336}}},"n6":{"Id":"n6","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"input.text","rulesJson":"[\n  { \"campo\": \"Empresa\", \"regex\": \"EMPRESA\\\\s+(.+?)(?=\\\\s*ORDEN\\\\s*DE\\\\s*COMPRA)\", \"grupo\": 1 },\n\n  { \"campo\": \"OrdenCompraNumero\", \"regex\": \"ORDEN\\\\s+DE\\\\s+COMPRA\\\\s+N[º°]\\\\s*([A-Z0-9-]+)\", \"grupo\": 1 },\n\n  { \"campo\": \"Fecha\", \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})(?=\\\\s*Proveedor:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCodigo\", \"regex\": \"COD:\\\\s*([A-Z0-9-]+?)(?=\\\\s*Raz|\\\\s*Raz[oó]n|\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorRazonSocial\", \"regex\": \"Raz[oó]n\\\\s+Social:\\\\s*(.+?)(?=\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCUIT\", \"regex\": \"CUIT:\\\\s*(\\\\d{2}-\\\\d{8}-\\\\d)(?=\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Item\", \"regex\": \"Item:\\\\s*(.+?)(?=\\\\s*Cantidad:|\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Cantidad\", \"regex\": \"Cantidad:\\\\s*(\\\\d+)\\\\s*unidades(?=\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"PrecioUnitario\", \"regex\": \"Precio\\\\s*Unitario:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Total\", \"regex\": \"Total:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Condiciones:|\\\\s*Entrega\\\\s+en|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Entrega\", \"regex\": \"Entrega\\\\s+en\\\\s*([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n{ \"campo\": \"Pago\", \"regex\": \"Pago\\\\s+([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"AutorizadoPor\", \"regex\": \"Autorizado\\\\s+por:\\\\s*(.+)$\", \"grupo\": 1 }\n]\n","position":{"x":504,"y":488}}},"n7":{"Id":"n7","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":504,"y":640}}},"n15":{"Id":"n15","Type":"util.docTipo.resolve","Label":"Documento: Resolver tipo","Parameters":{"docTipoCodigo":"ORDEN_COMPRA","position":{"x":472,"y":184}}}},"Edges":[{"Id":"e10","From":"n5","To":"n6","Condition":"always"},{"Id":"e11","From":"n6","To":"n7","Condition":"always"},{"Id":"e13","From":"n6","To":"n7","Condition":"always"},{"Id":"e16","From":"n1","To":"n15","Condition":"always"},{"Id":"e17","From":"n15","To":"n5","Condition":"always"}],"Meta":{"Name":"Prueba DocTipo"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":480,"y":48}}},"n5":{"Id":"n5","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":"C:\\temp\\Orden de Compra.docx","mode":"word","position":{"x":480,"y":336}}},"n6":{"Id":"n6","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"input.text","rulesJson":"[\n  { \"campo\": \"Empresa\", \"regex\": \"EMPRESA\\\\s+(.+?)(?=\\\\s*ORDEN\\\\s*DE\\\\s*COMPRA)\", \"grupo\": 1 },\n\n  { \"campo\": \"OrdenCompraNumero\", \"regex\": \"ORDEN\\\\s+DE\\\\s+COMPRA\\\\s+N[º°]\\\\s*([A-Z0-9-]+)\", \"grupo\": 1 },\n\n  { \"campo\": \"Fecha\", \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})(?=\\\\s*Proveedor:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCodigo\", \"regex\": \"COD:\\\\s*([A-Z0-9-]+?)(?=\\\\s*Raz|\\\\s*Raz[oó]n|\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorRazonSocial\", \"regex\": \"Raz[oó]n\\\\s+Social:\\\\s*(.+?)(?=\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCUIT\", \"regex\": \"CUIT:\\\\s*(\\\\d{2}-\\\\d{8}-\\\\d)(?=\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Item\", \"regex\": \"Item:\\\\s*(.+?)(?=\\\\s*Cantidad:|\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Cantidad\", \"regex\": \"Cantidad:\\\\s*(\\\\d+)\\\\s*unidades(?=\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"PrecioUnitario\", \"regex\": \"Precio\\\\s*Unitario:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Total\", \"regex\": \"Total:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Condiciones:|\\\\s*Entrega\\\\s+en|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Entrega\", \"regex\": \"Entrega\\\\s+en\\\\s*([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n{ \"campo\": \"Pago\", \"regex\": \"Pago\\\\s+([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"AutorizadoPor\", \"regex\": \"Autorizado\\\\s+por:\\\\s*(.+)$\", \"grupo\": 1 }\n]\n","position":{"x":504,"y":488}}},"n7":{"Id":"n7","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":504,"y":640}}},"n15":{"Id":"n15","Type":"util.docTipo.resolve","Label":"Documento: Resolver tipo","Parameters":{"docTipoCodigo":"ORDEN_COMPRA","position":{"x":472,"y":184}}}},"Edges":[{"Id":"e10","From":"n5","To":"n6","Condition":"always"},{"Id":"e11","From":"n6","To":"n7","Condition":"always"},{"Id":"e13","From":"n6","To":"n7","Condition":"always"},{"Id":"e16","From":"n1","To":"n15","Condition":"always"},{"Id":"e17","From":"n15","To":"n5","Condition":"always"}],"Meta":{"Name":"Prueba DocTipo"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":480,"y":48}}},"n5":{"Id":"n5","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":"C:\\temp\\Orden de Compra.docx","mode":"word","position":{"x":480,"y":336}}},"n6":{"Id":"n6","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"input.text","rulesJson":"[\n  { \"campo\": \"Empresa\", \"regex\": \"EMPRESA\\\\s+(.+?)(?=\\\\s*ORDEN\\\\s*DE\\\\s*COMPRA)\", \"grupo\": 1 },\n\n  { \"campo\": \"OrdenCompraNumero\", \"regex\": \"ORDEN\\\\s+DE\\\\s+COMPRA\\\\s+N[º°]\\\\s*([A-Z0-9-]+)\", \"grupo\": 1 },\n\n  { \"campo\": \"Fecha\", \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})(?=\\\\s*Proveedor:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCodigo\", \"regex\": \"COD:\\\\s*([A-Z0-9-]+?)(?=\\\\s*Raz|\\\\s*Raz[oó]n|\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorRazonSocial\", \"regex\": \"Raz[oó]n\\\\s+Social:\\\\s*(.+?)(?=\\\\s*CUIT:|\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"ProveedorCUIT\", \"regex\": \"CUIT:\\\\s*(\\\\d{2}-\\\\d{8}-\\\\d)(?=\\\\s*Detalle\\\\s+de\\\\s+la\\\\s+compra:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Item\", \"regex\": \"Item:\\\\s*(.+?)(?=\\\\s*Cantidad:|\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Cantidad\", \"regex\": \"Cantidad:\\\\s*(\\\\d+)\\\\s*unidades(?=\\\\s*Precio\\\\s*Unitario:|\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"PrecioUnitario\", \"regex\": \"Precio\\\\s*Unitario:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Total:|\\\\s*Condiciones:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Total\", \"regex\": \"Total:\\\\s*([0-9\\\\.]+,[0-9]{2})(?=\\\\s*Condiciones:|\\\\s*Entrega\\\\s+en|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"Entrega\", \"regex\": \"Entrega\\\\s+en\\\\s*([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Pago\\\\s+|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n{ \"campo\": \"Pago\", \"regex\": \"Pago\\\\s+([^\\\\r\\\\n]+?)(?:\\\\.|\\\\r?\\\\n|\\\\s*Autorizado\\\\s+por:|$)\", \"grupo\": 1 },\n\n  { \"campo\": \"AutorizadoPor\", \"regex\": \"Autorizado\\\\s+por:\\\\s*(.+)$\", \"grupo\": 1 }\n]\n","position":{"x":504,"y":488}}},"n7":{"Id":"n7","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":504,"y":640}}},"n15":{"Id":"n15","Type":"util.docTipo.resolve","Label":"Documento: Resolver tipo","Parameters":{"docTipoCodigo":"NOTA_PEDIDO","position":{"x":472,"y":184}}}},"Edges":[{"Id":"e10","From":"n5","To":"n6","Condition":"always"},{"Id":"e11","From":"n6","To":"n7","Condition":"always"},{"Id":"e13","From":"n6","To":"n7","Condition":"always"},{"Id":"e16","From":"n1","To":"n15","Condition":"always"},{"Id":"e17","From":"n15","To":"n5","Condition":"always"}],"Meta":{"Name":"Prueba DocTipo"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":648,"y":8}}},"n2":{"Id":"n2","Type":"util.notify","Label":"Solicitud de compra","Parameters":{"titulo":"Solicitud de compra","mensaje":"Usuario=${input.usuarioId}, Tipo=${input.tipo}, Item=${input.codigoItem}, Importe=${input.monto}","canal":"log","nivel":"info","position":{"x":24,"y":112}}},"n3":{"Id":"n3","Type":"control.if","Label":"¿Material o servicio?","Parameters":{"expression":"${input.tipo} == \"MATERIAL\"","position":{"x":24,"y":208}}},"n4":{"Id":"n4","Type":"http.request","Label":"Comprobar inventario ERP","Parameters":{"method":"GET","url":"https://erp.interno/api/inventario?item=${input.codigoItem}","headers":{},"body":"","destino":"erp.stock","position":{"x":16,"y":312}}},"n5":{"Id":"n5","Type":"control.if","Label":"¿Hay stock?","Parameters":{"expression":"${erp.stock.cantidadDisponible} > 0","position":{"x":32,"y":408}}},"n6":{"Id":"n6","Type":"util.notify","Label":"Atender y actualizar stock","Parameters":{"titulo":"Atender solicitud","mensaje":"Reserva stock para item=${input.codigoItem}, cant=${input.cantidad}","canal":"log","nivel":"info","position":{"x":24,"y":512}}},"n7":{"Id":"n7","Type":"control.delay","Label":"Esperar actualización","Parameters":{"seconds":300,"position":{"x":24,"y":616}}},"n8":{"Id":"n8","Type":"util.notify","Label":"Comprobar presupuestos anteriores","Parameters":{"titulo":"Comprobar presupuestos anteriores","mensaje":"Revisar presupuestos previos para item=${input.codigoItem}","canal":"log","nivel":"info","position":{"x":480,"y":216}}},"n9":{"Id":"n9","Type":"util.notify","Label":"Hacer al menos 3 presupuestos","Parameters":{"titulo":"Hacer al menos 3 presupuestos","mensaje":"Item=${input.codigoItem}, se requieren al menos 3 presupuestos.","canal":"log","nivel":"info","position":{"x":464,"y":384}}},"n10":{"Id":"n10","Type":"control.if","Label":"¿Compra autorizada?","Parameters":{"expression":"${input.autorizada} == true","position":{"x":920,"y":368}}},"n11":{"Id":"n11","Type":"control.if","Label":"¿Monto > 1000?","Parameters":{"expression":"${input.monto} > 1000","position":{"x":920,"y":496}}},"n12":{"Id":"n12","Type":"queue.publish","Label":"Autorización de compra","Parameters":{"queue":"AUTORIZACION_COMPRA","broker":"sql","correlationId":"${wf.instanceId}","priority":10,"payload":{"usuarioId":"${input.usuarioId}","monto":"${input.monto}","codigoItem":"${input.codigoItem}","descripcion":"${input.descripcion}"},"position":{"x":632,"y":616}}},"n13":{"Id":"n13","Type":"util.notify","Label":"Contrato de compra","Parameters":{"titulo":"Contrato de compra","mensaje":"Preparar contrato para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":920,"y":744}}},"n14":{"Id":"n14","Type":"util.error","Label":"Error / No autorizada","Parameters":{"mensaje":"Compra NO autorizada para usuario ${input.usuarioId}, monto=${input.monto}","capturar":true,"notificar":true,"volverAIntentar":false,"position":{"x":456,"y":520}}},"n17":{"Id":"n17","Type":"util.notify","Label":"Proceso de compra","Parameters":{"titulo":"Proceso de compra","mensaje":"Iniciar proceso de compra para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":440,"y":736}}},"n15":{"Id":"n15","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":32,"y":736}}},"n27":{"Id":"n27","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"doc.archivo","rulesJson":"[\n  { \"campo\": \"Proveedor\", \"regex\": \"Proveedor:\\\\s*(.+)\", \"grupo\": 1 },\n  { \"campo\": \"Fecha\",     \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})\", \"grupo\": 1 }\n]","position":{"x":40,"y":8}}},"n28":{"Id":"n28","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":" C:\\temp\\Nota de Pedido de Compras.docx","mode":"word","position":{"x":320,"y":24}}}},"Edges":[{"Id":"efix1","From":"n2","To":"n3","Condition":"always"},{"Id":"efix2","From":"n3","To":"n4","Condition":"true"},{"Id":"efix3","From":"n3","To":"n8","Condition":"false"},{"Id":"efix4","From":"n4","To":"n5","Condition":"always"},{"Id":"efix5","From":"n5","To":"n6","Condition":"true"},{"Id":"efix6","From":"n5","To":"n9","Condition":"false"},{"Id":"efix7","From":"n6","To":"n7","Condition":"always"},{"Id":"efix8","From":"n7","To":"n15","Condition":"always"},{"Id":"efix9","From":"n8","To":"n10","Condition":"always"},{"Id":"efix10","From":"n9","To":"n10","Condition":"always"},{"Id":"efix11","From":"n10","To":"n11","Condition":"true"},{"Id":"efix12","From":"n10","To":"n14","Condition":"false"},{"Id":"efix13","From":"n11","To":"n12","Condition":"true"},{"Id":"efix14","From":"n11","To":"n13","Condition":"false"},{"Id":"efix15","From":"n12","To":"n13","Condition":"always"},{"Id":"efix16","From":"n13","To":"n17","Condition":"always"},{"Id":"efix17","From":"n17","To":"n15","Condition":"always"},{"Id":"efix18","From":"n14","To":"n15","Condition":"always"},{"Id":"efix21","From":"n27","To":"n2","Condition":"always"},{"Id":"e29","From":"n1","To":"n28","Condition":"always"},{"Id":"e30","From":"n28","To":"n27","Condition":"always"}],"Meta":{"Name":"Solicitud de Compras"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":648,"y":8}}},"n2":{"Id":"n2","Type":"util.notify","Label":"Solicitud de compra","Parameters":{"titulo":"Solicitud de compra","mensaje":"Usuario=${input.usuarioId}, Tipo=${input.tipo}, Item=${input.codigoItem}, Importe=${input.monto}","canal":"log","nivel":"info","position":{"x":24,"y":112}}},"n3":{"Id":"n3","Type":"control.if","Label":"¿Material o servicio?","Parameters":{"expression":"${input.tipo} == \"MATERIAL\"","position":{"x":24,"y":208}}},"n4":{"Id":"n4","Type":"http.request","Label":"Comprobar inventario ERP","Parameters":{"method":"GET","url":"https://erp.interno/api/inventario?item=${input.codigoItem}","headers":{},"body":"","destino":"erp.stock","position":{"x":16,"y":312}}},"n5":{"Id":"n5","Type":"control.if","Label":"¿Hay stock?","Parameters":{"expression":"${erp.stock.cantidadDisponible} > 0","position":{"x":32,"y":408}}},"n6":{"Id":"n6","Type":"util.notify","Label":"Atender y actualizar stock","Parameters":{"titulo":"Atender solicitud","mensaje":"Reserva stock para item=${input.codigoItem}, cant=${input.cantidad}","canal":"log","nivel":"info","position":{"x":24,"y":512}}},"n7":{"Id":"n7","Type":"control.delay","Label":"Esperar actualización","Parameters":{"seconds":300,"position":{"x":24,"y":616}}},"n8":{"Id":"n8","Type":"util.notify","Label":"Comprobar presupuestos anteriores","Parameters":{"titulo":"Comprobar presupuestos anteriores","mensaje":"Revisar presupuestos previos para item=${input.codigoItem}","canal":"log","nivel":"info","position":{"x":480,"y":216}}},"n9":{"Id":"n9","Type":"util.notify","Label":"Hacer al menos 3 presupuestos","Parameters":{"titulo":"Hacer al menos 3 presupuestos","mensaje":"Item=${input.codigoItem}, se requieren al menos 3 presupuestos.","canal":"log","nivel":"info","position":{"x":464,"y":384}}},"n10":{"Id":"n10","Type":"control.if","Label":"¿Compra autorizada?","Parameters":{"expression":"${input.autorizada} == true","position":{"x":920,"y":368}}},"n11":{"Id":"n11","Type":"control.if","Label":"¿Monto > 1000?","Parameters":{"expression":"${input.monto} > 1000","position":{"x":920,"y":496}}},"n12":{"Id":"n12","Type":"queue.publish","Label":"Autorización de compra","Parameters":{"queue":"AUTORIZACION_COMPRA","broker":"sql","correlationId":"${wf.instanceId}","priority":10,"payload":{"usuarioId":"${input.usuarioId}","monto":"${input.monto}","codigoItem":"${input.codigoItem}","descripcion":"${input.descripcion}"},"position":{"x":632,"y":616}}},"n13":{"Id":"n13","Type":"util.notify","Label":"Contrato de compra","Parameters":{"titulo":"Contrato de compra","mensaje":"Preparar contrato para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":920,"y":744}}},"n14":{"Id":"n14","Type":"util.error","Label":"Error / No autorizada","Parameters":{"mensaje":"Compra NO autorizada para usuario ${input.usuarioId}, monto=${input.monto}","capturar":true,"notificar":true,"volverAIntentar":false,"position":{"x":456,"y":520}}},"n17":{"Id":"n17","Type":"util.notify","Label":"Proceso de compra","Parameters":{"titulo":"Proceso de compra","mensaje":"Iniciar proceso de compra para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":440,"y":736}}},"n15":{"Id":"n15","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":32,"y":736}}},"n26":{"Id":"n26","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":" C:\\temp\\Nota de Pedido de Compras.docx","mode":"auto","position":{"x":328,"y":8}}},"n27":{"Id":"n27","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"doc.archivo","rulesJson":"[\n  { \"campo\": \"Proveedor\", \"regex\": \"Proveedor:\\\\s*(.+)\", \"grupo\": 1 },\n  { \"campo\": \"Fecha\",     \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})\", \"grupo\": 1 }\n]","position":{"x":40,"y":8}}}},"Edges":[{"Id":"efix1","From":"n2","To":"n3","Condition":"always"},{"Id":"efix2","From":"n3","To":"n4","Condition":"true"},{"Id":"efix3","From":"n3","To":"n8","Condition":"false"},{"Id":"efix4","From":"n4","To":"n5","Condition":"always"},{"Id":"efix5","From":"n5","To":"n6","Condition":"true"},{"Id":"efix6","From":"n5","To":"n9","Condition":"false"},{"Id":"efix7","From":"n6","To":"n7","Condition":"always"},{"Id":"efix8","From":"n7","To":"n15","Condition":"always"},{"Id":"efix9","From":"n8","To":"n10","Condition":"always"},{"Id":"efix10","From":"n9","To":"n10","Condition":"always"},{"Id":"efix11","From":"n10","To":"n11","Condition":"true"},{"Id":"efix12","From":"n10","To":"n14","Condition":"false"},{"Id":"efix13","From":"n11","To":"n12","Condition":"true"},{"Id":"efix14","From":"n11","To":"n13","Condition":"false"},{"Id":"efix15","From":"n12","To":"n13","Condition":"always"},{"Id":"efix16","From":"n13","To":"n17","Condition":"always"},{"Id":"efix17","From":"n17","To":"n15","Condition":"always"},{"Id":"efix18","From":"n14","To":"n15","Condition":"always"},{"Id":"efix19","From":"n1","To":"n26","Condition":"always"},{"Id":"efix20","From":"n26","To":"n27","Condition":"always"},{"Id":"efix21","From":"n27","To":"n2","Condition":"always"}],"Meta":{"Name":"Solicitud de Compras"}}
{"StartNodeId":"n1","Nodes":{"n1":{"Id":"n1","Type":"util.start","Label":"Inicio","Parameters":{"position":{"x":720,"y":8}}},"n2":{"Id":"n2","Type":"util.notify","Label":"Solicitud de compra","Parameters":{"titulo":"Solicitud de compra","mensaje":"Usuario=${input.usuarioId}, Tipo=${input.tipo}, Item=${input.codigoItem}, Importe=${input.monto}","canal":"log","nivel":"info","position":{"x":24,"y":112}}},"n3":{"Id":"n3","Type":"control.if","Label":"¿Material o servicio?","Parameters":{"expression":"${input.tipo} == \"MATERIAL\"","position":{"x":24,"y":208}}},"n4":{"Id":"n4","Type":"http.request","Label":"Comprobar inventario ERP","Parameters":{"method":"GET","url":"https://erp.interno/api/inventario?item=${input.codigoItem}","headers":{},"body":"","destino":"erp.stock","position":{"x":16,"y":312}}},"n5":{"Id":"n5","Type":"control.if","Label":"¿Hay stock?","Parameters":{"expression":"${erp.stock.cantidadDisponible} > 0","position":{"x":32,"y":408}}},"n6":{"Id":"n6","Type":"util.notify","Label":"Atender y actualizar stock","Parameters":{"titulo":"Atender solicitud","mensaje":"Reserva stock para item=${input.codigoItem}, cant=${input.cantidad}","canal":"log","nivel":"info","position":{"x":24,"y":512}}},"n7":{"Id":"n7","Type":"control.delay","Label":"Esperar actualización","Parameters":{"seconds":300,"position":{"x":24,"y":616}}},"n8":{"Id":"n8","Type":"util.notify","Label":"Comprobar presupuestos anteriores","Parameters":{"titulo":"Comprobar presupuestos anteriores","mensaje":"Revisar presupuestos previos para item=${input.codigoItem}","canal":"log","nivel":"info","position":{"x":480,"y":216}}},"n9":{"Id":"n9","Type":"util.notify","Label":"Hacer al menos 3 presupuestos","Parameters":{"titulo":"Hacer al menos 3 presupuestos","mensaje":"Item=${input.codigoItem}, se requieren al menos 3 presupuestos.","canal":"log","nivel":"info","position":{"x":464,"y":384}}},"n10":{"Id":"n10","Type":"control.if","Label":"¿Compra autorizada?","Parameters":{"expression":"${input.autorizada} == true","position":{"x":920,"y":368}}},"n11":{"Id":"n11","Type":"control.if","Label":"¿Monto > 1000?","Parameters":{"expression":"${input.monto} > 1000","position":{"x":920,"y":496}}},"n12":{"Id":"n12","Type":"queue.publish","Label":"Autorización de compra","Parameters":{"queue":"AUTORIZACION_COMPRA","broker":"sql","correlationId":"${wf.instanceId}","priority":10,"payload":{"usuarioId":"${input.usuarioId}","monto":"${input.monto}","codigoItem":"${input.codigoItem}","descripcion":"${input.descripcion}"},"position":{"x":632,"y":616}}},"n13":{"Id":"n13","Type":"util.notify","Label":"Contrato de compra","Parameters":{"titulo":"Contrato de compra","mensaje":"Preparar contrato para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":920,"y":744}}},"n14":{"Id":"n14","Type":"util.error","Label":"Error / No autorizada","Parameters":{"mensaje":"Compra NO autorizada para usuario ${input.usuarioId}, monto=${input.monto}","capturar":true,"notificar":true,"volverAIntentar":false,"position":{"x":456,"y":520}}},"n17":{"Id":"n17","Type":"util.notify","Label":"Proceso de compra","Parameters":{"titulo":"Proceso de compra","mensaje":"Iniciar proceso de compra para item=${input.codigoItem}, monto=${input.monto}","canal":"log","nivel":"info","position":{"x":440,"y":736}}},"n15":{"Id":"n15","Type":"util.end","Label":"Fin","Parameters":{"position":{"x":32,"y":736}}},"n22":{"Id":"n22","Type":"doc.load","Label":"Documento: Cargar archivo","Parameters":{"path":" C:\\temp\\Nota de Pedido de Compras.docx","mode":"word","salidaPrefix":"doc.","position":{"x":360,"y":8}}},"n23":{"Id":"n23","Type":"doc.extract","Label":"Extraer de texto","Parameters":{"origen":"doc.archivo","rulesJson":"[\n  { \"campo\": \"Proveedor\", \"regex\": \"Proveedor:\\\\s*(.+)\", \"grupo\": 1 },\n  { \"campo\": \"Fecha\",     \"regex\": \"Fecha:\\\\s*(\\\\d{2}/\\\\d{2}/\\\\d{4})\", \"grupo\": 1 }\n]","position":{"x":40,"y":8}}}},"Edges":[{"From":"n2","To":"n3","Condition":"always"},{"From":"n3","To":"n4","Condition":"true"},{"From":"n3","To":"n8","Condition":"false"},{"From":"n4","To":"n5","Condition":"always"},{"From":"n5","To":"n6","Condition":"true"},{"From":"n5","To":"n9","Condition":"false"},{"From":"n6","To":"n7","Condition":"always"},{"From":"n7","To":"n15","Condition":"always"},{"From":"n8","To":"n10","Condition":"always"},{"From":"n9","To":"n10","Condition":"always"},{"From":"n10","To":"n11","Condition":"true"},{"From":"n10","To":"n14","Condition":"false"},{"From":"n11","To":"n12","Condition":"true"},{"From":"n11","To":"n13","Condition":"false"},{"From":"n12","To":"n13","Condition":"always"},{"From":"n13","To":"n17","Condition":"always"},{"From":"n17","To":"n15","Condition":"always"},{"From":"n14","To":"n15","Condition":"always"},{"From":"n1","To":"n22","Condition":"always"},{"From":"n22","To":"n23","Condition":"always"},{"From":"n23","To":"n2","Condition":"always"}],"Meta":{"Name":"Solicitud de Compras"}}






