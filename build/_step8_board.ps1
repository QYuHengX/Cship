# 08 发布态开板内存实测
param([string]$Exe, [switch]$Click, [int]$Warmup = 4)
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$probe = Join-Path $PSScriptRoot '_step7_probe.ps1'
Get-Process Cship -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $Warmup
$base = (Get-Process -Id $p.Id).WorkingSet64
Write-Host ("baseline WS = {0} MB" -f [math]::Round($base/1MB,1))
$rects = @(& $pwsh -NoProfile -File $probe rects $p.Id)
Write-Host "visible windows:"
foreach ($r in $rects) { Write-Host ("   " + $r) }
if ($Click) {
    $best = $null
    foreach ($r in $rects) {
        $a = $r -split ','
        $w = [int]$a[2]; $h = [int]$a[3]
        if ($w -le 0 -or $h -le 0) { continue }
        if ($w -gt 900 -or $h -gt 900) { continue }   # 排除全屏/大窗
        if ($null -eq $best -or ($w*$h) -lt ($best[2]*$best[3])) { $best = @([int]$a[0],[int]$a[1],$w,$h) }
    }
    if ($best) {
        $cx = $best[0] + [int]($best[2]/2); $cy = $best[1] + [int]($best[3]/2)
        Write-Host ("click dock at ({0},{1}) size {2}x{3}" -f $cx,$cy,$best[2],$best[3])
        & $pwsh -NoProfile -File $probe click $p.Id $cx $cy
        Start-Sleep -Milliseconds 2500
        $peak = (Get-Process -Id $p.Id).WorkingSet64
        Write-Host ("after open WS = {0} MB" -f [math]::Round($peak/1MB,1))
        Start-Sleep -Seconds 6
        $stable = (Get-Process -Id $p.Id).WorkingSet64
        Write-Host ("board stable WS = {0} MB" -f [math]::Round($stable/1MB,1))
        $rects2 = @(& $pwsh -NoProfile -File $probe rects $p.Id)
        Write-Host ("windows after open: " + ($rects2 -join ' | '))
    } else { Write-Host "no dock-like window found" }
}
Start-Sleep -Seconds 1
Stop-Process -Id $p.Id -Force
Write-Host "done"
