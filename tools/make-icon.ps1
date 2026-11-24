# Generates src/Assets/app.ico (neon bear-paw "M" badge). Run once; output is committed.
Add-Type -AssemblyName System.Drawing
$dst = Join-Path $PSScriptRoot '..\src\Assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  # Draw on a common grid so the bear stays recognizable at tray sizes.
  $g.ScaleTransform(($s / 100.0), ($s / 100.0))
  $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#17162D'))
  $light = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F1EFFF'))
  $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
    [Drawing.Rectangle]::new(5, 8, 90, 85),
    [Drawing.ColorTranslator]::FromHtml('#67EDFC'),
    [Drawing.ColorTranslator]::FromHtml('#D77BFF'), 35)
  $g.FillEllipse($gradient, 4, 9, 31, 31)
  $g.FillEllipse($gradient, 65, 9, 31, 31)
  $g.FillEllipse($ink, 11, 16, 17, 17)
  $g.FillEllipse($ink, 72, 16, 17, 17)
  $g.FillEllipse($gradient, 9, 16, 82, 78)
  $g.FillEllipse($ink, 16, 23, 68, 63)
  $g.FillEllipse($light, 29, 39, 10, 12)
  $g.FillEllipse($light, 61, 39, 10, 12)
  $g.FillEllipse($gradient, 32, 55, 36, 23)
  $nose = [Drawing.Point[]]@([Drawing.Point]::new(42,60), [Drawing.Point]::new(58,60), [Drawing.Point]::new(50,68))
  $g.FillPolygon($ink, $nose)
  $ink.Dispose()
  $light.Dispose()
  $gradient.Dispose()
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  , $ms.ToArray()
}
$fs = [System.IO.File]::Create($dst); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
"ok $dst"
