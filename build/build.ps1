#Requires -Version 5.1
<#
  build.ps1 — Cship 双架构单文件发布（步骤 08 · 任务 2）

  产出（**仓库根 Releases\**，自包含、整体可任意移动/改名）：
    Releases\Cship.exe                       ← win-x64 单文件 self-contained
    Releases\Cship-x86.exe                   ← win-x86 单文件（必须重命名，否则与 x64 同名冲突）
    Releases\dependencies\resources\         ← i18n / icons，两架构**共用一份**
    （dependencies\config\ 不预置：程序首启自建 settings/state/iconcache/assets/logs）

  发布参数口径（08 §2；**压缩默认关闭，理由见下**）：
    PublishTrimmed=false                  WPF 不支持裁剪，严禁改 true
    IncludeNativeLibrariesForSelfExtract  单文件内嵌原生库（压缩前提）
    EnableCompressionInSingleFile         **默认 false**；-Compress 可开启
    SatelliteResourceLanguages=en         去掉非英文附属资源程序集（省体积）
    DebugType=none                        不产出 .pdb
    InvariantGlobalization 保持 false     中文排序/日期必需（不在本脚本改）

  **压缩取舍实测（2026-10-06 · 本机 Win10 19045 · 43 项桌面 · 同一口径）**：
    压缩开启：exe 63.2MB，双击→悬浮窗 1267/1555ms，常驻 60s WS 245.9MB，开板稳定 214.3MB
    压缩关闭：exe 139.3MB，双击→悬浮窗  827ms，      常驻 60s WS 125.8MB，开板稳定 138.2MB
    → 压缩使**常驻内存 +120MB、启动慢 45%**（自解压缓存常驻），只换来 -76MB 体积。
    08 §4「体积与启动耗时不可兼得时以启动耗时为先」+ 00 §0「体积是七维度末位、与用户体验
    冲突时让位」→ **默认不压缩**。需要小体积分发时用 -Compress 自行权衡。

  幂等：先清空 Releases\ 再产出；暂存目录 build\_publish\ 用完即删（-KeepTemp 保留）。

  用法（仓库根）：
    powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1
    ... -Compress               # 开压缩（体积换内存/启动，见上表）
    ... -Only x64               # 只打一个架构（调试用）
#>
[CmdletBinding()]
param(
    [switch]$Compress,
    [switch]$KeepTemp,
    [ValidateSet('both', 'x64', 'x86')][string]$Only = 'both'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\Cship'
$outDir = Join-Path $root 'Releases'
$stage = Join-Path $root 'build\_publish'
$resSrc = Join-Path $proj 'dependencies\resources'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    return (Get-ChildItem -LiteralPath $Path -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
}

function Format-MB {
    param([long]$Bytes)
    return ('{0:N1} MB' -f ($Bytes / 1MB))
}

function Invoke-Publish {
    param([string]$Rid, [string]$Dest)
    $compress = if ($Compress) { 'true' } else { 'false' }
    $pubArgs = @(
        'publish', $proj,
        '-c', 'Release',
        '-r', $Rid,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=none',
        '-p:SatelliteResourceLanguages=en',
        "-p:EnableCompressionInSingleFile=$compress",
        '-v', 'minimal',
        '-o', $Dest
    )
    Write-Host ("  dotnet publish {0} ..." -f $Rid) -ForegroundColor DarkGray
    $t = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @pubArgs 2>&1
    $code = $LASTEXITCODE
    $t.Stop()
    if ($code -ne 0) {
        $output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
        throw "dotnet publish $Rid 失败（退出码 $code）"
    }
    $warn = @($output | Where-Object { $_ -match 'warning|警告' })
    Write-Host ("    {0} 完成，用时 {1:N1}s，{2} 条警告" -f $Rid, $t.Elapsed.TotalSeconds, $warn.Count)
    return $t.Elapsed.TotalSeconds
}

Write-Host '=== Cship 发布 ===' -ForegroundColor Cyan
Write-Host ("压缩单文件：{0}" -f $(if ($Compress) { '开启' } else { '关闭' }))
Write-Host ("目标架构：{0}" -f $Only)

# 1) 清空交付目录与暂存目录（防旧文件混入导致体积/回归数据失真）
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 2) 双架构发布
$timings = @{}
if ($Only -in @('both', 'x64')) {
    $timings['x64'] = Invoke-Publish -Rid 'win-x64' -Dest (Join-Path $stage 'x64')
}
if ($Only -in @('both', 'x86')) {
    $timings['x86'] = Invoke-Publish -Rid 'win-x86' -Dest (Join-Path $stage 'x86')
}

# 3) 组装交付目录
Write-Host '组装 Releases\ ...' -ForegroundColor DarkGray
if ($timings.ContainsKey('x64')) {
    Copy-Item -LiteralPath (Join-Path $stage 'x64\Cship.exe') -Destination (Join-Path $outDir 'Cship.exe') -Force
}
if ($timings.ContainsKey('x86')) {
    Copy-Item -LiteralPath (Join-Path $stage 'x86\Cship.exe') -Destination (Join-Path $outDir 'Cship-x86.exe') -Force
}

# resources 只放一份（从源复制，显式排除 config，避免开发期数据回归）
$resDst = Join-Path $outDir 'dependencies\resources'
New-Item -ItemType Directory -Force -Path $resDst | Out-Null
Copy-Item -Path (Join-Path $resSrc '*') -Destination $resDst -Recurse -Force
$stray = @(Get-ChildItem -LiteralPath (Join-Path $outDir 'dependencies') -Recurse -Directory -Filter 'config')
if ($stray.Count -gt 0) { $stray | Remove-Item -Recurse -Force }

# 4) 摘要
Write-Host ''
Write-Host '=== 交付摘要 ===' -ForegroundColor Cyan
foreach ($exe in @(Get-ChildItem -LiteralPath $outDir -Filter '*.exe' | Sort-Object Name)) {
    Write-Host ("  {0,-16} {1}" -f $exe.Name, (Format-MB $exe.Length))
}
Write-Host ("  {0,-16} {1}" -f 'dependencies\', (Format-MB (Get-DirSize (Join-Path $outDir 'dependencies'))))
Write-Host ("  {0,-16} {1}" -f '合计', (Format-MB (Get-DirSize $outDir)))
foreach ($k in $timings.Keys) { Write-Host ("  发布耗时 {0}: {1:N1}s" -f $k, $timings[$k]) }
Write-Host ("  总耗时: {0:N1}s" -f $sw.Elapsed.TotalSeconds)
Write-Host ''
Write-Host '产物：' -ForegroundColor DarkGray
Get-ChildItem -LiteralPath $outDir -Recurse -File | ForEach-Object {
    Write-Host ("    " + $_.FullName.Substring($outDir.Length + 1))
}

if (-not $KeepTemp) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
else { Write-Host ("暂存保留：" + $stage) -ForegroundColor DarkGray }
