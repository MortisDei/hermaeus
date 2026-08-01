# Renders the Moss mascot to the app/tray raster assets.
#
# This is a direct raster of the SAME geometry as the in-app control,
# src/Hermaeus.Desktop/Controls/MossIcon.axaml, shape for shape on its 24x24
# canvas. The taskbar icon and the icon inside the app are one character, and
# the only way to keep them one character across future edits is for this file
# to be a transcription of that one rather than a second drawing of it.
#
# The first version of this script was a redraw "at icon scale" instead, with a
# wide brimmed hood and round spectacles. It read as a cowboy and looked
# nothing like Moss, which is exactly the failure mode a transcription avoids.
#
# It is also full bleed. The dark rounded field the redraw sat on cost about a
# fifth of the icon's width on every edge, so Moss rendered visibly smaller
# than every neighbouring taskbar icon, and the field itself was invisible
# anyway: Ink on a near-black taskbar is not a background, it is padding.
#
# No new dependencies: System.Drawing ships with Windows.
#
# Run: pwsh ./scripts/generate-icons.ps1
Add-Type -AssemblyName System.Drawing

$Repo = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Repo "src\Hermaeus.Desktop\Assets"

# docs/mascot.md's palette.
$DeepMoss  = [System.Drawing.Color]::FromArgb(255, 0x2E, 0x3D, 0x2B)
$Forest    = [System.Drawing.Color]::FromArgb(255, 0x43, 0x6B, 0x3F)
$Parchment = [System.Drawing.Color]::FromArgb(255, 0xE8, 0xDF, 0xC6)
$Ink       = [System.Drawing.Color]::FromArgb(255, 0x1A, 0x1D, 0x18)
$Copper    = [System.Drawing.Color]::FromArgb(255, 0xB8, 0x73, 0x33)

# MossIcon.axaml draws inside x 4..20, y 1..19 of its 24x24 canvas. Rastering
# the whole canvas would reintroduce the padding problem, so the render maps
# that content box to the output square, less a small margin so antialiasing
# has somewhere to go.
$ContentX = 4.0
$ContentY = 1.0
$ContentW = 16.0
$ContentH = 18.0
$Margin   = 0.04

function New-MossBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Uniform fit of the content box, centred.
    $inner = $Size * (1.0 - 2.0 * $Margin)
    $scale = [Math]::Min($inner / $ContentW, $inner / $ContentH)
    $offX = ($Size - $ContentW * $scale) / 2.0 - $ContentX * $scale
    $offY = ($Size - $ContentH * $scale) / 2.0 - $ContentY * $scale

    function X([double]$v) { return [single]($v * $scale + $offX) }
    function Y([double]$v) { return [single]($v * $scale + $offY) }
    function S([double]$v) { return [single]($v * $scale) }
    function P([double]$px, [double]$py) {
        return New-Object System.Drawing.PointF((X $px), (Y $py))
    }

    $bForest = New-Object System.Drawing.SolidBrush($Forest)
    $bDeep   = New-Object System.Drawing.SolidBrush($DeepMoss)
    $bFace   = New-Object System.Drawing.SolidBrush($Parchment)
    $bInk    = New-Object System.Drawing.SolidBrush($Ink)
    $bCopper = New-Object System.Drawing.SolidBrush($Copper)

    # Ears: Polygon 4,8 8,3 8,9 and its mirror.
    [System.Drawing.PointF[]]$earL = @((P 4 8), (P 8 3), (P 8 9))
    [System.Drawing.PointF[]]$earR = @((P 20 8), (P 16 3), (P 16 9))
    $g.FillPolygon($bForest, $earL)
    $g.FillPolygon($bForest, $earR)

    # Hood: Ellipse 4,5 16x14.
    $g.FillEllipse($bForest, (X 4), (Y 5), (S 16), (S 14))

    # Hood peak: Polygon 8,5 12,1 16,5.
    [System.Drawing.PointF[]]$peak = @((P 8 5), (P 12 1), (P 16 5))
    $g.FillPolygon($bDeep, $peak)

    # Face: Ellipse 7,10 10x8.
    $g.FillEllipse($bFace, (X 7), (Y 10), (S 10), (S 8))

    # Eyes: Ellipse 8.3,12.5 and 12.7,12.5, each 3x3.6.
    $g.FillEllipse($bInk, (X 8.3), (Y 12.5), (S 3), (S 3.6))
    $g.FillEllipse($bInk, (X 12.7), (Y 12.5), (S 3), (S 3.6))

    # Catchlights: Ellipse 8.9,13 and 13.3,13, each 0.9x0.9. Under 32px they
    # land on less than a pixel and read as noise in the eye rather than a
    # highlight, so they are dropped there.
    if ($Size -ge 32) {
        $g.FillEllipse($bFace, (X 8.9), (Y 13), (S 0.9), (S 0.9))
        $g.FillEllipse($bFace, (X 13.3), (Y 13), (S 0.9), (S 0.9))
    }

    # Brass spectacles band: Rectangle 7.8,13.6 8.4x1, corner radius 0.5.
    $band = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = (S 1)
    $band.AddArc((X 7.8), (Y 13.6), $d, $d, 90, 180)
    $band.AddArc((X (16.2 - 1)), (Y 13.6), $d, $d, 270, 180)
    $band.CloseFigure()
    $g.FillPath($bCopper, $band)
    $band.Dispose()

    $g.Dispose()
    foreach ($b in @($bForest, $bDeep, $bFace, $bInk, $bCopper)) { $b.Dispose() }
    return $bmp
}

# Small sizes are rendered large and downsampled. Drawing a 0.9-unit catchlight
# straight into a 16px bitmap gives a smear; supersampling gives a pixel.
function New-MossBitmapScaled {
    param([int]$Size)

    if ($Size -ge 128) { return New-MossBitmap -Size $Size }

    $big = New-MossBitmap -Size ($Size * 8)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($big, (New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)))
    $g.Dispose(); $big.Dispose()
    return $bmp
}

# PNGs. All transparent: the tray and the taskbar both composite onto whatever
# the shell's background is, and a baked-in field only makes Moss smaller.
foreach ($spec in @(
    @{n='hermaeus-app.png';        s=512},
    @{n='hermaeus-tray.png';       s=256},
    @{n='hermaeus-tray-dark.png';  s=256},
    @{n='hermaeus-tray-light.png'; s=256})) {
    $bmp = New-MossBitmapScaled -Size $spec.s
    $bmp.Save((Join-Path $Assets $spec.n), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "wrote $($spec.n) ($($spec.s)px)"
}

# Multi-size ICO. System.Drawing cannot emit one, so write the directory by hand
# over PNG-compressed frames (supported since Vista).
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-MossBitmapScaled -Size $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($sz, $ms.ToArray())
    $ms.Dispose(); $bmp.Dispose()
}

$fs = [System.IO.File]::Create((Join-Path $Assets "hermaeus.ico"))
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + (16 * $pngs.Count)
foreach ($e in $pngs) {
    $sz = $e[0]; $bytes = $e[1]
    $dim = [byte]$(if ($sz -ge 256) { 0 } else { $sz })
    $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($e in $pngs) { $bw.Write($e[1]) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
"wrote hermaeus.ico ($($pngs.Count) sizes)"
