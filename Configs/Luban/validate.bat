@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Validate the pinned tool, XML schema, workbook data, group projections, and every Luban ref without writing generated outputs.
set "CONFIG_ROOT=%~dp0"
for %%I in ("%CONFIG_ROOT%\..\..") do set "REPO_ROOT=%%~fI"
set "LUBAN_EXE=%REPO_ROOT%\Tools\Luban\v4.10.2\Luban\Luban.exe"
set "WORKBOOK=%REPO_ROOT%\Configs\Xlsx\CharacterConfig.xlsx"
call "%CONFIG_ROOT%verify-tool.bat"
if errorlevel 1 exit /b %errorlevel%
rem A schema-only pass still proves the CLI and repository-relative paths before the workbook exists.
"%LUBAN_EXE%" --conf "%CONFIG_ROOT%luban.conf" -t client --validationFailAsError
if errorlevel 1 echo [Luban] XML schema validation failed. & exit /b %errorlevel%
if not exist "%WORKBOOK%" echo [Luban] XML schema is valid, but data validation cannot run because "%WORKBOOK%" does not exist. & exit /b 20
rem Force-load client data first so client-only presentation refs are checked.
"%LUBAN_EXE%" --conf "%CONFIG_ROOT%luban.conf" -t client -f --validationFailAsError
if errorlevel 1 echo [Luban] Client-group data or ref validation failed. & exit /b %errorlevel%
rem Force-load server data separately so accidental client-only dependencies cannot leak into authoritative configuration.
"%LUBAN_EXE%" --conf "%CONFIG_ROOT%luban.conf" -t server -f --validationFailAsError
if errorlevel 1 echo [Luban] Server-group data or ref validation failed. & exit /b %errorlevel%
echo [Luban] Client and server configuration validation succeeded.
exit /b 0
