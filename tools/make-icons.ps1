# Regenerates assets/pk2-mixer.png and assets/pk2-mixer.ico from code.
# Requires Windows PowerShell/.NET Framework System.Drawing (run on Windows).
param(
    [string]$Assets = "$PSScriptRoot\..\assets"
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Assets | Out-Null

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 256.0
    $margin = 8 * $scale
    $radius = 46 * $scale
    $rect = [System.Drawing.RectangleF]::new($margin, $margin,
        ($size - 2 * $margin), ($size - 2 * $margin))
    $path = New-RoundedRect $rect.X $rect.Y $rect.Width $rect.Height $radius

    $bgTop = [System.Drawing.Color]::FromArgb(255, 32, 50, 74)
    $bgBottom = [System.Drawing.Color]::FromArgb(255, 15, 23, 34)
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, $bgTop, $bgBottom, 90)
    $g.FillPath($bg, $path)

    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 90, 150, 230),
        [Math]::Max(2, 5 * $scale))
    $g.DrawPath($border, $path)

    # archive box
    $bx = 56 * $scale
    $by = 74 * $scale
    $bw = 144 * $scale
    $bh = 112 * $scale
    $box = [System.Drawing.RectangleF]::new($bx, $by, $bw, $bh)
    $goldTop = [System.Drawing.Color]::FromArgb(255, 240, 197, 96)
    $goldBottom = [System.Drawing.Color]::FromArgb(255, 186, 134, 46)
    $gold = [System.Drawing.Drawing2D.LinearGradientBrush]::new($box, $goldTop, $goldBottom, 90)
    $g.FillRectangle($gold, $box)

    $lid = [System.Drawing.RectangleF]::new($bx, $by, $bw, 28 * $scale)
    $lidBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 158, 108, 32))
    $g.FillRectangle($lidBrush, $lid)

    $ink = [System.Drawing.Color]::FromArgb(255, 20, 30, 44)
    $inkBrush = [System.Drawing.SolidBrush]::new($ink)
    $boxPen = [System.Drawing.Pen]::new($ink, [Math]::Max(2, 5 * $scale))
    $g.DrawRectangle($boxPen, $bx, $by, $bw, $bh)
    $g.DrawLine($boxPen, $bx, $by + 28 * $scale, $bx + $bw, $by + 28 * $scale)

    # slot details on the lid
    $slotW = 34 * $scale
    $slotH = 8 * $scale
    $g.FillRectangle($inkBrush, $bx + ($bw - $slotW) / 2, $by + 10 * $scale, $slotW, $slotH)

    # PK2 label
    $fontSize = [Math]::Max(10, 46 * $scale)
    $font = [System.Drawing.Font]::new('Segoe UI', $fontSize,
        [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $labelRect = [System.Drawing.RectangleF]::new($bx, $by + 28 * $scale, $bw, ($bh - 28 * $scale))
    $g.DrawString('PK2', $font, $inkBrush, $labelRect, $format)

    $font.Dispose()
    $format.Dispose()
    $bg.Dispose()
    $gold.Dispose()
    $lidBrush.Dispose()
    $inkBrush.Dispose()
    $boxPen.Dispose()
    $border.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

# PNG (used by the app window, Linux desktop entry and AppImage)
$png = New-IconBitmap 256
$pngPath = Join-Path $Assets 'pk2-mixer.png'
$png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Host "wrote $pngPath"

# ICO with PNG-compressed entries (16..256), used for the Windows executable
$sizes = 16, 24, 32, 48, 64, 128, 256
$payloads = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += ,$ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

$icoPath = Join-Path $Assets 'pk2-mixer.ico'
$fs = [System.IO.FileStream]::new($icoPath, [System.IO.FileMode]::Create)
$bw = [System.IO.BinaryWriter]::new($fs)
$bw.Write([uint16]0)            # reserved
$bw.Write([uint16]1)            # type: icon
$bw.Write([uint16]$sizes.Count) # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([byte]0)          # palette colours
    $bw.Write([byte]0)          # reserved
    $bw.Write([uint16]1)        # colour planes
    $bw.Write([uint16]32)       # bits per pixel
    $bw.Write([uint32]$payloads[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $payloads[$i].Length
}
foreach ($payload in $payloads) {
    $bw.Write($payload)
}
$bw.Dispose()
$fs.Dispose()
Write-Host "wrote $icoPath"
