# 07 §4 遗留热点 2 冒烟：hoverReveal 模式开板 + 鼠标扫掠（驱动 UpdateReveal 的缓存路径）
# 用法: pwsh -NoProfile -File build\_step7_reveal_smoke.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$exe   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\Cship.exe'
$cfg   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\dependencies\config'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$pwsh  = 'C:\Program Files\PowerShell\7\pwsh.exe'
$log   = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
$settings = Join-Path $cfg 'settings.json'

function Log-New([int]$from) { Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $from }
function Rect($id) { (& $pwsh -NoProfile -File $probe rects $id | Select-Object -First 1) }
function Show([string]$t) { Write-Host ''; Write-Host "===== $t =====" -ForegroundColor Cyan }

Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Copy-Item $settings "$settings.orig" -Force
try {
    $j = Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json
    $j.'display.iconStyle' = 'hoverReveal'
    $j.'display.showLabels' = $true
    $j | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settings -Encoding UTF8

    Show '1) hoverReveal 模式启动并开板'
    $from = (Get-Content $log).Count
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 4
    $r = Rect $p.Id
    $a = $r -split ','
    $cx = [int]$a[0] + [int]([int]$a[2] / 2); $cy = [int]$a[1] + [int]([int]$a[3] / 2)
    & $pwsh -NoProfile -File $probe click $p.Id $cx $cy | Write-Host
    Start-Sleep -Seconds 5
    Write-Host '  开板日志：'
    (Log-New $from) | Where-Object { $_ -match '扫描|陈列|首载|材质|WARN|ERROR' } | ForEach-Object { Write-Host '   ' $_ }

    Show '2) 在板上扫掠鼠标（驱动 UpdateReveal 逐图标 SetReveal）'
    $r = Rect $p.Id
    if ($r) {
        $a = $r -split ','
        $x0 = [int]$a[0] + 40; $y0 = [int]$a[1] + 80
        $x1 = [int]$a[0] + [int]$a[2] - 40; $y1 = [int]$a[1] + [int]$a[3] - 60
        $from2 = (Get-Content $log).Count
        & $pwsh -NoProfile -File $probe sweep $p.Id $x0 $y0 $x1 $y1 60 | Write-Host
        & $pwsh -NoProfile -File $probe sweep $p.Id $x1 $y0 $x0 $y1 60 | Write-Host
        & $pwsh -NoProfile -File $probe sweep $p.Id $x0 $y1 $x1 $y0 60 | Write-Host
        Start-Sleep -Milliseconds 500
        $errs = (Log-New $from2) | Where-Object { $_ -match '\[WARN\]|\[ERROR\]' }
        Write-Host ("  扫掠期间 WARN/ERROR 条数 = {0}" -f $errs.Count)
        $errs | ForEach-Object { Write-Host '   ' $_ }
        $q = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        Write-Host ("  进程存活 = {0}  WS = {1} MB" -f ($q -ne $null), [math]::Round($q.WorkingSet64 / 1MB, 1))
    }
    else { Write-Host '  （板未打开，跳过扫掠）' }

    Show '3) 会话 WARN/ERROR 汇总'
    Write-Host ("  本会话 WARN/ERROR = {0}" -f ((Log-New $from) | Where-Object { $_ -match '\[WARN\]|\[ERROR\]' }).Count)
    (Log-New $from) | Where-Object { $_ -match '\[WARN\]|\[ERROR\]' } | ForEach-Object { Write-Host '   ' $_ }
}
finally {
    Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    Copy-Item "$settings.orig" $settings -Force
    Remove-Item "$settings.orig" -Force
    Write-Host '已还原 settings.json'
}
