using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading;

namespace Intranet.WorkflowStudio.Runtime
{
    /// <summary>
    /// Migrador mínimo e idempotente (sin DBA):
    /// - Crea WF_SchemaVersion si no existe.
    /// - Aplica la migración ENTIDAD v2026022401 si no está aplicada.
    /// </summary>
    public static class SchemaMigrator
    {
        private static int _initOnce = 0;

        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public static void Initialize()
        {
            if (Interlocked.Exchange(ref _initOnce, 1) == 1)
                return;

            try
            {
                using (var cn = new SqlConnection(Cnn))
                {
                    cn.Open();

                    // 1) Tabla de versiones
                    Exec(cn, @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_SchemaVersion')
BEGIN
    CREATE TABLE dbo.WF_SchemaVersion (
        VersionId   INT NOT NULL PRIMARY KEY,
        Nombre      NVARCHAR(200) NOT NULL,
        AppliedUtc  DATETIME2(0) NOT NULL DEFAULT (SYSUTCDATETIME())
    );
END");

                    // 2) Migración ENTIDAD core
                    if (!HasVersion(cn, 2026022401))
                    {
                        Exec(cn, @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_Entidad')
BEGIN
    CREATE TABLE dbo.WF_Entidad (
        EntidadId             BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId              NVARCHAR(50) NULL,

        TipoEntidad           NVARCHAR(80) NOT NULL,
        EstadoActual          NVARCHAR(50) NULL,

        InstanciaId           BIGINT NULL,

        DataJson              NVARCHAR(MAX) NULL,
        Total                 DECIMAL(18,2) NULL,

        DocumentoOriginalId   BIGINT NULL,
        DocumentoProcesadoId  BIGINT NULL,
        DocumentoFirmadoId    BIGINT NULL,

        CreadoUtc             DATETIME2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
        ActualizadoUtc        DATETIME2(0) NOT NULL DEFAULT (SYSUTCDATETIME()),
        CreadoPor             NVARCHAR(100) NULL,
        ActualizadoPor        NVARCHAR(100) NULL,

        RowVer                ROWVERSION NOT NULL
    );

    CREATE UNIQUE INDEX UX_WF_Entidad_InstanciaId
        ON dbo.WF_Entidad (InstanciaId)
        WHERE InstanciaId IS NOT NULL;

    CREATE INDEX IX_WF_Entidad_TipoEntidad_Estado
        ON dbo.WF_Entidad (TipoEntidad, EstadoActual);

    CREATE INDEX IX_WF_Entidad_CreadoUtc
        ON dbo.WF_Entidad (CreadoUtc);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_EntidadItem')
BEGIN
    CREATE TABLE dbo.WF_EntidadItem (
        EntidadItemId   BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntidadId       BIGINT NOT NULL,
        ItemIndex       INT NOT NULL,
        Path            NVARCHAR(200) NULL,
        DataJson        NVARCHAR(MAX) NOT NULL,
        Descripcion     NVARCHAR(400) NULL,
        Cantidad        DECIMAL(18,4) NULL,
        Importe         DECIMAL(18,2) NULL
    );

    ALTER TABLE dbo.WF_EntidadItem
      ADD CONSTRAINT FK_WF_EntidadItem_Entidad
      FOREIGN KEY (EntidadId) REFERENCES dbo.WF_Entidad(EntidadId);

    CREATE UNIQUE INDEX UX_WF_EntidadItem_Entidad_ItemIndex
        ON dbo.WF_EntidadItem (EntidadId, ItemIndex);

    CREATE INDEX IX_WF_EntidadItem_Entidad
        ON dbo.WF_EntidadItem (EntidadId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WF_EntidadIndice')
BEGIN
    CREATE TABLE dbo.WF_EntidadIndice (
        EntidadIndiceId  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EntidadId        BIGINT NOT NULL,
        [Key]            NVARCHAR(100) NOT NULL,
        [Value]          NVARCHAR(400) NULL,
        ValueNorm        NVARCHAR(400) NULL,
        TipoDato         NVARCHAR(30) NULL,
        SourcePath       NVARCHAR(200) NULL
    );

    ALTER TABLE dbo.WF_EntidadIndice
      ADD CONSTRAINT FK_WF_EntidadIndice_Entidad
      FOREIGN KEY (EntidadId) REFERENCES dbo.WF_Entidad(EntidadId);

    CREATE INDEX IX_WF_EntidadIndice_Key_ValueNorm
        ON dbo.WF_EntidadIndice ([Key], ValueNorm)
        INCLUDE (EntidadId);

    CREATE INDEX IX_WF_EntidadIndice_Entidad
        ON dbo.WF_EntidadIndice (EntidadId);
END;
");

                        Exec(cn, @"
INSERT INTO dbo.WF_SchemaVersion (VersionId, Nombre)
VALUES (2026022401, N'ENTIDAD core (WF_Entidad / WF_EntidadItem / WF_EntidadIndice)');");
                    }
                }
            }
            catch
            {
                // No romper el arranque: si falla, se verá al ejecutar features de entidad.
                // (En entornos productivos, luego lo conectamos a un logger central)
            }
        }

        private static bool HasVersion(SqlConnection cn, int versionId)
        {
            using (var cmd = new SqlCommand(
                "SELECT 1 FROM dbo.WF_SchemaVersion WHERE VersionId=@V", cn))
            {
                cmd.Parameters.AddWithValue("@V", versionId);
                var x = cmd.ExecuteScalar();
                return x != null && x != DBNull.Value;
            }
        }

        private static void Exec(SqlConnection cn, string sql)
        {
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.CommandTimeout = 60;
                cmd.ExecuteNonQuery();
            }
        }
    }
}