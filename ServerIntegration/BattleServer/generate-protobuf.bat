@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Protobuf C# generation is owned by Grpc.Tools in BattleServer.csproj so this command performs the authoritative build-time generation path.
dotnet build "%~dp0BattleServer.csproj" %*
exit /b %errorlevel%
