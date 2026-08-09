@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Build and run the authoritative server; the project copies generated Luban binaries into the output directory.
dotnet run --project "%~dp0BattleServer.csproj" -- %*
exit /b %errorlevel%
