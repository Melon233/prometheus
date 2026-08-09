@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Resolve every path relative to this script so callers can run it from any working directory.
set "CONFIG_ROOT=%~dp0"
for %%I in ("%CONFIG_ROOT%\..\..") do set "REPO_ROOT=%%~fI"
set "LUBAN_EXE=%REPO_ROOT%\Tools\Luban\v4.10.2\Luban\Luban.exe"
set "LUBAN_ARCHIVE=%REPO_ROOT%\Tools\Luban\v4.10.2\Luban.7z"
set "EXPECTED_PRODUCT_VERSION=4.10.2+332018b42be100dfc3e2bc77b7647e79851bb861"
set "EXPECTED_EXE_SHA256=8567455B4FF73E95636945AF511C40F2758913139FBD3E957194449C7FFFE1ED"
set "EXPECTED_ARCHIVE_SHA256=785B53B570C918827D314EF78CAA180CA1C55BC252EBC1E921A6DC0760317E8D"
rem Refuse to generate with a missing, silently upgraded, or locally modified Luban executable.
if not exist "%LUBAN_EXE%" echo [Luban] Missing pinned executable: "%LUBAN_EXE%" & exit /b 10
if not exist "%LUBAN_ARCHIVE%" echo [Luban] Missing official release archive retained for provenance: "%LUBAN_ARCHIVE%" & exit /b 11
for /f "delims=" %%H in ('%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$sha=[System.Security.Cryptography.SHA256]::Create();[System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($env:LUBAN_EXE))).Replace('-','')"') do set "ACTUAL_EXE_SHA256=%%H"
for /f "delims=" %%H in ('%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$sha=[System.Security.Cryptography.SHA256]::Create();[System.BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($env:LUBAN_ARCHIVE))).Replace('-','')"') do set "ACTUAL_ARCHIVE_SHA256=%%H"
for /f "delims=" %%V in ('%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoLogo -NoProfile -NonInteractive -Command "(Get-Item -LiteralPath $env:LUBAN_EXE).VersionInfo.ProductVersion"') do set "ACTUAL_PRODUCT_VERSION=%%V"
if /I not "%ACTUAL_EXE_SHA256%"=="%EXPECTED_EXE_SHA256%" echo [Luban] Executable SHA-256 mismatch. Expected %EXPECTED_EXE_SHA256% but received %ACTUAL_EXE_SHA256%. & exit /b 12
if /I not "%ACTUAL_ARCHIVE_SHA256%"=="%EXPECTED_ARCHIVE_SHA256%" echo [Luban] Release archive SHA-256 mismatch. Expected %EXPECTED_ARCHIVE_SHA256% but received %ACTUAL_ARCHIVE_SHA256%. & exit /b 13
if /I not "%ACTUAL_PRODUCT_VERSION%"=="%EXPECTED_PRODUCT_VERSION%" echo [Luban] Product version mismatch. Expected %EXPECTED_PRODUCT_VERSION% but received %ACTUAL_PRODUCT_VERSION%. & exit /b 14
echo [Luban] Verified v4.10.2 executable, product version, and retained release archive.
exit /b 0
