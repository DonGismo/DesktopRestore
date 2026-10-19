# Erzeugt assets\app.ico (mehrere Groessen, PNG-komprimiert) mit System.Drawing.
param([string]$OutFile = (Join-Path $PSScriptRoot '..\assets\app.ico'))

Add-Type -AssemblyName System.Drawing

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Monitor-Rahmen
    $pad = [Math]::Max(1, [int]($size * 0.06))
    $screenH = [int]($size * 0.68)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), $screenH
    $radius = [Math]::Max(2, $size * 0.10)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 32, 120, 220)), ([System.Drawing.Color]::FromArgb(255, 18, 70, 160)), 90
    $g.FillPath($brush, $path)

    # Standfuss
    $footW = $size * 0.36
    $footH = [Math]::Max(1, $size * 0.07)
    $stemW = [Math]::Max(1, $size * 0.10)
    $gray = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 90, 100, 115))
    $g.FillRectangle($gray, ($size - $stemW) / 2, $rect.Bottom, $stemW, $size * 0.12)
    $g.FillRectangle($gray, ($size - $footW) / 2, $size - $pad - $footH, $footW, $footH)

    # Icon-Raster (2 Spalten x 3 Zeilen) auf dem Bildschirm
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 255, 255, 255))
    $cell = $rect.Height / 3.6
    $box = [Math]::Max(1, $cell * 0.62)
    for ($c = 0; $c -lt 2; $c++) {
        for ($r = 0; $r -lt 3; $r++) {
            $x = $rect.X + $rect.Width * 0.12 + $c * $cell
            $y = $rect.Y + $rect.Height * 0.10 + $r * $cell
            $g.FillRectangle($white, $x, $y, $box, $box)
        }
    }
    # Pfeil (Wiederherstellen) rechts
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.2, $size * 0.07))
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $arcSize = $rect.Height * 0.55
    $g.DrawArc($pen, $rect.Right - $arcSize - $rect.Width * 0.10, $rect.Y + ($rect.Height - $arcSize) / 2, $arcSize, $arcSize, 200, 280)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) { $frames += , (New-Frame $s) }

$dir = Split-Path $OutFile -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$fs = [System.IO.File]::Create($OutFile)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $frames[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }
$bw.Close()
Write-Host "Icon geschrieben: $OutFile"
