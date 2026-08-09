@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Execute Protocol v5, Luban ref, prediction, gameplay, world-combat, Ping/Pong, and 30 Hz acceptance checks.
dotnet run --project "%~dp0BattleServer.csproj" -- --smoke-test %*
exit /b %errorlevel%
