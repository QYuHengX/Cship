# 08 步发布态冒烟与性能实测（启动耗时 / 内存 / 首启生成 config / 日志 WARN·ERROR）
# 用法: powershell -NoProfile -File build\_step8_smoke.ps1 -Exe "D:\...\Releases\Cship.exe" [-Quick] [-Label x64]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Label = 'run',
    [switch]$Quick
)

$ErrorActionPreference = 'Stop'
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$exeDir = Split-Path -Parent $Exe
$cfg = Join-Path $exeDir 'dependencies\config'
$log = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))

function Log-Count { if (Test-Path $log) { (Get-Content -LiteralPath $log -Encoding UTF8).Count } else { 0 } }
function Log-New([int]$from) {
    if (-not (Test-Path $log)) { return @() }
    return @(Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $from)
}
function VisWins([int]$id) { return [int](& $pwsh -NoProfile -File $probe win $id) }

Write-Host ("===== 发布态冒烟 [{0}] =====" -f $Label) -ForegroundColor Cyan
Write-Host ("exe : {0}" -f $Exe)
Write-Host ("cfg : {0}" -f $cfg)

# 0) 前置：清残留实例（单实例互斥，开发态/发布态共用互斥体）
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$cfgExistedBefore = Test-Path -LiteralPath $cfg
Write-Host ("启动前 config 存在：{0}" -f $cfgExistedBefore)

# 1) 冷启动计时（到"该进程有可见顶层窗口"= 悬浮窗可见）
$from = Log-Count
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -PassThru
$visibleMs = -1
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    if ((VisWins $p.Id) -gt 0) { $visibleMs = $sw.ElapsedMilliseconds; break }
    Start-Sleep -Milliseconds 25
}
$sw.Stop()
Write-Host ("[启动] 双击 → 悬浮窗可见：{0} ms   (PID={1})" -f $visibleMs, $p.Id) -ForegroundColor Yellow

Start-Sleep -Seconds 3
$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { throw '进程已退出（启动失败）' }
Write-Host ("[内存] 启动 3s 后 WS = {0} MB, Private = {1} MB" -f `
        [math]::Round($proc.WorkingSet64 / 1MB, 1), [math]::Round($proc.PrivateMemorySize64 / 1MB, 1))

# 2) 首启结构生成
$cfgNow = Test-Path -LiteralPath $cfg
Write-Host ("[首启] config 目录：启动前={0} 启动后={1}" -f $cfgExistedBefore, $cfgNow)
if ($cfgNow) {
    foreach ($sub in 'settings.json', 'state.json', 'iconcache', 'assets', 'logs') {
        $q = Join-Path $cfg $sub
        Write-Host ("        {0,-14} {1}" -f $sub, (Test-Path -LiteralPath $q))
    }
}
$startupLines = Log-New $from | Where-Object { $_ -match 'startup|启动' }
$startupLines | ForEach-Object { Write-Host ('        ' + $_) }

# 3) 空闲内存/CPU（30s 心跳会落盘；Quick 模式缩短）
$idleSec = if ($Quick) { 15 } else { 60 }
$cpu0 = (Get-Process -Id $p.Id).TotalProcessorTime
Start-Sleep -Seconds $idleSec
$q = Get-Process -Id $p.Id
$cpu1 = $q.TotalProcessorTime
$cpuMs = ($cpu1 - $cpu0).TotalMilliseconds
Write-Host ("[空闲] {0}s 后 WS = {1} MB, CPU = {2} ms ({3:N2}% 单核)" -f `
        $idleSec, [math]::Round($q.WorkingSet64 / 1MB, 1), [math]::Round($cpuMs, 0),
    ($cpuMs / ($idleSec * 1000) * 100))

# 4) 日志体检
$newLines = Log-New $from
$warns = @($newLines | Where-Object { $_ -match '\[WARN\]' })
$errs = @($newLines | Where-Object { $_ -match '\[ERROR\]' })
Write-Host ("[日志] 新增 {0} 行；WARN={1} ERROR={2}" -f $newLines.Count, $warns.Count, $errs.Count)
$warns | Select-Object -First 5 | ForEach-Object { Write-Host ('   WARN  ' + $_) }
$errs | Select-Object -First 5 | ForEach-Object { Write-Host ('   ERROR ' + $_) }

# 5) 退出（自动化只能强杀：托盘"关闭"菜单需真实点击，见 STEP_LOG 留人工项）
if (-not $KeepRunning) {
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 800
    Write-Host '[退出] 已强制结束（干净退出路径见留人工验证项）'
}

Write-Host ''
Write-Host ("RESULT[{0}] startupMs={1} ws3s={2}MB idleWs={3}MB idleCpuPct={4:N2} warn={5} err={6}" -f `
        $Label, $visibleMs, [math]::Round(($proc.WorkingSet64) / 1MB, 1),
    [math]::Round($q.WorkingSet64 / 1MB, 1), ($cpuMs / ($idleSec * 1000) * 100), $warns.Count, $errs.Count)
