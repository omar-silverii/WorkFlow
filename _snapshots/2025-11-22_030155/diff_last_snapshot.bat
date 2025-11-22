@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "ROOT=%CD%"
set "SNAPROOT=%ROOT%\_snapshots"

if not exist "%SNAPROOT%" (
  echo [diff] No existe "_snapshots". Ejecuta primero snapshot.bat
  exit /b 1
)

for /f "delims=" %%D in ('dir /b /ad /o-n "%SNAPROOT%"') do (
  if not defined LAST set "LAST=%%D"
)

if not defined LAST (
  echo [diff] No hay snapshots en "%SNAPROOT%".
  exit /b 1
)

set "LEFT=%SNAPROOT%\%LAST%"
set "RIGHT=%ROOT%"

rem Permite override manual:
if defined WINMERGE_PATH (
  set "WM=%WINMERGE_PATH%"
) else (
  set "WM=%ProgramFiles%\WinMerge\WinMergeU.exe"
  if not exist "%WM%" set "WM=%ProgramFiles(x86)%\WinMerge\WinMergeU.exe"
)

if not exist "%WM%" (
  echo [diff] No encuentro WinMergeU.exe. Define WINMERGE_PATH o instala WinMerge.
  exit /b 1
)

echo [diff] Abriendo WinMerge: "%LEFT%"  vs  "%RIGHT%"
"%WM%" -e -u -dl "Snapshot %LAST%" -dr "Actual" "%LEFT%" "%RIGHT%"
exit /b 0
