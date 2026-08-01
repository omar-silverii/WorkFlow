/*
  DEMO.CONTRATO.APROBACION
  Tipo documental genérico requerido por doc.load.
  No agrega reglas de extracción ni modifica handlers.
*/
SET NOCOUNT ON;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.WF_DocTipo
    WHERE Codigo = N'CONTRATO_GENERAL'
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
        N'CONTRATO_GENERAL',
        N'Contrato general',
        N'contrato',
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
       SET Nombre = N'Contrato general',
           ContextPrefix = N'contrato',
           MotorExtraccion = N'REGLAS',
           EsActivo = 1,
           UpdatedAt = GETDATE()
     WHERE Codigo = N'CONTRATO_GENERAL';
END;

SELECT DocTipoId, Codigo, Nombre, ContextPrefix, MotorExtraccion, EsActivo
FROM dbo.WF_DocTipo
WHERE Codigo = N'CONTRATO_GENERAL';