param([string]$Exe, [string]$Out)
Add-Type -AssemblyName System.Drawing
$ico = [System.Drawing.Icon]::ExtractAssociatedIcon($Exe)
$bmp = $ico.ToBitmap()
$sum = 0; $n = 0
for ($y = 0; $y -lt $bmp.Height; $y += 2) {
    for ($x = 0; $x -lt $bmp.Width; $x += 2) {
        $c = $bmp.GetPixel($x, $y)
        $sum += $c.R * 3 + $c.G * 5 + $c.B * 7
        $n++
    }
}
$fp = [math]::Round($sum / [math]::Max($n,1), 2)
$line = "size=$($bmp.Width)x$($bmp.Height) fingerprint=$fp"
Write-Host $line
if ($Out) { Set-Content -LiteralPath $Out -Value $line -Encoding UTF8 }
