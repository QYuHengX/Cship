#Requires -Version 5.1
<#
  build.ps1 — Cship 双档双架构发布（步骤 08 · 任务 2；2026-10-06 用户规格：两档交付）

  产出（交付根 = dock++\Releases\，与源码仓库 CShips\ 同级；脚本在开发工作区或 CShips 仓库内运行都指向同一交付根）：

    Releases\完整版\   —— **纯净系统双击即用、完全离线**（self-contained 单文件，默认压缩）
      Cship.exe · Cship-x86.exe · dependencies\resources\
      .NET 8 运行时（CoreCLR + WPF + BCL + ICU）是不可分割的整体：WPF 不支持裁剪
      （PublishTrimmed 会运行时崩溃），压缩后的 63/58 MB 就是"离线即用"的正路下限。

    Releases\精简版\   —— 初始 **约 4 MB**，首次运行联网获取 .NET 8（多源 + 进度条，免管理员免安装）
      Cship.exe / Cship-x86.exe            ← 引导器（net48，系统自带 .NET Framework 4.8 即可运行）
      Cship.app.exe / Cship.app-x86.exe    ← 主程序（framework-dependent 单文件）
      dependencies\resources\              ← 预置
      dependencies\runtime\{arch}\dotnet   ← 引导器下载后生成：基础运行时 + WPF 框架两包叠加解压，
                                              注入 DOTNET_ROOT_X64/X86 启动，**不装系统、零注册表残留**

  发布参数口径：
    PublishTrimmed=false                  WPF 不支持裁剪，严禁改 true
    EnableCompressionInSingleFile         完整版**默认 true**（两档交付后体积优先；-NoCompress 可关）
                                          实测压缩使常驻内存 +120MB、启动慢 440~730ms——在意内存请用精简版
    SatelliteResourceLanguages=en         去掉非英文附属资源程序集（MessageBox 按钮文本来自 user32，不受影响）
    DebugType=none                        不产出 .pdb
    InvariantGlobalization 保持 false     中文排序/日期必需（不在本脚本改）

  幂等：按 -Mode 先清对应交付目录再产出；暂存 build\_publish\ 用完即删（-KeepTemp 保留）。

  用法（仓库根）：
    powershell -NoProfile -ExecutionPolicy Bypass -File build\build.ps1            # 两档全出
    ... -Mode full|lite       # 只出一档
    ... -NoCompress           # 完整版关压缩（体积换内存/启动，完整版曾默认不压缩，见 STEP_LOG 决策 291→296）
    ... -Only x64             # 只打一个架构（调试用）
#>
[CmdletBinding()]
param(
    [switch]$NoCompress,
    [switch]$KeepTemp,
    [ValidateSet('both', 'x64', 'x86')][string]$Only = 'both',
    [ValidateSet('both', 'full', 'lite')][string]$Mode = 'both'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\Cship'
$bootProj = Join-Path $root 'src\CshipBootstrapper'
# 交付根 = dock++\Releases\（与源码仓库 CShips\ 同级）。两种运行位置都得到同一结果：
#   · 开发工作区 dock++\（其下存在 CShips\）→ 交付根就是 $root
#   · 源码仓库 CShips\（从仓库内构建）      → 交付根是仓库的上一级
$outRoot = if (Test-Path -LiteralPath (Join-Path $root 'CShips')) {
    Join-Path $root 'Releases'
} else {
    Join-Path (Split-Path -Parent $root) 'Releases'
}
$fullDir = Join-Path $outRoot '完整版'
$liteDir = Join-Path $outRoot '精简版'
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

function Invoke-CshipPublish {
    param([string]$Rid, [string]$Dest, [bool]$SelfContained, [bool]$Compressed, [string]$Label)
    $sc = if ($SelfContained) { 'true' } else { 'false' }
    $cp = if ($Compressed) { 'true' } else { 'false' }
    $pubArgs = @(
        'publish', $proj,
        '-c', 'Release',
        '-r', $Rid,
        '--self-contained', $sc,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=none',
        '-p:SatelliteResourceLanguages=en',
        "-p:EnableCompressionInSingleFile=$cp",
        '-v', 'minimal',
        '-o', $Dest
    )
    Write-Host ("  主程序 {0}（{1}）..." -f $Rid, $Label) -ForegroundColor DarkGray
    $t = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet @pubArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
        throw "dotnet publish $Rid 失败（退出码 $LASTEXITCODE）"
    }
    $t.Stop()
    Write-Host ("    {0} 完成，用时 {1:N1}s" -f $Rid, $t.Elapsed.TotalSeconds)
}

function Invoke-BootstrapperBuild {
    param([string]$Dest)
    $pubArgs = @('publish', $bootProj, '-c', 'Release', '-v', 'minimal', '-o', $Dest)
    Write-Host '  引导器（net48）...' -ForegroundColor DarkGray
    $output = & dotnet @pubArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
        throw '引导器构建失败'
    }
}

