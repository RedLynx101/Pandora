$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$brandDir = Join-Path $root "src\OrbitDock.App\Assets\Brand"
New-Item -ItemType Directory -Path $brandDir -Force | Out-Null

function New-BrandBitmap {
    param(
        [int]$Size,
        [string]$Path
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
    $discInset = [Math]::Max(2, [int]($Size * 0.08))
    $discRect = New-Object System.Drawing.Rectangle $discInset, $discInset, ($Size - ($discInset * 2)), ($Size - ($discInset * 2))
    $discBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(128, 7, 13, 23))
    $graphics.FillEllipse($discBrush, $discRect)
    $discBrush.Dispose()

    $pad = [Math]::Max(2, [int]($Size * 0.10))
    $outer = New-Object System.Drawing.Rectangle $pad, $pad, ($Size - ($pad * 2)), ($Size - ($pad * 2))
    $cyanPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 76, 214, 255)), ([Math]::Max(2, $Size * 0.055))
    $violetPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 158, 137, 255)), ([Math]::Max(2, $Size * 0.040))
    $goldPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 242, 201, 76)), ([Math]::Max(1, $Size * 0.025))
    $graphics.DrawArc($cyanPen, $outer, 205, 250)
    $inner = New-Object System.Drawing.Rectangle ($pad * 2), ($pad * 2), ($Size - ($pad * 4)), ($Size - ($pad * 4))
    $graphics.DrawArc($violetPen, $inner, 20, 250)
    $tiny = New-Object System.Drawing.Rectangle ([int]($Size * 0.30)), ([int]($Size * 0.30)), ([int]($Size * 0.40)), ([int]($Size * 0.40))
    $graphics.DrawArc($goldPen, $tiny, 115, 270)

    $cellBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(238, 238, 246, 255))
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
    $cellSize = [Math]::Max(5, [int]($Size * 0.16))
    $gap = [Math]::Max(2, [int]($Size * 0.045))
    $startX = [int](($Size - ($cellSize * 2) - $gap) / 2)
    $startY = [int](($Size - ($cellSize * 2) - $gap) / 2)
    foreach ($row in 0..1) {
        foreach ($col in 0..1) {
            $x = $startX + ($col * ($cellSize + $gap))
            $y = $startY + ($row * ($cellSize + $gap))
            [RoundedRectangleExtensions]::FillRoundedRectangle($graphics, $shadowBrush, ([System.Drawing.Rectangle]::new(($x + 1), ($y + 2), $cellSize, $cellSize)), [Math]::Max(2, $Size * 0.025))
            [RoundedRectangleExtensions]::FillRoundedRectangle($graphics, $cellBrush, ([System.Drawing.Rectangle]::new($x, $y, $cellSize, $cellSize)), [Math]::Max(2, $Size * 0.025))
        }
    }

    $accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 214, 255))
    $graphics.FillEllipse($accentBrush, ([int]($Size * 0.67)), ([int]($Size * 0.18)), ([int]($Size * 0.11)), ([int]($Size * 0.11)))

    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

Add-Type @"
using System.Drawing;
using System.Drawing.Drawing2D;
public static class RoundedRectangleExtensions {
    public static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle bounds, float radius) {
        using (GraphicsPath path = new GraphicsPath()) {
            float diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.FillPath(brush, path);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing -ErrorAction SilentlyContinue

$png256 = Join-Path $brandDir "OrbitDock.png"
New-BrandBitmap -Size 256 -Path $png256
New-BrandBitmap -Size 64 -Path (Join-Path $brandDir "OrbitDock-64.png")
New-BrandBitmap -Size 32 -Path (Join-Path $brandDir "OrbitDock-32.png")

$svg = @'
<svg width="256" height="256" viewBox="0 0 256 256" fill="none" xmlns="http://www.w3.org/2000/svg">
  <circle cx="128" cy="128" r="108" fill="#090E16" fill-opacity="0.5"/>
  <path d="M46 152C62 65 157 24 213 72" stroke="#56D6FF" stroke-width="14" stroke-linecap="round"/>
  <path d="M210 101C190 187 95 228 42 176" stroke="#9F8CFF" stroke-width="10" stroke-linecap="round"/>
  <path d="M82 91H116V125H82V91Z" fill="#EEF6FF"/>
  <path d="M138 91H172V125H138V91Z" fill="#EEF6FF"/>
  <path d="M82 146H116V180H82V146Z" fill="#EEF6FF"/>
  <path d="M138 146H172V180H138V146Z" fill="#EEF6FF"/>
  <circle cx="185" cy="62" r="12" fill="#F2C94C"/>
</svg>
'@
Set-Content -LiteralPath (Join-Path $brandDir "OrbitDock.svg") -Value $svg -Encoding UTF8

$iconPath = Join-Path $brandDir "OrbitDock.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngFrames = @()
foreach ($size in $sizes) {
    $framePath = Join-Path $brandDir "OrbitDock-$size.png"
    New-BrandBitmap -Size $size -Path $framePath
    $pngFrames += [PSCustomObject]@{ Size = $size; Bytes = [System.IO.File]::ReadAllBytes($framePath) }
}

$stream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$pngFrames.Count)
$offset = 6 + (16 * $pngFrames.Count)
foreach ($frame in $pngFrames) {
    $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
    $writer.Write([byte]($(if ($frame.Size -eq 256) { 0 } else { $frame.Size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $pngFrames) {
    $writer.Write($frame.Bytes)
}
$writer.Dispose()
$stream.Dispose()

Write-Host "Generated OrbitDock brand assets in $brandDir"
