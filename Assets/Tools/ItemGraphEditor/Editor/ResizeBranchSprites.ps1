# Technical resize of existing artwork, not image generation. Originals remain untouched.
param(
 [Parameter(Mandatory=$true)][string]$BranchSource,
 [Parameter(Mandatory=$true)][string]$LargeBranchSource
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetsRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Save-FittedSprite([string]$source, [string]$destination, [int]$width, [int]$height) {
 $inputImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $source).Path)
 try {
  $left=$inputImage.Width; $top=$inputImage.Height; $right=-1; $bottom=-1
  for ($y=0; $y -lt $inputImage.Height; $y++) {
   for ($x=0; $x -lt $inputImage.Width; $x++) {
    if ($inputImage.GetPixel($x,$y).A -lt 128) { continue }
    $left=[Math]::Min($left,$x); $right=[Math]::Max($right,$x)
    $top=[Math]::Min($top,$y); $bottom=[Math]::Max($bottom,$y)
   }
  }
  if ($right -lt $left) { throw "No visible artwork in $source" }
  $sourceWidth=$right-$left+1; $sourceHeight=$bottom-$top+1
  $scale=[Math]::Min(($width-4)/$sourceWidth,($height-4)/$sourceHeight)
  $drawWidth=[Math]::Max(1,[int][Math]::Round($sourceWidth*$scale))
  $drawHeight=[Math]::Max(1,[int][Math]::Round($sourceHeight*$scale))
  $outputImage=[System.Drawing.Bitmap]::new($width,$height,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
   # Explicit nearest-neighbour sampling preserves the existing pixel-art palette/alpha.
   $offsetX=[int][Math]::Floor(($width-$drawWidth)/2)
   $offsetY=[int][Math]::Floor(($height-$drawHeight)/2)
   for ($y=0; $y -lt $drawHeight; $y++) {
    for ($x=0; $x -lt $drawWidth; $x++) {
     $sx=$left+[Math]::Min($sourceWidth-1,[int][Math]::Floor(($x+.5)*$sourceWidth/$drawWidth))
     $sy=$top+[Math]::Min($sourceHeight-1,[int][Math]::Floor(($y+.5)*$sourceHeight/$drawHeight))
     $outputImage.SetPixel($offsetX+$x,$offsetY+$y,$inputImage.GetPixel($sx,$sy))
    }
   }
   $outputImage.Save($destination,[System.Drawing.Imaging.ImageFormat]::Png)
  } finally { $outputImage.Dispose() }
 } finally { $inputImage.Dispose() }
}
foreach ($entry in @(@('branch',$BranchSource),@('large_branch',$LargeBranchSource))) {
 Save-FittedSprite $entry[1] (Join-Path $assetsRoot "Art/UI/Icons/$($entry[0]).png") 32 32
 Save-FittedSprite $entry[1] (Join-Path $assetsRoot "Art/UI/Icons/$($entry[0])_world.png") 32 16
}
