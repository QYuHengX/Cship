#Requires -Version 5.1
<#
  makeicon.ps1 — 程序图标生成（步骤 08 · 任务 1）

  源图：assets\CShip.png（用户提供的产品图；非方形 → 补透明底正方形、居中缩放，不变形）
        兼容旧位置 素材\CShip.png（开发期目录）。

  产出（三处同源，观感一致）：
    src\Cship\app.ico                                ← csproj <ApplicationIcon>（exe 文件图标 / 任务栏 / 任务管理器）
    src\Cship\dependencies\resources\icons\app.ico   ← 托盘 NotifyIcon
    src\Cship\dependencies\resources\icons\app.png   ← 设置-关于页 Logo（512，透明底）

  帧格式：16~128 用 DIB(BMP) 帧、256 用 PNG 帧 —— 与既有产物一致，
          托盘 System.Drawing.Icon 与 shell 取帧无兼容风险（GDI+ 对纯 PNG 帧支持不完整）。

  幂等：重复运行结果一致（先删旧产物；ICO 直接在内存组装，全程无中间文件落盘）。

  替换图标：覆盖源图后重跑本脚本，再跑 build.ps1 重新打包即可。

  用法（仓库根）：
    powershell -NoProfile -ExecutionPolicy Bypass -File build\makeicon.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceImage,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256),
    [int]$LargePng = 512
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if (-not $SourceImage) {
    foreach ($cand in @((Join-Path $root 'assets\CShip.png'), (Join-Path $root '素材\CShip.png'))) {
        if (Test-Path -LiteralPath $cand) { $SourceImage = $cand; break }
    }
}
if (-not $SourceImage -or -not (Test-Path -LiteralPath $SourceImage)) {
    throw "找不到图标源图。请用 -SourceImage <png 路径> 指定，或放置 assets\CShip.png。"
}

$iconsDir = Join-Path $root 'src\Cship\dependencies\resources\icons'
$appIco = Join-Path $root 'src\Cship\app.ico'
$trayIco = Join-Path $iconsDir 'app.ico'
$aboutPng = Join-Path $iconsDir 'app.png'
New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

function New-SquareBitmap {
    param([System.Drawing.Image]$Image, [int]$Side)
    $canvas = New-Object System.Drawing.Bitmap($Side, $Side, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $scale = [Math]::Min($Side / $Image.Width, $Side / $Image.Height)
        $w = [int][Math]::Round($Image.Width * $scale)
        $h = [int][Math]::Round($Image.Height * $scale)
        $g.DrawImage($Image, [int](($Side - $w) / 2), [int](($Side - $h) / 2), $w, $h)
    } finally { $g.Dispose() }
    return $canvas
}

function Get-DibFrameBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $w = $Bitmap.Width; $h = $Bitmap.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $buf = New-Object byte[] ($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    } finally { $Bitmap.UnlockBits($data) }

    $andRow = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([uint32]40)                       # biSize
        $bw.Write([int32]$w)                        # biWidth
        $bw.Write([int32]($h * 2))                  # biHeight（XOR + AND 两段）
        $bw.Write([uint16]1)                        # biPlanes
        $bw.Write([uint16]32)                       # biBitCount
        $bw.Write([uint32]0)                        # biCompression = BI_RGB
        $bw.Write([uint32]($w * 4 * $h))            # biSizeImage
        $bw.Write([int32]0); $bw.Write([int32]0)    # 分辨率占位
        $bw.Write([uint32]0); $bw.Write([uint32]0)  # clrUsed / clrImportant
        $rowBytes = $w * 4
        for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($buf, $y * $stride, $rowBytes) }  # DIB 自下而上
        $bw.Write((New-Object byte[] ($andRow * $h)))                                   # AND 掩码（透明由 alpha 决定）
    } finally { $bw.Flush(); $bw.Dispose() }
    return $ms.ToArray()
}

function Get-PngFrameBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = New-Object System.IO.MemoryStream
    try { $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $ms.Dispose() }
    return $ms.ToArray()
}

function Write-IcoFile {
    param([string]$Path, [object[]]$Frames)
    $list = @()
    foreach ($f in $Frames) {
        $list += [pscustomobject]@{ Size = [int]$f.Size; Bytes = [byte[]]$f.Bytes }
    }
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$list.Count)
        $offset = 6 + 16 * $list.Count
        foreach ($f in $list) {
            $dim = [byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size })
            $bw.Write($dim); $bw.Write($dim)
            $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$f.Bytes.Length)
            $bw.Write([uint32]$offset)
            $offset += $f.Bytes.Length
        }
        foreach ($f in $list) { $bw.Write($f.Bytes, 0, $f.Bytes.Length) }
        $bw.Flush()
        [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    } finally { $bw.Dispose(); $ms.Dispose() }
}

# 幂等：先清旧产物
foreach ($p in @($appIco, $trayIco, $aboutPng)) {
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
}

$src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $SourceImage).Path)
try {
    Write-Host "源图：$SourceImage ($($src.Width)x$($src.Height))"
    $frames = @()
    foreach ($s in ($Sizes | Sort-Object)) {
        $bmp = New-SquareBitmap -Image $src -Side $s
        try {
            $bytes = if ($s -ge 256) { Get-PngFrameBytes -Bitmap $bmp } else { Get-DibFrameBytes -Bitmap $bmp }
            $frames += [pscustomobject]@{ Size = $s; Bytes = $bytes }
        } finally { $bmp.Dispose() }
    }
    Write-IcoFile -Path $appIco -Frames $frames
    Copy-Item -LiteralPath $appIco -Destination $trayIco -Force

    $large = New-SquareBitmap -Image $src -Side $LargePng
    try { $large.Save($aboutPng, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $large.Dispose() }
} finally { $src.Dispose() }

Write-Host ("ICO -> {0}  ({1} bytes, {2} 帧: {3})" -f $appIco, (Get-Item -LiteralPath $appIco).Length, $frames.Count, (($Sizes | Sort-Object) -join '/'))
Write-Host ("ICO -> {0}  ({1} bytes)" -f $trayIco, (Get-Item -LiteralPath $trayIco).Length)
Write-Host ("PNG -> {0}  ({1} bytes, {2}px)" -f $aboutPng, (Get-Item -LiteralPath $aboutPng).Length, $LargePng)
