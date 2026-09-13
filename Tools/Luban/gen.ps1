# 导出战斗与玩法配表：Excel -> C# 代码 + 二进制数据。
# 与 gen.bat 等价，供 CI 与 Unity 编辑器菜单调用。
$ErrorActionPreference = 'Stop'
$workspace = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$lubanDll  = Join-Path $workspace 'Tools\Luban\v4.10.2\Luban\Luban.dll'
$confRoot  = Join-Path $workspace 'Data\Excel'

& dotnet $lubanDll `
    -t client `
    -c cs-bin `
    -d bin `
    --conf (Join-Path $confRoot 'luban.conf') `
    -x "outputCodeDir=$(Join-Path $workspace 'Assets\Gen\Config\Generated')" `
    -x "outputDataDir=$(Join-Path $workspace 'Assets\BundleResources\Table')"

if ($LASTEXITCODE -ne 0) { throw "导表失败，错误码 $LASTEXITCODE" }
Write-Host "[导表成功] 代码: Assets/Gen/Config/Generated  数据: Assets/BundleResources/Table"
