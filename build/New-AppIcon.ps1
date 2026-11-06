param(
    [string] $SourceSvg = (Join-Path $PSScriptRoot '..\assets\klarseal-mark-square-white-on-black.svg'),
    [string] $Output = (Join-Path $PSScriptRoot '..\src\SignMyApp.App\Assets\klarseal.ico')
)

# Authoring only. WPF Geometry accepts this SVG's path grammar; no SVG library or compilation.
# Run with powershell -NoProfile -STA -File build/New-AppIcon.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
[xml] $svg = Get-Content -LiteralPath $SourceSvg -Raw
$path = $svg.DocumentElement.SelectSingleNode('*[local-name()="path"]')
if ($svg.DocumentElement.viewBox -ne '0 0 1000 1000' -or
    $path.transform -ne 'translate(368.235 180) scale(3.764706)') {
    throw 'Unexpected brand SVG: review its viewBox and transform before generating.'
}
$geometry = [System.Windows.Media.Geometry]::Parse($path.d).Clone()
$geometry.Transform = [System.Windows.Media.MatrixTransform]::new(3.764706, 0, 0, 3.764706, 368.235, 180)
$frames = @()
foreach ($size in @(16, 24, 32, 48, 256)) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 1000.0, $size / 1000.0))
    $drawing.DrawRectangle([System.Windows.Media.Brushes]::Black, $null, [System.Windows.Rect]::new(0, 0, 1000, 1000))
    $drawing.DrawGeometry([System.Windows.Media.Brushes]::White, $null, $geometry)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new()
    $encoder.Save($memory)
    $frames += @{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose()
}
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Output))) | Out-Null
$stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Output))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $encodedSize = $frame.Size % 256
        $writer.Write([byte] $encodedSize)
        $writer.Write([byte] $encodedSize)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $frame.Bytes.Length)
        $writer.Write([uint32] $offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]] $frame.Bytes) }
}
finally { $writer.Dispose(); $stream.Dispose() }
Write-Output "Generated $Output (16, 24, 32, 48, 256 px)"
