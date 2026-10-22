# Generates src/Assets/app.ico (neon bear-paw "M" badge). Run once; output is committed.
Add-Type -AssemblyName System.Drawing
$dst = Join-Path $PSScriptRoot '..\src\Assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
  $r = New-Object System.Drawing.RectangleF 0, 0, $s, $s
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $rad = $s * 0.28
  $path.AddArc(0, 0, $rad, $rad, 180, 90); $path.AddArc($s - $rad - 1, 0, $rad, $rad, 270, 90)
  $path.AddArc($s - $rad - 1, $s - $rad - 1, $rad, $rad, 0, 90); $path.AddArc(0, $s - $rad - 1, $rad, $rad, 90, 90)
  $path.CloseFigure()
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(255, 162, 89, 255)), ([System.Drawing.Color]::FromArgb(255, 255, 46, 151)), 45
  $g.FillPath($br, $path)
  # bear ears
  $ear = [System.Drawing.Color]::FromArgb(235, 15, 15, 26)
  $eb = New-Object System.Drawing.SolidBrush $ear
  $g.FillEllipse($eb, $s * 0.17, $s * 0.14, $s * 0.22, $s * 0.22)
  $g.FillEllipse($eb, $s * 0.61, $s * 0.14, $s * 0.22, $s * 0.22)
  $g.FillEllipse($eb, $s * 0.18, $s * 0.24, $s * 0.64, $s * 0.6)
  $font = New-Object System.Drawing.Font 'Segoe UI', ($s * 0.36), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
  $tr = New-Object System.Drawing.RectangleF 0, ($s * 0.1), $s, $s
  $g.DrawString('M', $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 120, 200))), $tr, $sf)
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