function Copy-Resources {
    param([string]$DestDir)
    $resDst = Join-Path $DestDir 'dependencies\resources'
    New-Item -ItemType Directory -Force -Path $resDst | Out-Null
    Copy-Item -Path (Join-Path $resSrc '*') -Destination $resDst -Recurse -Force
}

Write-Host '=== Cship 发布 ===' -ForegroundColor Cyan
Write-Host ("档位：{0} · 架构：{1} · 完整版压缩：{2}" -f $Mode, $Only, $(if ($NoCompress) { '关' } else { '开' }))

# 1) 清理交付目录与暂存目录（先清后产，防旧文件混入导致体积/回归数据失真）
if ($Mode -eq 'both' -and (Test-Path -LiteralPath $outRoot)) { Remove-Item -LiteralPath $outRoot -Recurse -Force }
if (($Mode -eq 'both' -or $Mode -eq 'full') -and (Test-Path -LiteralPath $fullDir)) { Remove-Item -LiteralPath $fullDir -Recurse -Force }
if (($Mode -eq 'both' -or $Mode -eq 'lite') -and (Test-Path -LiteralPath $liteDir)) { Remove-Item -LiteralPath $liteDir -Recurse -Force }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# 2) 完整版：self-contained 单文件（默认压缩）
if ($Mode -in @('both', 'full')) {
    New-Item -ItemType Directory -Force -Path $fullDir | Out-Null
    if ($Only -in @('both', 'x64')) {
        Invoke-CshipPublish -Rid 'win-x64' -Dest (Join-Path $stage 'x64') -SelfContained $true -Compressed (-not $NoCompress) -Label 'self-contained'
        Copy-Item -LiteralPath (Join-Path $stage 'x64\Cship.exe') -Destination (Join-Path $fullDir 'Cship.exe') -Force
    }
    if ($Only -in @('both', 'x86')) {
        Invoke-CshipPublish -Rid 'win-x86' -Dest (Join-Path $stage 'x86') -SelfContained $true -Compressed (-not $NoCompress) -Label 'self-contained'
        Copy-Item -LiteralPath (Join-Path $stage 'x86\Cship.exe') -Destination (Join-Path $fullDir 'Cship-x86.exe') -Force
    }
    Copy-Resources -DestDir $fullDir
}

# 3) 精简版：主程序 framework-dependent + 引导器
if ($Mode -in @('both', 'lite')) {
    New-Item -ItemType Directory -Force -Path $liteDir | Out-Null
    if ($Only -in @('both', 'x64')) {
        Invoke-CshipPublish -Rid 'win-x64' -Dest (Join-Path $stage 'lite-x64') -SelfContained $false -Compressed $false -Label 'framework-dependent'
        Copy-Item -LiteralPath (Join-Path $stage 'lite-x64\Cship.exe') -Destination (Join-Path $liteDir 'Cship.app.exe') -Force
    }
    if ($Only -in @('both', 'x86')) {
        Invoke-CshipPublish -Rid 'win-x86' -Dest (Join-Path $stage 'lite-x86') -SelfContained $false -Compressed $false -Label 'framework-dependent'
        Copy-Item -LiteralPath (Join-Path $stage 'lite-x86\Cship.exe') -Destination (Join-Path $liteDir 'Cship.app-x86.exe') -Force
    }
    # 引导器：同一 AnyCPU 程序集改名两次（运行时按自身文件名区分 x86/x64 语义）
    Invoke-BootstrapperBuild -Dest (Join-Path $stage 'boot')
    Copy-Item -LiteralPath (Join-Path $stage 'boot\CshipBootstrapper.exe') -Destination (Join-Path $liteDir 'Cship.exe') -Force
    Copy-Item -LiteralPath (Join-Path $stage 'boot\CshipBootstrapper.exe') -Destination (Join-Path $liteDir 'Cship-x86.exe') -Force
    Copy-Resources -DestDir $liteDir
}

# 4) 摘要
Write-Host ''
Write-Host '=== 交付摘要 ===' -ForegroundColor Cyan
foreach ($dir in @($fullDir, $liteDir)) {
    if (Test-Path -LiteralPath $dir) {
        Write-Host ("  [{0}]  合计 {1}" -f (Split-Path -Leaf $dir), (Format-MB (Get-DirSize $dir)))
        Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object {
            Write-Host ("      {0,-28} {1,10:N0} KB" -f $_.FullName.Substring($dir.Length + 1), ($_.Length / 1KB))
        }
    }
}
Write-Host ("  总耗时: {0:N1}s" -f $sw.Elapsed.TotalSeconds)

if (-not $KeepTemp) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
else { Write-Host ("暂存保留：" + $stage) -ForegroundColor DarkGray }
