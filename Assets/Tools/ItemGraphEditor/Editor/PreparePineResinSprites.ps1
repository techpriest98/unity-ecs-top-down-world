param(
    [Parameter(Mandatory=$true)][string]$CutSource,
    [Parameter(Mandatory=$true)][string]$ResinSource,
    [Parameter(Mandatory=$true)][string]$ItemSource,
    [Parameter(Mandatory=$true)][string]$AssetsRoot
)

Add-Type -AssemblyName System.Drawing

function Save-FittedSprite([string]$Source, [string]$Destination, [int]$Width, [int]$Height) {
    $inputImage = [System.Drawing.Bitmap]::FromFile($Source)
    try {
        $left = $inputImage.Width; $top = $inputImage.Height; $right = -1; $bottom = -1
        for ($y = 0; $y -lt $inputImage.Height; $y++) {
            for ($x = 0; $x -lt $inputImage.Width; $x++) {
                if ($inputImage.GetPixel($x, $y).A -gt 12) {
                    if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
                    if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
                }
            }
        }
        if ($right -lt $left -or $bottom -lt $top) { throw "No opaque pixels in $Source" }
        $cropWidth = $right - $left + 1; $cropHeight = $bottom - $top + 1
        $fit = [Math]::Min(($Width - 2) / [double]$cropWidth, ($Height - 2) / [double]$cropHeight)
        $drawWidth = [Math]::Max(1, [int][Math]::Round($cropWidth * $fit))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($cropHeight * $fit))
        $offsetX = [int][Math]::Floor(($Width - $drawWidth) / 2)
        $offsetY = [int][Math]::Floor(($Height - $drawHeight) / 2)
        $output = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for ($y = 0; $y -lt $drawHeight; $y++) {
                $sy = $top + [int][Math]::Floor(($y + .5) * $cropHeight / $drawHeight)
                for ($x = 0; $x -lt $drawWidth; $x++) {
                    $sx = $left + [int][Math]::Floor(($x + .5) * $cropWidth / $drawWidth)
                    $output.SetPixel($offsetX + $x, $offsetY + $y, $inputImage.GetPixel($sx, $sy))
                }
            }
            $output.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $output.Dispose() }
    } finally { $inputImage.Dispose() }
}

$iconDir = Join-Path $AssetsRoot 'Art/UI/Icons'
$resourceDir = Join-Path $AssetsRoot 'Resources'
Save-FittedSprite $ItemSource (Join-Path $iconDir 'PineResin.png') 32 32
Save-FittedSprite $ItemSource (Join-Path $iconDir 'PineResin_world.png') 32 16
Save-FittedSprite $CutSource (Join-Path $resourceDir 'TreeBarkCutOverlay.png') 16 24
Save-FittedSprite $ResinSource (Join-Path $resourceDir 'TreeBarkResinOverlay.png') 16 24
