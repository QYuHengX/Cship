# 08 发布态功能抽查：启动 → 点悬浮窗开板 → 截图取证
param(
    [string]$Exe = 'D:\AiAgent\OCproject\dock++\Releases\Cship.exe',
    [string]$Out = 'D:\AiAgent\OCproject\dock++\build\_step8_board_shot.png'
)
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$py = 'C:\Users\YuHeng\AppData\Local\Programs\Python\Python313\python.exe'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 4
$rects = @(& $pwsh -NoProfile -File $probe rects $p.Id)
$best = $null
foreach ($r in $rects) {
    $a = $r -split ','
    $w = [int]$a[2]; $h = [int]$a[3]
    if ($w -le 0 -or $h -le 0 -or $w -gt 900 -or $h -gt 900) { continue }
    if ($null -eq $best -or ($w * $h) -lt ($best[2] * $best[3])) { $best = @([int]$a[0], [int]$a[1], $w, $h) }
}
if (-not $best) { throw 'no dock window' }
$cx = $best[0] + [int]($best[2] / 2); $cy = $best[1] + [int]($best[3] / 2)
Write-Host ("click dock ({0},{1})" -f $cx, $cy)
& $pwsh -NoProfile -File $probe click $p.Id $cx $cy | Write-Host
Start-Sleep -Milliseconds 3000
& $py -c "from PIL import ImageGrab; ImageGrab.grab().save(r'$Out')"
Write-Host ("shot -> {0}" -f $Out)
$rects2 = @(& $pwsh -NoProfile -File $probe rects $p.Id)
Write-Host ("windows after open: " + ($rects2 -join ' | '))
$ws = [math]::Round((Get-Process -Id $p.Id).WorkingSet64 / 1MB, 1)
Write-Host ("WS after open = {0} MB" -f $ws)
Stop-Process -Id $p.Id -Force
Write-Host 'closed'
