/*
  DEMO.RECLAMO.GESTION
  Tipo documental genérico requerido por doc.load.
  No agrega reglas de extracción ni modifica handlers.
*/
SET NOCOUNT ON;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.WF_DocTipo
    WHERE Codigo = N'RECLAMO_GENERAL'
)
BEGIN
    INSERT INTO dbo.WF_DocTipo
    (
        Codigo,
        Nombre,
        ContextPrefix,
        MotorExtraccion,
        PlantillaPath,
        RutaBase,
        EsActivo,
        CreatedAt,
        UpdatedAt,
        RulesJson
    )
    VALUES
    (
        N'RECLAMO_GENERAL',
        N'Reclamo general',
        N'reclamo',
        N'REGLAS',
        NULL,
        NULL,
        1,
        GETDATE(),
        NULL,
        NULL
    );
END
ELSE
BEGIN
    UPDATE dbo.WF_DocTipo
       SET Nombre = N'Reclamo general',
           ContextPrefix = N'reclamo',
           MotorExtraccion = N'REGLAS',
           EsActivo = 1,
           UpdatedAt = GETDATE()
     WHERE Codigo = N'RECLAMO_GENERAL';
END;

SELECT DocTipoId, Codigo, Nombre, ContextPrefix, MotorExtraccion, EsActivo
FROM dbo.WF_DocTipo
WHERE Codigo = N'RECLAMO_GENERAL';