$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path (Split-Path -Parent $PSScriptRoot) "src\PKiller.App\Assets"

function New-RoundedRectanglePath([single] $x, [single] $y, [single] $width, [single] $height, [single] $radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-PKillerIcon($graphics, [single] $x, [single] $y, [single] $size) {
    $state = $graphics.Save()
    $graphics.TranslateTransform($x, $y)
    $graphics.ScaleTransform($size / 512, $size / 512)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#1F2937"))
    $barOne = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#9CA3AF"))
    $barTwo = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#6B7280"))
    $red = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#E24B4A"))
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 20)
    $white.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paths = @(
        (New-RoundedRectanglePath 0 0 512 512 112),
        (New-RoundedRectanglePath 80 96 340 48 12),
        (New-RoundedRectanglePath 80 192 240 48 12)
    )
    $graphics.FillPath($background, $paths[0])
    $graphics.FillPath($barOne, $paths[1])
    $graphics.FillPath($barTwo, $paths[2])
    $graphics.FillEllipse($red, 268, 272, 192, 192)
    $graphics.DrawLine($white, 328, 332, 400, 404)
    $graphics.DrawLine($white, 400, 332, 328, 404)
    foreach ($path in $paths) { $path.Dispose() }
    $background.Dispose()
    $barOne.Dispose()
    $barTwo.Dispose()
    $red.Dispose()
    $white.Dispose()
    $graphics.Restore($state)
}

function New-Png([string] $path, [int] $width, [int] $height, [scriptblock] $draw) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    & $draw $graphics
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Draw-PKillerVerticalLogo($graphics, [int] $width, [int] $height) {
    $state = $graphics.Save()
    $graphics.ScaleTransform($width / 240, $height / 280)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#1F2937"))
    $barOne = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#9CA3AF"))
    $barTwo = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#6B7280"))
    $red = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#E24B4A"))
    $white = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 4.5)
    $white.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paths = @(
        (New-RoundedRectanglePath 60 30 120 120 24),
        (New-RoundedRectanglePath 78 51 84 12 3),
        (New-RoundedRectanglePath 78 75 60 12 3)
    )
    $graphics.FillPath($background, $paths[0])
    $graphics.FillPath($barOne, $paths[1])
    $graphics.FillPath($barTwo, $paths[2])
    $graphics.FillEllipse($red, 126, 99, 48, 48)
    $graphics.DrawLine($white, 141, 114, 159, 132)
    $graphics.DrawLine($white, 159, 114, 141, 132)
    $font = [System.Drawing.Font]::new("Segoe UI", 44, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $taglineFont = [System.Drawing.Font]::new("Segoe UI", 16, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.Trimming = [System.Drawing.StringTrimming]::None
    $format.Alignment = [System.Drawing.StringAlignment]::Near
    $pWidth = $graphics.MeasureString("P", $font, 1000, [System.Drawing.StringFormat]::GenericTypographic).Width
    $killerWidth = $graphics.MeasureString("Killer", $font, 1000, [System.Drawing.StringFormat]::GenericTypographic).Width
    $start = (240 - $pWidth - $killerWidth) / 2
    $graphics.DrawString("P", $font, $barTwo, [single] $start, [single] 157, [System.Drawing.StringFormat]::GenericTypographic)
    $graphics.DrawString("Killer", $font, $red, [single] ($start + $pWidth), [single] 157, [System.Drawing.StringFormat]::GenericTypographic)
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString("Stop any process", $taglineFont, $barTwo, [single] 120, [single] 217, $format)
    foreach ($path in $paths) { $path.Dispose() }
    $background.Dispose()
    $barOne.Dispose()
    $barTwo.Dispose()
    $red.Dispose()
    $white.Dispose()
    $font.Dispose()
    $taglineFont.Dispose()
    $format.Dispose()
    $graphics.Restore($state)
}

New-Png (Join-Path $assets "Square44x44Logo.png") 44 44 {
    param($graphics)
    Draw-PKillerIcon $graphics 0 0 44
}
New-Png (Join-Path $assets "Square150x150Logo.png") 150 150 {
    param($graphics)
    Draw-PKillerIcon $graphics 0 0 150
}
New-Png (Join-Path $assets "StoreLogo.png") 50 50 {
    param($graphics)
    Draw-PKillerIcon $graphics 0 0 50
}
New-Png (Join-Path $assets "Wide310x150Logo.png") 310 150 {
    param($graphics)
    Draw-PKillerIcon $graphics 0 0 150
}
New-Png (Join-Path $assets "PKillerLogoVertical.png") 240 280 {
    param($graphics)
    Draw-PKillerVerticalLogo $graphics 240 280
}
New-Png (Join-Path $assets "PKillerIcon256.png") 256 256 {
    param($graphics)
    Draw-PKillerIcon $graphics 0 0 256
}

$iconPath = Join-Path $assets "PKillerIcon256.png"
$iconBitmap = [System.Drawing.Bitmap]::new($iconPath)
$iconWidth = $iconBitmap.Width
$iconHeight = $iconBitmap.Height
$iconRect = [System.Drawing.Rectangle]::new(0, 0, $iconBitmap.Width, $iconBitmap.Height)
$locked = $iconBitmap.LockBits(
    $iconRect,
    [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$rowLength = $iconBitmap.Width * 4
$xor = [byte[]]::new($rowLength * $iconBitmap.Height)
$row = [byte[]]::new($rowLength)
for ($y = 0; $y -lt $iconBitmap.Height; $y++) {
    [System.Runtime.InteropServices.Marshal]::Copy(
        [IntPtr]::Add($locked.Scan0, $y * $locked.Stride),
        $row,
        0,
        $rowLength)
    [Array]::Copy($row, 0, $xor, ($iconBitmap.Height - 1 - $y) * $rowLength, $rowLength)
}
$iconBitmap.UnlockBits($locked)
$iconBitmap.Dispose()
$andMask = [byte[]]::new((([int]([Math]::Ceiling($iconWidth / 32.0)) * 4) * $iconHeight))
$payloadLength = 40 + $xor.Length + $andMask.Length
$iconStream = [System.IO.File]::Create((Join-Path $assets "PKillerIcon.ico"))
$writer = [System.IO.BinaryWriter]::new($iconStream)
$writer.Write([UInt16] 0)
$writer.Write([UInt16] 1)
$writer.Write([UInt16] 1)
$writer.Write([byte] 0)
$writer.Write([byte] 0)
$writer.Write([byte] 0)
$writer.Write([byte] 0)
$writer.Write([UInt16] 1)
$writer.Write([UInt16] 32)
$writer.Write([UInt32] $payloadLength)
$writer.Write([UInt32] 22)
$writer.Write([UInt32] 40)
$writer.Write([Int32] $iconWidth)
$writer.Write([Int32] ($iconHeight * 2))
$writer.Write([UInt16] 1)
$writer.Write([UInt16] 32)
$writer.Write([UInt32] 0)
$writer.Write([UInt32] $xor.Length)
$writer.Write([Int32] 0)
$writer.Write([Int32] 0)
$writer.Write([UInt32] 0)
$writer.Write([UInt32] 0)
$writer.Write($xor)
$writer.Write($andMask)
$writer.Dispose()
$iconStream.Dispose()
Remove-Item $iconPath
