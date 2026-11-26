# Generates docs/og.png (1200x630) used for link previews.
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$w = 1200; $h = 630
$bmp = New-Object Drawing.Bitmap $w, $h
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$bg = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Rectangle 0, 0, $w, $h), ([Drawing.Color]::FromArgb(255, 10, 19, 23)), ([Drawing.Color]::FromArgb(255, 17, 40, 48)), 35
$g.FillRectangle($bg, 0, 0, $w, $h)

$icon = [Drawing.Image]::FromFile("$root\docs\icon.png")
$g.DrawImage($icon, 80, 80, 96, 96)
$white = [Drawing.Brushes]::White
$dim = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 155, 174, 181))
$f1 = New-Object Drawing.Font 'Segoe UI Semibold', 40, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$f2 = New-Object Drawing.Font 'Segoe UI Semibold', 66, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$f3 = New-Object Drawing.Font 'Segoe UI', 30, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$g.DrawString('PulseCheck', $f1, $white, 196, 98)
$fmt = New-Object Drawing.StringFormat
$rect = New-Object Drawing.RectangleF 80, 230, 1040, 230
$g.DrawString("Check your new PC before the return window closes.", $f2, $white, $rect, $fmt)
$g.DrawString("Free  ·  Open source  ·  100 % local  ·  Windows 10 and 11", $f3, $dim, 80, 520)
$g.Dispose()
$bmp.Save("$root\docs\og.png", [Drawing.Imaging.ImageFormat]::Png)
"og.png ok"
