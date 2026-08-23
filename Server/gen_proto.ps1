# 手动生成 Go / C# 协议代码。
# 仅在修改 proto 后手动执行；服务器启动 / 游戏启动流程不会自动调用本脚本。
# 用法：在 Server/ 目录下执行  ./gen_proto.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot                     # 项目根目录
$protoc = Join-Path $root 'Tools\protoc\bin\protoc.exe'      # protoc 编译器
$gobin = Join-Path (go env GOPATH) 'bin\protoc-gen-go.exe'   # Go 插件

Push-Location $PSScriptRoot
try {
    # 1. 生成 Go 代码 -> Server/gen/protocol
    & $protoc --plugin=protoc-gen-go=$gobin --go_out=. --go_opt=module=prometheus proto/poi.proto
    if ($LASTEXITCODE -ne 0) { throw "Go codegen failed (exit $LASTEXITCODE)" }

    # 2. 生成 C# 代码 -> Assets/Gen/Protocol
    & $protoc --csharp_out=../Assets/Gen/Protocol proto/poi.proto
    if ($LASTEXITCODE -ne 0) { throw "C# codegen failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}
Write-Host "proto codegen done: Server/gen/protocol + Assets/Gen/Protocol"
