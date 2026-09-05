<#
.SYNOPSIS
Rebuilds the Pandora PNG, ICO and review-sheet assets from their SVG sources.
.DESCRIPTION
Uses only Windows WPF and System.Drawing (no downloaded tools or fonts).
The intentionally small SVG subset is path, rect and circle with solid paint,
opacity and round caps/joins. Unsupported elements/attributes fail closed.
Run with Windows PowerShell 5.1 or PowerShell on Windows with WPF installed.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$brandDirectory = Join-Path $repository 'src\Pandora.App\Assets\Brand'
$reviewDirectory = Join-Path $repository 'screenshots'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$choices = @(
    @{ Stem = 'Pandora'; Name = 'Aperture'; Tag = 'DEFAULT'; Description = 'An open vessel. One guiding star.' },
    @{ Stem = 'Pandora-Selene'; Name = 'Selene'; Tag = 'OPTION 02'; Description = 'A sculpted crescent. Quiet, lunar.' },
    @{ Stem = 'Pandora-Aster'; Name = 'Aster'; Tag = 'OPTION 03'; Description = 'A central star. A shared orbit.' }
)

function Get-Number([Xml.XmlElement]$Element, [string]$Name, [double]$Default = 0) {
    if (-not $Element.HasAttribute($Name)) { return $Default }
    return [double]::Parse($Element.GetAttribute($Name), [Globalization.CultureInfo]::InvariantCulture)
}

function Get-Paint([Xml.XmlElement]$Element, [string]$Name) {
    $value = $Element.GetAttribute($Name)
    if (-not $value -or $value -eq 'none') { return $null }
    if ($value -notmatch '^#[0-9a-fA-F]{6}$') { throw "Only RGB hex paints are supported: $value" }
    $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($value))
    $brush.Opacity = Get-Number $Element "$Name-opacity" 1
    $brush.Freeze()
    return $brush
}

function Read-Vector([string]$Path) {
    # Disallow external XML resources even when a locally edited SVG is supplied.
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    } finally { $reader.Dispose() }
    if ($document.DocumentElement.GetAttribute('viewBox') -ne '0 0 256 256') {
        throw 'Pandora brand SVGs must use a 256-square viewBox.'
    }
    $drawing = [Windows.Media.DrawingGroup]::new()
    $context = $drawing.Open()
    try {
        foreach ($element in $document.DocumentElement.ChildNodes) {
            if ($element -isnot [Xml.XmlElement] -or $element.LocalName -in @('title', 'desc')) { continue }
            $allowed = @('fill', 'fill-opacity', 'stroke', 'stroke-opacity', 'stroke-width', 'stroke-linecap', 'stroke-linejoin')
            switch ($element.LocalName) {
                'path' { $allowed += 'd'; $geometry = [Windows.Media.Geometry]::Parse($element.GetAttribute('d')) }
                'rect' {
                    $allowed += @('x', 'y', 'width', 'height', 'rx')
                    $bounds = [Windows.Rect]::new((Get-Number $element 'x'), (Get-Number $element 'y'), (Get-Number $element 'width'), (Get-Number $element 'height'))
                    $radius = Get-Number $element 'rx'
                    $geometry = [Windows.Media.RectangleGeometry]::new($bounds, $radius, $radius)
                }
                'circle' {
                    $allowed += @('cx', 'cy', 'r')
                    $geometry = [Windows.Media.EllipseGeometry]::new([Windows.Point]::new((Get-Number $element 'cx'), (Get-Number $element 'cy')), (Get-Number $element 'r'), (Get-Number $element 'r'))
                }
                default { throw "Unsupported SVG element: $($element.LocalName)" }
            }
            foreach ($attribute in $element.Attributes) {
                if ($attribute.Name -notin $allowed) { throw "Unsupported SVG attribute: $($attribute.Name)" }
            }
            $pen = $null
            $stroke = Get-Paint $element 'stroke'
            if ($null -ne $stroke) {
                $pen = [Windows.Media.Pen]::new($stroke, (Get-Number $element 'stroke-width' 1))
                if ($element.GetAttribute('stroke-linecap') -eq 'round') {
                    $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
                    $pen.EndLineCap = [Windows.Media.PenLineCap]::Round
                }
                if ($element.GetAttribute('stroke-linejoin') -eq 'round') { $pen.LineJoin = [Windows.Media.PenLineJoin]::Round }
            }
            $context.DrawGeometry((Get-Paint $element 'fill'), $pen, $geometry)
        }
    } finally { $context.Close() }
    $drawing.Freeze()
    return $drawing
}

