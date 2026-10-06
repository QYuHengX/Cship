# 07 验收：大桌面（+200 文件）首扫与批量删除（触发 watcher 64KB 缓冲）一致性
# 用法: pwsh -NoProfile -File build\_step7_bigdesk.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$exe   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\Cship.exe'
$cfg   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\dependencies\config'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$pwsh  = 'C:\Program Files\PowerShell\7\pwsh.exe'
$log   = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
$desk  = [Environment]::GetFolderPath('Desktop')
$prefix = '_step7probe_'

function Log-New([int]$from) { Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $from }
function Show([string]$t) { Write-Host ''; Write-Host "===== $t =====" -ForegroundColor Cyan }
function Desk-Count { (Get-ChildItem -LiteralPath $desk -File -ErrorAction SilentlyContinue).Count }

# 收尾：删掉所有探针文件（无论成败）
function Cleanup-Probe {
    Get-ChildItem -LiteralPath $desk -File -Filter "$prefix*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

Cleanup-Probe
$baseCount = Desk-Count
Write-Host ("桌面原有文件数 = {0}" -f $baseCount)

Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
try {
    Show '1) 启动并开板（基线）'
    $from = (Get-Content $log).Count
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 4
    $r = (& $pwsh -NoProfile -File $probe rects $p.Id | Select-Object -First 1) -split ','
    & $pwsh -NoProfile -File $probe click $p.Id ([int]$r[0] + [int]([int]$r[2] / 2)) ([int]$r[1] + [int]([int]$r[3] / 2)) | Out-Null
    Start-Sleep -Seconds 5
    (Log-New $from) | Where-Object { $_ -match '桌面扫描|收纳板陈列' } | ForEach-Object { Write-Host '   ' $_ }

    Show '2) 批量创建 200 个文件 → 等 watcher 刷新'
    $t = [Diagnostics.Stopwatch]::StartNew()
    1..200 | ForEach-Object { Set-Content -LiteralPath (Join-Path $desk ("{0}{1:d4}.txt" -f $prefix, $_)) -Value "x" -Encoding UTF8 }
    $createMs = $t.ElapsedMilliseconds
    Write-Host ("  创建 200 个文件耗时 {0} ms；桌面文件数 = {1}" -f $createMs, (Desk-Count))
    $from2 = (Get-Content $log).Count
    Start-Sleep -Seconds 8
    (Log-New $from2) | Where-Object { $_ -match '桌面扫描|收纳板陈列|监视|缓冲|重扫|WARN|ERROR' } | ForEach-Object { Write-Host '   ' $_ }
    Write-Host ("  开板状态 WS = {0} MB" -f [math]::Round((Get-Process -Id $p.Id).WorkingSet64 / 1MB, 1))

    Show '3) 一次性删除 200 个文件（冲击 watcher 缓冲 + 差量对账）'
    $t = [Diagnostics.Stopwatch]::StartNew()
    Get-ChildItem -LiteralPath $desk -File -Filter "$prefix*" | Remove-Item -Force
    $delMs = $t.ElapsedMilliseconds
    Write-Host ("  删除 200 个文件耗时 {0} ms；桌面文件数 = {1}" -f $delMs, (Desk-Count))
    $from3 = (Get-Content $log).Count
    Start-Sleep -Seconds 8
    (Log-New $from3) | Where-Object { $_ -match '桌面扫描|收纳板陈列|监视|缓冲|重扫|WARN|ERROR' } | ForEach-Object { Write-Host '   ' $_ }

    Show '4) 结果'
    $q = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    Write-Host ("  进程存活 = {0}" -f ($q -ne $null))
    $all = Log-New $from
    $errs = $all | Where-Object { $_ -match '\[WARN\]|\[ERROR\]' }
    Write-Host ("  本会话 WARN/ERROR = {0}" -f $errs.Count)
    $errs | ForEach-Object { Write-Host '   ' $_ }
    Write-Host ("  最终桌面文件数 = {0}（应与基线 {1} 一致）" -f (Desk-Count), $baseCount)
}
finally {
    Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    Cleanup-Probe
    Write-Host ("收尾：探针文件已清理，桌面文件数 = {0}" -f (Desk-Count))
}
