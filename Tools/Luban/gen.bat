@echo off
REM 导出战斗与玩法配表：Excel -> C# 代码 + 二进制数据。
REM 代码落到 Assets/Gen/Config，数据落到 Assets/BundleResources/Table。
setlocal
set WORKSPACE=%~dp0..\..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\v4.10.2\Luban\Luban.dll
set CONF_ROOT=%WORKSPACE%\Data\Excel

dotnet "%LUBAN_DLL%" ^
    -t client ^
    -c cs-bin ^
    -d bin ^
    --conf "%CONF_ROOT%\luban.conf" ^
    -x outputCodeDir="%WORKSPACE%\Assets\Gen\Config\Generated" ^
    -x outputDataDir="%WORKSPACE%\Assets\BundleResources\Table"

if %ERRORLEVEL% neq 0 ( echo. & echo [导表失败] 错误码 %ERRORLEVEL% & exit /b %ERRORLEVEL% )
echo.
echo [导表成功] 代码: Assets/Gen/Config/Generated  数据: Assets/BundleResources/Table
endlocal
