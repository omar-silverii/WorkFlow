DECLARE @Busqueda NVARCHAR(100) = 'RolKey';

SELECT 
    obj.name AS [Nombre_SP],
    mod.definition AS [Codigo_Fuente]
FROM 
    sys.sql_modules AS mod
INNER JOIN 
    sys.objects AS obj ON mod.object_id = obj.object_id
WHERE 
    obj.type = 'P'
    AND (
        -- 1. La palabra está rodeada de caracteres no alfanuméricos (espacios, comas, paréntesis, etc.)
        mod.definition LIKE '%[^a-z0-9]' + @Busqueda + '[^a-z0-9]%'
        -- 2. La palabra está justo al inicio del código
        OR mod.definition LIKE @Busqueda + '[^a-z0-9]%'
        -- 3. La palabra está justo al final del código
        OR mod.definition LIKE '%[^a-z0-9]' + @Busqueda
    )
ORDER BY 
    obj.name;