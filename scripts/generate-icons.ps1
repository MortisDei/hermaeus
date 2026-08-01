# Renders the Moss mascot to the app/tray raster assets.
#
# Palette is docs/mascot.md's: Deep Moss #2E3D2B, Forest #436B3F, Sage #7A8F6A,
# Parchment #E8DFC6, Ink #1A1D18, Copper #B87333.
#
# Composition is deliberately NOT a scaled-up copy of MossIcon.axaml. That
# control was drawn for a 16px inline glyph; enlarged, its ears read as horns
# and its spectacle band reads as a blindfold. This draws the same character at
# icon scale: centred, hooded, calm, with real round spectacles.
#
# No new dependencies: System.Drawing ships with Windows.
Add-Type -AssemblyName System.Drawing

$Repo = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $Repo "src\Hermaeus.Desktop\Assets"

$DeepMoss  = [System.Drawing.Color]::FromArgb(255, 0x2E, 0x3D, 0x2B)
$Forest    = [System.Drawing.Color]::FromArgb(255, 0x43, 0x6B, 0x3F)
$Sage      = [System.Drawing.Color]::FromArgb(255, 0x7A, 0x8F, 0x6A)
$Parchment = [System.Drawing.Color]::FromArgb(255, 0xE8, 0xDF, 0xC6)
$Ink       = [System.Drawing.Color]::FromArgb(255, 0x1A, 0x1D, 0x18)
$Copper    = [System.Drawing.Color]::FromArgb(255, 0xB8, 0x73, 0x33)

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-MossBitmap {
    param([int]$Size, [bool]$WithField)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 100x100 design space.
    $u = $Size / 100.0
    function U([double]$v) { return [single]($v * $u) }

    if ($WithField) {
        # Dark rounded field, matching the outgoing app icon's silhouette so
        # the taskbar/dock shape does not change.
        $field = New-RoundedPath (U 2) (U 2) (U 96) (U 96) (U 21)
        $b = New-Object System.Drawing.SolidBrush($Ink)
        $g.FillPath($b, $field); $b.Dispose()
        $pen = New-Object System.Drawing.Pen($DeepMoss, (U 2.5))
        $g.DrawPath($pen, $field); $pen.Dispose()
        $field.Dispose()
    }

    $bForest = New-Object System.Drawing.SolidBrush($Forest)
    $bDeep   = New-Object System.Drawing.SolidBrush($DeepMoss)
    $bSage   = New-Object System.Drawing.SolidBrush($Sage)
    $bFace   = New-Object System.Drawing.SolidBrush($Parchment)
    $bInk    = New-Object System.Drawing.SolidBrush($Ink)

    # ── Ears: low and swept back, behind the hood, so they read as ears ──────
    foreach ($side in @(-1, 1)) {
        [System.Drawing.PointF[]]$pts = @(
            (New-Object System.Drawing.PointF((U (50 + $side * 27)), (U 50))),
            (New-Object System.Drawing.PointF((U (50 + $side * 40)), (U 40))),
            (New-Object System.Drawing.PointF((U (50 + $side * 30)), (U 60))))
        $g.FillPolygon($bSage, $pts)
    }

    # ── Hood: one shape, peak included, so nothing floats ────────────────────
    $hood = New-Object System.Drawing.Drawing2D.GraphicsPath
    [System.Drawing.PointF[]]$hoodPts = @(
        (New-Object System.Drawing.PointF((U 22), (U 52))),
        (New-Object System.Drawing.PointF((U 50), (U 12))),
        (New-Object System.Drawing.PointF((U 78), (U 52))),
        (New-Object System.Drawing.PointF((U 78), (U 62))),
        (New-Object System.Drawing.PointF((U 22), (U 62))))
    $hood.AddPolygon($hoodPts)
    $g.FillPath($bForest, $hood)
    $hood.Dispose()
    # Hood body below the cowl
    $g.FillEllipse($bForest, (U 20), (U 40), (U 60), (U 50))
    # Inner shadow under the cowl edge
    $g.FillEllipse($bDeep, (U 24), (U 44), (U 52), (U 16))

    # ── Face ─────────────────────────────────────────────────────────────────
    $g.FillEllipse($bFace, (U 27), (U 50), (U 46), (U 38))

    # ── Spectacles: two brass rings and a bridge, sitting ON the eyes ────────
    $eyeY = 62.0
    $ringR = 10.0
    $pen = New-Object System.Drawing.Pen($Copper, (U 3.2))
    foreach ($cx in @(39.5, 60.5)) {
        # Eye inside the ring
        $g.FillEllipse($bInk, (U ($cx - 5.5)), (U ($eyeY - 5.5)), (U 11), (U 11))
        $g.DrawEllipse($pen, (U ($cx - $ringR)), (U ($eyeY - $ringR)), (U ($ringR * 2)), (U ($ringR * 2)))
    }
    # Bridge
    $g.DrawLine($pen, (U 49.5), (U $eyeY), (U 50.5), (U $eyeY))
    $pen.Dispose()

    # Catchlights: the "calm, curious, knowing" part. Skipped below 32px, where
    # they turn into stray light pixels rather than a highlight.
    if ($Size -ge 32) {
        foreach ($cx in @(37.5, 58.5)) {
            $g.FillEllipse($bFace, (U $cx), (U ($eyeY - 3.4)), (U 3), (U 3))
        }
    }

    $g.Dispose()
    foreach ($b in @($bForest, $bDeep, $bSage, $bFace, $bInk)) { $b.Dispose() }
    return $bmp
}

# ── PNGs ─────────────────────────────────────────────────────────────────────
# App icon carries the dark field; tray icons are transparent so they sit on
# whatever the shell's tray background is.
foreach ($spec in @(
    @{n='hermaeus-app.png';        s=512; field=$true},
    @{n='hermaeus-tray.png';       s=256; field=$false},
    @{n='hermaeus-tray-dark.png';  s=256; field=$false},
    @{n='hermaeus-tray-light.png'; s=256; field=$false})) {
    $bmp = New-MossBitmap -Size $spec.s -WithField $spec.field
    $bmp.Save((Join-Path $Assets $spec.n), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "wrote $($spec.n) ($($spec.s)px, field=$($spec.field))"
}

# ── Multi-size ICO (System.Drawing cannot emit one, so write it by hand) ─────
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-MossBitmap -Size $sz -WithField $true
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
