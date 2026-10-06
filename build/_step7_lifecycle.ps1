# 07 步整机生命周期实测（启动耗时 / 内存 / 强杀恢复 / TaskbarCreated 重放 / 配置损坏自愈）
# 用法: pwsh -NoProfile -File build\_step7_lifecycle.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$exe   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\Cship.exe'
$cfg   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\dependencies\config'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$pwsh  = 'C:\Program Files\PowerShell\7\pwsh.exe'
$log   = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))

function Log-Count { if (Test-Path $log) { (Get-Content -LiteralPath $log -Encoding UTF8).Count } else { 0 } }
function Log-New([int]$from) { if (-not (Test-Path $log)) { return @() }; (Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $from) }
function State-Json { Get-Content -LiteralPath (Join-Path $cfg 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function Show([string]$t) { Write-Host ''; Write-Host "===== $t =====" -ForegroundColor Cyan }
function VisWins([int]$id) { [int](& $pwsh -NoProfile -File $probe win $id) }

Show '0) 前置：清残留实例'
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Show '1) 启动耗时（到悬浮窗可见）与内存'
$from = Log-Count
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $exe -PassThru
$visibleMs = -1
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
    if ((VisWins $p.Id) -gt 0) { $visibleMs = $sw.ElapsedMilliseconds; break }
    Start-Sleep -Milliseconds 40
}
$sw.Stop()
Write-Host ("  启动到悬浮窗可见：{0} ms  (PID={1})" -f $visibleMs, $p.Id)
Start-Sleep -Seconds 3
$q = Get-Process -Id $p.Id
Write-Host ("  启动 3s 后 WS = {0} MB" -f [math]::Round($q.WorkingSet64 / 1MB, 1))
$newLines = Log-New $from
Write-Host ("  启动期日志新增 {0} 行，WARN/ERROR = {1}" -f $newLines.Count, (($newLines | Where-Object { $_ -match '\[WARN\]|\[ERROR\]' }).Count))
$newLines | Where-Object { $_ -match '资源管理器重启监视|桌面图标|启动自检' } | ForEach-Object { Write-Host '   ' $_ }

Show '2) 空闲 60s 后的内存与 CPU（含 30s 心跳落盘）'
$cpu0 = (Get-Process -Id $p.Id).TotalProcessorTime
Start-Sleep -Seconds 60
$q = Get-Process -Id $p.Id
$cpu1 = $q.TotalProcessorTime
Write-Host ("  空闲 60s 后 WS = {0} MB   本分钟 CPU = {1} ms" -f [math]::Round($q.WorkingSet64 / 1MB, 1), [math]::Round(($cpu1 - $cpu0).TotalMilliseconds, 0))
Write-Host ("  state.json lastRunOk = {0}（心跳应保持 False）" -f (State-Json).lastRunOk)

Show '3) 强杀（模拟崩溃）→ lastRunOk 应保持 False'
Stop-Process -Id $p.Id -Force
Start-Sleep -Milliseconds 800
Write-Host ("  强杀后 lastRunOk = {0}" -f (State-Json).lastRunOk)

Show '4) 重启：应报告上次未正常退出并执行启动自检'
$from = Log-Count
$p2 = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
(Log-New $from) | Where-Object { $_ -match '未正常退出|启动自检|桌面图标|资源管理器' } | ForEach-Object { Write-Host '   ' $_ }

Show '5) 广播 TaskbarCreated（等价资源管理器重启）'
$from = Log-Count
& $pwsh -NoProfile -File $probe taskbar | Write-Host
Start-Sleep -Seconds 3
(Log-New $from) | Where-Object { $_ -match 'TaskbarCreated|重放|资源管理器' } | ForEach-Object { Write-Host '   ' $_ }

Show '6) 破坏 settings.json 为非法 JSON → 应自愈为默认 + 生成 .bak + 气泡'
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
$settingsPath = Join-Path $cfg 'settings.json'
$bakPath = "$settingsPath.bak"
if (Test-Path $bakPath) { Remove-Item $bakPath -Force }
Copy-Item $settingsPath "$settingsPath.orig" -Force
Set-Content -LiteralPath $settingsPath -Value '{ this is not valid json,,, ' -Encoding UTF8 -NoNewline
$from = Log-Count
$p3 = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
(Log-New $from) | Where-Object { $_ -match '解析|备份|损坏' } | ForEach-Object { Write-Host '   ' $_ }
Write-Host ("  settings.json 已重建为合法 JSON：{0}" -f ((Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8) -match '^\s*\{'))
Write-Host ("  .bak 已生成：{0}（{1} 字节）" -f (Test-Path $bakPath), (Get-Item $bakPath -ErrorAction SilentlyContinue).Length)
# 还原用户原设置
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
Copy-Item "$settingsPath.orig" $settingsPath -Force
Remove-Item "$settingsPath.orig" -Force
if (Test-Path $bakPath) { Remove-Item $bakPath -Force }
Write-Host '  已还原用户原 settings.json'

Show '7) 汇总'
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host '  完成（详见上方各节）'
