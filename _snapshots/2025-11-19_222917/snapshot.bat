@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "ROOT=%CD%"
set "SNAPROOT=%ROOT%\_snapshots"
if not exist "%SNAPROOT%" mkdir "%SNAPROOT%"

for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HHmmss"') do set "TS=%%I"
set "DEST=%SNAPROOT%\%TS%"

echo [snapshot] Creando snapshot en "%DEST%"
robocopy "%ROOT%" "%DEST%" /E /R:1 /W:1 ^
 /XD bin obj .vs .git node_modules _snapshots ^
 /XF *.user *.suo *.cache *.pdb *.obj *.log *.tmp

if errorlevel 8 (
  echo [snapshot] Robocopy devolvio codigo %errorlevel% (error). Revisar arriba.
) else (
  echo [snapshot] Snapshot creado OK.
)

if "%~1" neq "" (
  > "%DEST%\SNAPSHOT.txt" echo %*
)

echo [snapshot] Listo: %DEST%
exit /b 0