function Save-Drawing([Windows.Media.Drawing]$Drawing, [int]$Width, [int]$Height, [string]$Path, [double]$Scale = 1) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($Scale, $Scale))
    $context.DrawDrawing($Drawing)
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Width, $Height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($Path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

function Save-Icon([string]$Stem) {
    $frames = foreach ($size in $sizes) {
        @{ Size = $size; Bytes = [IO.File]::ReadAllBytes((Join-Path $brandDirectory "$Stem-$size.png")) }
    }
    $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $brandDirectory "$Stem.ico")))
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
}

function Draw-Label($Context, [string]$Text, [double]$X, [double]$Y, [double]$Size, [string]$Color = '#E9EEFF', [bool]$Bold = $false) {
    $typeface = [Windows.Media.Typeface]::new('Segoe UI')
    if ($Bold) { $typeface = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new('Segoe UI'), [Windows.FontStyles]::Normal, [Windows.FontWeights]::SemiBold, [Windows.FontStretches]::Normal) }
    $label = [Windows.Media.FormattedText]::new($Text, [Globalization.CultureInfo]::InvariantCulture, [Windows.FlowDirection]::LeftToRight, $typeface, $Size, [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($Color)), 1.0)
    $Context.DrawText($label, [Windows.Point]::new($X, $Y))
}

$vectors = @{}
foreach ($choice in $choices) {
    $vector = Read-Vector (Join-Path $brandDirectory "$($choice.Stem).svg")
    $vectors[$choice.Stem] = $vector
    foreach ($size in $sizes) {
        Save-Drawing $vector $size $size (Join-Path $brandDirectory "$($choice.Stem)-$size.png") ($size / 256.0)
    }
    Save-Drawing $vector 256 256 (Join-Path $brandDirectory "$($choice.Stem).png")
    Save-Icon $choice.Stem
}

# A readable human review sheet: three distinct options and native-size samples
# on both dark and light surfaces. No generated text is embedded in app icons.
$sheet = [Windows.Media.DrawingGroup]::new()
$sheetContext = $sheet.Open()
try {
    $sheetContext.DrawRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#080E1C'), $null, [Windows.Rect]::new(0, 0, 1440, 840))
    Draw-Label $sheetContext 'Pandora' 72 48 46 '#F3F5FF' $true
    Draw-Label $sheetContext 'ICON EXPLORATIONS' 73 111 12 '#98A9EC' $true
    Draw-Label $sheetContext 'Celestial. Greek-inspired. Designed to stay clear at desktop scale.' 72 143 19 '#AEB9D3'
    for ($i = 0; $i -lt $choices.Count; $i++) {
        $choice = $choices[$i]
        $x = 72 + $i * 448
        if ($i -gt 0) {
            $sheetContext.DrawLine([Windows.Media.Pen]::new([Windows.Media.BrushConverter]::new().ConvertFromString('#273148'), 1), [Windows.Point]::new($x - 28, 230), [Windows.Point]::new($x - 28, 744))
        }
        Draw-Label $sheetContext $choice.Tag $x 221 12 '#98A9EC' $true
        $sheetContext.PushTransform([Windows.Media.TranslateTransform]::new($x + 74, 258))
        $sheetContext.DrawDrawing($vectors[$choice.Stem])
        $sheetContext.Pop()
        Draw-Label $sheetContext $choice.Name $x 534 29 '#F3F5FF' $true
        Draw-Label $sheetContext $choice.Description $x 577 17 '#AEB9D3'
        $sheetContext.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#111A2E'), $null, [Windows.Rect]::new($x, 622, 388, 56), 10, 10)
        $sheetContext.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString('#ECEEF4'), $null, [Windows.Rect]::new($x, 690, 388, 56), 10, 10)
        foreach ($rowY in @(622, 690)) {
            Draw-Label $sheetContext '16 / 24 / 32 / 48 px' ($x + 18) ($rowY + 19) 12 $(if ($rowY -eq 622) { '#AEB9D3' } else { '#465370' })
            $offsetX = $x + 172
            foreach ($size in @(16, 24, 32, 48)) {
                $sheetContext.PushTransform([Windows.Media.TranslateTransform]::new($offsetX, $rowY + (56 - $size) / 2))
                $sheetContext.PushTransform([Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
                $sheetContext.DrawDrawing($vectors[$choice.Stem])
                $sheetContext.Pop(); $sheetContext.Pop()
                $offsetX += $size + 14
            }
        }
    }
    Draw-Label $sheetContext 'Native SVG sources  /  Transparent PNGs  /  Seven-size Windows ICOs  /  No network dependencies' 72 790 14 '#7E8BA9'
} finally { $sheetContext.Close() }
Save-Drawing $sheet 1440 840 (Join-Path $reviewDirectory 'pandora-icon-options.png')
Write-Output "Generated three Pandora icon sets and screenshots/pandora-icon-options.png."
