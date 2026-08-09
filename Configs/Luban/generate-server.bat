@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Generate server-group C# readers and binary data after verifying the exact pinned Luban tool package.
set "CONFIG_ROOT=%~dp0"
for %%I in ("%CONFIG_ROOT%\..\..") do set "REPO_ROOT=%%~fI"
set "LUBAN_EXE=%REPO_ROOT%\Tools\Luban\v4.10.2\Luban\Luban.exe"
set "WORKBOOK=%REPO_ROOT%\Configs\Xlsx\CharacterConfig.xlsx"
if defined PROMETHEUS_BATTLE_SERVER_ROOT (set "SERVER_ROOT=%PROMETHEUS_BATTLE_SERVER_ROOT%") else (for %%I in ("%REPO_ROOT%\..\battle-server") do set "SERVER_ROOT=%%~fI")
set "SERVER_CODE_DIR=%SERVER_ROOT%\Generated\Config"
set "SERVER_DATA_DIR=%SERVER_ROOT%\ConfigData\Luban"
call "%CONFIG_ROOT%verify-tool.bat"
if errorlevel 1 exit /b %errorlevel%
if not exist "%WORKBOOK%" echo [Luban] Missing workbook: "%WORKBOOK%" & exit /b 20
if not exist "%SERVER_ROOT%\BattleServer.csproj" echo [Luban] Battle server was not found at "%SERVER_ROOT%". Set PROMETHEUS_BATTLE_SERVER_ROOT to override the sibling-directory convention. & exit /b 21
rem The server target excludes all presentation fields and assets while retaining the same validated gameplay references.
"%LUBAN_EXE%" --conf "%CONFIG_ROOT%luban.conf" -t server -c cs-bin -d bin --validationFailAsError -x "outputCodeDir=%SERVER_CODE_DIR%" -x "outputDataDir=%SERVER_DATA_DIR%"
if errorlevel 1 echo [Luban] Server generation failed. & exit /b %errorlevel%
echo [Luban] Server code: "%SERVER_CODE_DIR%"
echo [Luban] Server data: "%SERVER_DATA_DIR%"
exit /b 0
