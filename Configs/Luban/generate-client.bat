@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Generate the client-group C# readers and binary data after verifying the exact pinned Luban tool package.
set "CONFIG_ROOT=%~dp0"
for %%I in ("%CONFIG_ROOT%\..\..") do set "REPO_ROOT=%%~fI"
set "LUBAN_EXE=%REPO_ROOT%\Tools\Luban\v4.10.2\Luban\Luban.exe"
set "WORKBOOK=%REPO_ROOT%\Configs\Xlsx\CharacterConfig.xlsx"
set "CLIENT_CODE_DIR=%REPO_ROOT%\Assets\PromeArchTrial\Generated\LubanClient"
set "CLIENT_DATA_DIR=%REPO_ROOT%\Assets\StreamingAssets\PromeArchTrial\Config"
call "%CONFIG_ROOT%verify-tool.bat"
if errorlevel 1 exit /b %errorlevel%
if not exist "%WORKBOOK%" echo [Luban] Missing workbook: "%WORKBOOK%" & exit /b 20
rem The client target includes shared gameplay fields plus client-only presentation fields; ref validation failures are fatal.
"%LUBAN_EXE%" --conf "%CONFIG_ROOT%luban.conf" -t client -c cs-bin -d bin --validationFailAsError -x "outputCodeDir=%CLIENT_CODE_DIR%" -x "outputDataDir=%CLIENT_DATA_DIR%"
if errorlevel 1 echo [Luban] Client generation failed. & exit /b %errorlevel%
echo [Luban] Client code: "%CLIENT_CODE_DIR%"
echo [Luban] Client data: "%CLIENT_DATA_DIR%"
exit /b 0
