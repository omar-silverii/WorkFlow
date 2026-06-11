
ALTER TABLE dbo.WF_DocTipo
DROP CONSTRAINT CK_WF_DocTipo_MotorExtraccion;
GO

ALTER TABLE dbo.WF_DocTipo WITH CHECK
ADD CONSTRAINT CK_WF_DocTipo_MotorExtraccion
CHECK (MotorExtraccion IN ('REGLAS', 'FACTURA_AR', 'NC_AR'));
GO

ALTER TABLE dbo.WF_DocTipo
CHECK CONSTRAINT CK_WF_DocTipo_MotorExtraccion;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.WF_DocTipo
    WHERE Codigo = 'NOTA_CREDITO_ELECTRONICA_AR'
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
        'NOTA_CREDITO_ELECTRONICA_AR',
        'Nota de crédito electrónica AFIP',
        'notaCredito',
        'NC_AR',
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
    SET Nombre = 'Nota de crédito electrónica AFIP',
        ContextPrefix = 'notaCredito',
        MotorExtraccion = 'NC_AR',
        EsActivo = 1,
        UpdatedAt = GETDATE()
    WHERE Codigo = 'NOTA_CREDITO_ELECTRONICA_AR';
END;
