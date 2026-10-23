<#
.SYNOPSIS
  Start a MuMu emulator instance under RenderDoc so frames can be captured from it (Windows PowerShell 5.1).

.DESCRIPTION
  Actions (each prints one JSON object):
    detect    find MuMu and RenderDoc, the instance, its renderer mode and whether it is already injected
    launch    shut the instance down if it runs (needs -Force when it runs WITHOUT RenderDoc), then start its device
              process through `renderdoccmd capture`, and wait until RenderDoc's target-control port is listening
    restore   shut the instance down and start it normally through MuMuManager (no RenderDoc)
    captures  list the newest .rdc files in -OutDir and in RenderDoc's default folder (%TEMP%\RenderDoc)

  Paths: -MuMuDir / -RenderDocDir win; otherwise the uninstall registry, the .rdc file association and common install
  folders are searched. When something is not found the JSON says so ("error"), and the caller asks the user.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File mumu_capture.ps1 -Action detect
  powershell -ExecutionPolicy Bypass -File mumu_capture.ps1 -Action launch -Index 0 -OutDir D:\Rdc -Force
#>
param(
    [ValidateSet('detect', 'launch', 'restore', 'captures')] [string]$Action = 'detect',
    [string]$MuMuDir,
    [string]$RenderDocDir,
    [int]$Index = 0,
    [string]$OutDir = 'D:\Rdc',
    [string]$Name = 'MuMuNxDevice',
    [switch]$Force,
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Out-Result($obj) { $obj | ConvertTo-Json -Depth 6; exit 0 }

function Find-MuMu {
    if ($MuMuDir) { return $MuMuDir }
    $keys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    foreach ($e in (Get-ItemProperty $keys -ErrorAction SilentlyContinue)) {
        if ($e.DisplayName -match 'MuMu' -and $e.InstallLocation -and (Test-Path (Join-Path $e.InstallLocation 'nx_main\MuMuManager.exe'))) { return $e.InstallLocation.TrimEnd('\') }
    }
    $candidates = @()
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Root })) {
        $candidates += Join-Path $drive 'MuMuPlayer'
        $candidates += Join-Path $drive 'Program Files\Netease\MuMuPlayer'
        $candidates += Join-Path $drive 'Program Files\Netease\MuMu Player 12'
    }
    foreach ($c in $candidates) { if (Test-Path (Join-Path $c 'nx_main\MuMuManager.exe')) { return $c } }
    return $null
}

function Find-RenderDoc {
    if ($RenderDocDir) { return $RenderDocDir }
    try {
        $cmd = (Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\RenderDoc.RDCCapture.1\shell\open\command' -ErrorAction Stop).'(default)'
        if ($cmd -match '"([^"]+qrenderdoc\.exe)"') { $d = Split-Path $Matches[1]; if (Test-Path (Join-Path $d 'renderdoccmd.exe')) { return $d } }
    } catch {}
    foreach ($c in @("$env:ProgramFiles\RenderDoc", "${env:ProgramFiles(x86)}\RenderDoc")) { if ($c -and (Test-Path (Join-Path $c 'renderdoccmd.exe'))) { return $c } }
    return $null
}

function Get-InstanceInfo($manager) {
    try { return (& $manager info -v $Index | Out-String | ConvertFrom-Json) } catch { return $null }
}

# RenderDoc's in-application target-control server listens on the first free port from 38920.
function Get-InjectedPort($procId) {
    if (-not $procId) { return $null }
    $c = Get-NetTCPConnection -State Listen -OwningProcess $procId -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -ge 38920 -and $_.LocalPort -le 38935 } | Select-Object -First 1
    if ($c) { return $c.LocalPort } else { return $null }
}

