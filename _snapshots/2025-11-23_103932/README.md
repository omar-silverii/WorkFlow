# Workflow Studio (ASP.NET WebForms)

Editor + motor mínimo de flujos (grafo) con persistencia en SQL.

## Solución
- `Intranet.WorkflowStudio.WebForms.sln`
- Sitio WebForms (.NET Framework), UI en `WorkflowUI.aspx`, JS en `Scripts/workflow.*.js`
- Motor en `App_Code/MotorFlujoMinimo.cs`
- Tablas: `WF_Definicion`, `WF_Instancia`, `WF_InstanciaLog`

## Requisitos
- Windows + Visual Studio (con .NET Framework 4.x)
- SQL Server (Express vale)

## Cómo correr
1. Configurar `Web.config` local (ver `Web.config.example`).
2. Abrir la `.sln` en VS, compilar y ejecutar con IIS Express.
3. Abrir `/WorkflowUI.aspx` para el editor.

## Flujo de trabajo con Git
- Rama de trabajo: `dev`  
- PRs de `dev` → `main` (main protegido)

## Notas
- No subir secretos. Usar `Web.config.example` y mantener tu `Web.config` local.
