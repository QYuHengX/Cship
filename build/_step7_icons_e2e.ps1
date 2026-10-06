# 07 §2 桌面图标崩溃恢复 端到端实测（hideDesktopIcons=true → 强杀 → 重启应恢复可见）
# 全程 try/finally 保证结束时桌面图标一定可见、settings.json 一定还原。
# 用法: pwsh -NoProfile -File build\_step7_icons_e2e.ps1
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$exe   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\Cship.exe'
$cfg   = Join-Path $root 'src\Cship\bin\Debug\net8.0-windows\dependencies\config'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
$pwsh  = 'C:\Program Files\PowerShell\7\pwsh.exe'
$harness = Join-Path $root 'build\_step7test\bin\Debug\net8.0-windows\step7test.exe'
$log   = Join-Path $cfg ("logs\cship_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
$settings = Join-Path $cfg 'settings.json'

function Log-Count { (Get-Content -LiteralPath $log -Encoding UTF8).Count }
function Log-New([int]$from) { Get-Content -LiteralPath $log -Encoding UTF8 | Select-Object -Skip $from }
function State-Json { Get-Content -LiteralPath (Join-Path $cfg 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json }
function IconVis { (& $pwsh -NoProfile -File $probe icons) -replace '.*visible=', '' }
function Set-HideSetting([bool]$v) {
    $j = Get-Content -LiteralPath $settings -Raw -Encoding UTF8 | ConvertFrom-Json
    $j.'general.hideDesktopIcons' = $v
    $j | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settings -Encoding UTF8
}
function Show([string]$t) { Write-Host ''; Write-Host "===== $t =====" -ForegroundColor Cyan }
function Stop-App { Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
function Start-App {
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 4
    return $p
}

Copy-Item $settings "$settings.orig" -Force
try {
    Show '1) 意图=隐藏：启动后桌面图标应被隐藏'
    Stop-App
    Set-HideSetting $true
    $from = Log-Count
    $p = Start-App
    Write-Host ("  桌面图标实测：{0}（应为 False=隐藏）" -f (IconVis))
    $s = State-Json
    Write-Host ("  state: lastRunOk={0}  hideIconsOwned={1}（应为 False / True）" -f $s.lastRunOk, $s.hideIconsOwned)
    (Log-New $from) | Where-Object { $_ -match '桌面图标' } | ForEach-Object { Write-Host '   ' $_ }

    Show '2) 强杀进程（不走退出清理，模拟崩溃）'
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 900
    Write-Host ("  强杀后桌面图标实测：{0}（应仍为 False=隐藏，残留）" -f (IconVis))
    $s = State-Json
    Write-Host ("  state: lastRunOk={0}（应为 False）  hideIconsOwned={1}（应仍为 True）" -f $s.lastRunOk, $s.hideIconsOwned)

    Show '3) 再启动：应"先强制恢复可见，再按当前设置重新隐藏"'
    $from = Log-Count
    $p = Start-App
    (Log-New $from) | Where-Object { $_ -match '桌面图标|未正常退出|启动自检' } | ForEach-Object { Write-Host '   ' $_ }
    Write-Host ("  最终桌面图标实测：{0}（意图=隐藏，故应为 False）" -f (IconVis))

    Show '4) 意图改为显示 → 再启动：应恢复可见且不再隐藏'
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 900
    Set-HideSetting $false
    $from = Log-Count
    $p = Start-App
    (Log-New $from) | Where-Object { $_ -match '桌面图标|未正常退出|启动自检' } | ForEach-Object { Write-Host '   ' $_ }
    Write-Host ("  桌面图标实测：{0}（应为 True=可见）" -f (IconVis))
    $s = State-Json
    Write-Host ("  state: hideIconsOwned={0}（应为 False）" -f $s.hideIconsOwned)
}
finally {
    Show '5) 收尾：还原设置 + 强制恢复桌面图标可见'
    Stop-App
    Copy-Item "$settings.orig" $settings -Force
    Remove-Item "$settings.orig" -Force
    # 用夹具强制恢复可见（双保险）
    & $harness icontoggle | Out-Null
    Write-Host ("  桌面图标实测：{0}（必须为 True）" -f (IconVis))
    Write-Host '  用户 settings.json 已还原'
}