function Get-State {
    $mumu = Find-MuMu
    $rd = Find-RenderDoc
    $s = [ordered]@{ mumu_dir = $mumu; renderdoc_dir = $rd; index = $Index; out_dir = $OutDir; error = $null }
    if (-not $mumu) { $s.error = 'mumu_not_found'; return $s }
    if (-not $rd) { $s.error = 'renderdoc_not_found'; return $s }
    $s.manager = Join-Path $mumu 'nx_main\MuMuManager.exe'
    $vm = Get-ChildItem (Join-Path $mumu 'vms') -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "-$Index$" } | Select-Object -First 1
    if (-not $vm) { $s.error = "instance_${Index}_not_found"; $s.instances = @(Get-ChildItem (Join-Path $mumu 'vms') -Directory -ErrorAction SilentlyContinue | ForEach-Object Name); return $s }
    $s.vm = $vm.Name                                         # e.g. MuMuPlayer-15.0-0
    $ver = if ($vm.Name -match '-(\d+(\.\d+)*)-\d+$') { $Matches[1] } else { $null }
    $device = $null
    if ($ver) { $device = Join-Path $mumu "nx_device\$ver\shell\MuMuNxDevice.exe" }
    if (-not $device -or -not (Test-Path $device)) { $device = Get-ChildItem (Join-Path $mumu 'nx_device') -Recurse -Filter MuMuNxDevice.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
    if (-not $device) { $s.error = 'device_exe_not_found'; return $s }
    $s.device_exe = $device
    try {
        $cfg = Get-Content (Join-Path $vm.FullName 'configs\shell_config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $p = $cfg.renderer.platform
        $s.renderer_platform = $p
        $s.renderer_api = switch ($p) { 'ngl' { 'OpenGL' } default { "unknown ($p): check the renderer setting in MuMu" } }
    } catch { $s.renderer_platform = $null }
    $info = Get-InstanceInfo $s.manager
    $s.running = [bool]($info -and $info.is_process_started)
    $s.android_ready = [bool]($info -and $info.is_android_started)
    $s.pid = if ($info) { $info.pid } else { $null }
    $s.injected_port = Get-InjectedPort $s.pid
    $s.injected = [bool]$s.injected_port
    return $s
}

function Wait-Until([scriptblock]$cond, [int]$sec) {
    $t = [Diagnostics.Stopwatch]::StartNew()
    while ($t.Elapsed.TotalSeconds -lt $sec) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 1000 }
    return $false
}

function Stop-Instance($s) {
    & $s.manager control -v $Index shutdown | Out-Null
    return (Wait-Until { $i = Get-InstanceInfo $s.manager; -not ($i -and $i.is_process_started) } 60)
}

$s = Get-State
if ($s.error -or $Action -eq 'detect') { Out-Result $s }

switch ($Action) {
    'launch' {
        if ($s.running -and $s.injected -and -not $Force) { $s.result = 'already_injected'; Out-Result $s }
        if ($s.running -and -not $s.injected -and -not $Force) { $s.error = 'running_not_injected_needs_force'; Out-Result $s }
        if ($s.running) { if (-not (Stop-Instance $s)) { $s.error = 'shutdown_timeout'; Out-Result $s } }
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        $shell = Split-Path $s.device_exe
        $template = Join-Path $OutDir $Name
        $rdcmd = Join-Path $s.renderdoc_dir 'renderdoccmd.exe'
        $argList = @('capture', '-c', "`"$template`"", '-d', "`"$shell`"", "`"$($s.device_exe)`"", '--vm', $s.vm)
        Start-Process -FilePath $rdcmd -ArgumentList $argList -WindowStyle Hidden | Out-Null
        $ok = Wait-Until { $i = Get-InstanceInfo $s.manager; $i -and $i.is_process_started -and (Get-InjectedPort $i.pid) } $TimeoutSec
        $s = Get-State
        $s.capture_template = $template
        $s.result = if ($ok) { 'launched_injected' } else { 'launch_timeout' }
        Out-Result $s
    }
    'restore' {
        if ($s.running) { if (-not (Stop-Instance $s)) { $s.error = 'shutdown_timeout'; Out-Result $s } }
        & $s.manager control -v $Index launch | Out-Null
        $ok = Wait-Until { $i = Get-InstanceInfo $s.manager; $i -and $i.is_process_started } $TimeoutSec
        $s = Get-State
        $s.result = if ($ok -and -not $s.injected) { 'launched_normal' } elseif ($ok) { 'launched_but_still_injected' } else { 'launch_timeout' }
        Out-Result $s
    }
    'captures' {
        $dirs = @($OutDir, (Join-Path $env:TEMP 'RenderDoc')) | Where-Object { Test-Path $_ }
        $s.captures = @(Get-ChildItem $dirs -Filter *.rdc -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 10 |
            ForEach-Object { [ordered]@{ path = $_.FullName; size_mb = [math]::Round($_.Length / 1MB, 1); time = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') } })
        Out-Result $s
    }
}
