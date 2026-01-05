/*
3) Bandejas del gerente (queries finales)
A) Mis Tareas (pendientes por usuario o rol)


INSERT INTO dbo.WF_UsuarioRol (Usuario, Rol)
VALUES
('omard/omard', 'GERENCIA'),
('omard/omard', 'FINANZAS'),
('omard/omard', 'JEFE_AREA');


INSERT INTO dbo.WF_UsuarioScope (Usuario, ScopeKey)
VALUES
('omard/omard', 'COMPRAS'),
('omard/omard', 'RRHH');


UPDATE dbo.WF_Instancia
SET ScopeKey = 'COMPRAS', ProcesoKey = 'NP'
WHERE WF_DefinicionId = 66;


INSERT INTO dbo.WF_ScopeRegla (DocTipoId, Campo, MatchTipo, MatchValor, ScopeKey)
VALUES
(1, 'input.sector', 'contains', 'Compras', 'COMPRAS'),
(1, 'input.sector', 'contains', 'Finanzas', 'FINANZAS');


*/

DECLARE @Usuario nvarchar(100) = 'omard/omard';

SELECT TOP (200)
    t.Id,
    t.WF_InstanciaId,
    t.NodoId,
    t.Titulo,
    t.RolDestino,
    t.UsuarioAsignado,
    t.Estado,
    t.FechaCreacion
FROM dbo.WF_Tarea t
WHERE t.Estado = 'Pendiente'
  AND (
        LOWER(t.UsuarioAsignado) = LOWER(@Usuario)
        OR EXISTS (
            SELECT 1
            FROM dbo.WF_UsuarioRol ur
            WHERE LOWER(ur.Usuario) = LOWER(@Usuario)
              AND ur.Rol = t.RolDestino
        )
      )
ORDER BY t.FechaCreacion DESC;

/*
B) Instancias “Mi área” (en curso, filtrado por scopes del usuario)

DECLARE @Usuario nvarchar(100) = 'omard/omard';
*/
SELECT TOP (200)
    i.Id,
    i.WF_DefinicionId,
    i.Estado,
    i.ProcesoKey,
    i.ScopeKey,
    i.FechaInicio,
    i.CreadoPor
FROM dbo.WF_Instancia i
WHERE i.Estado = 'EnCurso'
  AND EXISTS (
      SELECT 1
      FROM dbo.WF_UsuarioScope us
      WHERE us.Usuario = @Usuario
        AND us.ScopeKey = i.ScopeKey
  )
ORDER BY i.FechaInicio DESC;
/*
C) Finalizadas “Mi área”
DECLARE @Usuario nvarchar(100) = @P_Usuario;
*/

SELECT TOP (200)
    i.Id, i.WF_DefinicionId, i.ProcesoKey, i.ScopeKey, i.FechaInicio, i.FechaFin, i.CreadoPor
FROM dbo.WF_Instancia i
WHERE i.Estado = 'Finalizado'
  AND EXISTS (
      SELECT 1
      FROM dbo.WF_UsuarioScope us
      WHERE us.Usuario = @Usuario
        AND us.ScopeKey = i.ScopeKey
  )
ORDER BY i.FechaFin DESC;
/*
D) Errores “Mi área”
DECLARE @Usuario nvarchar(100) = @P_Usuario;
*/
SELECT TOP (200)
    i.Id, i.WF_DefinicionId, i.ProcesoKey, i.ScopeKey, i.FechaInicio, i.FechaFin, i.CreadoPor
FROM dbo.WF_Instancia i
WHERE i.Estado = 'Error'
  AND EXISTS (
      SELECT 1
      FROM dbo.WF_UsuarioScope us
      WHERE us.Usuario = @Usuario
        AND us.ScopeKey = i.ScopeKey
  )
ORDER BY ISNULL(i.FechaFin, i.FechaInicio) DESC;


DECLARE @DocTipoId int = 1;             -- si lo tenés en el estado; si no, podés usar WF_DefinicionId
DECLARE @Sector nvarchar(200) = @valor;  -- input.sector

SELECT TOP 1 ScopeKey
FROM dbo.WF_ScopeRegla
WHERE (DocTipoId IS NULL OR DocTipoId = @DocTipoId)
  AND Campo = 'input.sector'
  AND (
        (MatchTipo='equals'   AND @Sector = MatchValor) OR
        (MatchTipo='contains' AND @Sector LIKE '%' + MatchValor + '%')
      )
ORDER BY Id DESC;
USE [Workflow]
GO

/****** Object:  Table [dbo].[WF_Instancia]    Script Date: 29/12/2025 21:37:32 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[WF_Instancia](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[WF_DefinicionId] [int] NOT NULL,
	[Estado] [nvarchar](30) NOT NULL,
	[DatosEntrada] [nvarchar](max) NULL,
	[DatosContexto] [nvarchar](max) NULL,
	[FechaInicio] [datetime] NOT NULL,
	[FechaFin] [datetime] NULL,
	[CreadoPor] [nvarchar](100) NULL,
	[TenantId] [int] NULL,
	[ProcesoKey] [nvarchar](50) NULL,
	[DocTipoId] [int] NULL,
	[ScopeKey] [nvarchar](100) NULL,
	[EmpresaKey] [nvarchar](100) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[WF_Instancia] ADD  DEFAULT ('Pendiente') FOR [Estado]
GO

ALTER TABLE [dbo].[WF_Instancia] ADD  DEFAULT (getdate()) FOR [FechaInicio]
GO

ALTER TABLE [dbo].[WF_Instancia]  WITH CHECK ADD  CONSTRAINT [FK_WF_Instancia_Def] FOREIGN KEY([WF_DefinicionId])
REFERENCES [dbo].[WF_Definicion] ([Id])
GO

ALTER TABLE [dbo].[WF_Instancia] CHECK CONSTRAINT [FK_WF_Instancia_Def]
GO



