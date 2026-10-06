# 08 精简版引导器端到端实测：强制引导 → 弹窗 → 真实下载 → 解压 → 启动主程序
# 用法: powershell -NoProfile -File build\_step8_lite.ps1 [-LiteDir <路径>]
param(
    [string]$LiteDir = 'D:\AiAgent\OCproject\dock++\Releases\精简版',
    [string]$ShotDir = 'D:\AiAgent\OCproject\dock++\build'
)
$ErrorActionPreference = 'Stop'
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$py = 'C:\Users\YuHeng\AppData\Local\Programs\Python\Python313\python.exe'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$bootExe = Join-Path $LiteDir 'Cship.exe'
$rtRoot = Join-Path $LiteDir 'dependencies\runtime'

function Shot([string]$name) {
    & $py -c "from PIL import ImageGrab; ImageGrab.grab().save(r'$ShotDir\_step8_lite_$name.png')"
    Write-Host ("   截图 -> _step8_lite_{0}.png" -f $name)
}

Write-Host '===== 精简版引导器端到端 =====' -ForegroundColor Cyan
Get-Process Cship, Cship.app, CshipBootstrapper -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
if (Test-Path -LiteralPath $rtRoot) { Remove-Item -LiteralPath $rtRoot -Recurse -Force }
Write-Host ("引导器: {0}" -f $bootExe)
Write-Host ("exe 体积（引导器，内嵌主程序）: {0:N0} KB" -f ((Get-Item $bootExe).Length / 1KB))
$relApp = Join-Path $LiteDir 'dependencies\app\Cship.app.exe'

# 1) 强制引导（CSHIP_FORCE_BOOTSTRAP=1）：即使系统已有运行时也走下载窗体
$env:CSHIP_FORCE_BOOTSTRAP = '1'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $bootExe -PassThru
$uiMs = -1
while ($sw.ElapsedMilliseconds -lt 15000) {
    if ([int](& $pwsh -NoProfile -File $probe win $p.Id) -gt 0) { $uiMs = $sw.ElapsedMilliseconds; break }
    Start-Sleep -Milliseconds 25
}
Write-Host ("[UI] 双击 → 引导窗体可见: {0} ms" -f $uiMs) -ForegroundColor Yellow
Start-Sleep -Milliseconds 800
Shot 'dialog'

# 2) 等待下载+解压完成（判定：runtime 目录出现 host\fxr 且引导器进程退出）
$ready = $false
$deadline = (Get-Date).AddMinutes(8)
while ((Get-Date) -lt $deadline) {
    $fxr = Join-Path $rtRoot 'x64\dotnet\host\fxr'
    if ((Test-Path -LiteralPath $fxr) -and (Get-ChildItem -LiteralPath $fxr -Directory).Count -gt 0) { $ready = $true; break }
    if ($p.HasExited) { break }
    Start-Sleep -Milliseconds 500
}
$dlMs = $sw.ElapsedMilliseconds
Write-Host ("[下载+解压] runtime 就绪: {0}（{1:N0}s，ready={2}）" -f $dlMs, ($dlMs / 1000.0), $ready) -ForegroundColor Yellow

if (Test-Path -LiteralPath $rtRoot) {
    $sz = (Get-ChildItem -LiteralPath $rtRoot -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("   runtime 目录合计: {0:N1} MB" -f ($sz / 1MB))
}

# 3) 等引导器退出（启动主程序后）并确认主程序悬浮窗
Start-Sleep -Seconds 3
if (-not (Test-Path -LiteralPath $relApp)) { throw ("主程序未释放: " + $relApp) }
Write-Host ("[释放] 主程序已就位: dependencies\app\Cship.app.exe ({0:N0} KB)" -f ((Get-Item -LiteralPath $relApp).Length / 1KB))
$main = Get-Process 'Cship.app' -ErrorAction SilentlyContinue
if (-not $main) { throw '主程序（Cship.app）未启动' }
$visMs = $sw.ElapsedMilliseconds
$wins = [int](& $pwsh -NoProfile -File $probe win $main[0].Id)
Write-Host ("[主程序] Cship.app PID={0} 悬浮窗可见窗口数={1}（t+{2:N0}s）" -f $main[0].Id, $wins, ($visMs / 1000.0)) -ForegroundColor Yellow
Start-Sleep -Seconds 2
$ws = [math]::Round($main[0].WorkingSet64 / 1MB, 1)
Write-Host ("[主程序] WS = {0} MB（framework-dependent，无自解压开销）" -f $ws)
Shot 'main'

# 4) 二次启动：应检测本地 runtime 直接进主程序（不弹窗）
Stop-Process -Name 'Cship.app' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800
Remove-Item Env:\CSHIP_FORCE_BOOTSTRAP -ErrorAction SilentlyContinue
$sw2 = [System.Diagnostics.Stopwatch]::StartNew()
$p2 = Start-Process -FilePath $bootExe -PassThru
Start-Sleep -Seconds 2
$main2 = Get-Process 'Cship.app' -ErrorAction SilentlyContinue
$bootStill = Get-Process -Id $p2.Id -ErrorAction SilentlyContinue
Write-Host ("[二次启动] 引导器存留={0}（应为空，即直接转发）；主程序={1}；耗时 {2:N0} ms" -f `
        ($null -ne $bootStill), ($null -ne $main2), $sw2.ElapsedMilliseconds) -ForegroundColor Yellow

# 5) 日志
$log = Join-Path $LiteDir 'dependencies\runtime\bootstrapper.log'
if (Test-Path -LiteralPath $log) { Write-Host '--- bootstrapper.log ---'; Get-Content -LiteralPath $log -Encoding UTF8 | ForEach-Object { Write-Host ('   ' + $_) } }

Get-Process Cship.app, CshipBootstrapper -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host '===== 完成 =====' -ForegroundColor Cyan
