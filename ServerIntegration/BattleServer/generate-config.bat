@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Ask the pinned Luban tool in the sibling Prometheus checkout to generate only the server projection into this project.
for %%I in ("%~dp0.") do set "PROMETHEUS_BATTLE_SERVER_ROOT=%%~fI"
set "PROMETHEUS_ROOT=%~dp0..\Prometheus"
if not exist "%PROMETHEUS_ROOT%\Configs\Luban\generate-server.bat" echo [BattleServer] Missing sibling Prometheus Luban generator: "%PROMETHEUS_ROOT%\Configs\Luban\generate-server.bat" & exit /b 10
call "%PROMETHEUS_ROOT%\Configs\Luban\generate-server.bat"
exit /b %errorlevel%
