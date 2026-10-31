param([Parameter(Mandatory=$true)][string]$StickSource,
      [Parameter(Mandatory=$true)][string]$TorchSource,
      [Parameter(Mandatory=$true)][string]$LitSource)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -AssemblyName System.Drawing
function Save-Sprite([string]$source,[string]$name,[int]$width,[int]$height) {
    $inputImage=[System.Drawing.Bitmap]::FromFile($source)
    try {
        $left=$inputImage.Width; $top=$inputImage.Height; $right=-1; $bottom=-1
        for($y=0;$y -lt $inputImage.Height;$y++) {
            for($x=0;$x -lt $inputImage.Width;$x++) {
                if($inputImage.GetPixel($x,$y).A -ge 128) {
                    $left=[Math]::Min($left,$x);$right=[Math]::Max($right,$x)
                    $top=[Math]::Min($top,$y);$bottom=[Math]::Max($bottom,$y)
                }
            }
        }
        if($right -lt $left) {throw 'Generated image is empty.'}
        $cw=$right-$left+1;$ch=$bottom-$top+1
        $fit=[Math]::Min(($width-2)/[double]$cw,($height-2)/[double]$ch)
        $dw=[Math]::Max(1,[int][Math]::Round($cw*$fit));$dh=[Math]::Max(1,[int][Math]::Round($ch*$fit))
        $ox=[int][Math]::Floor(($width-$dw)/2);$oy=[int][Math]::Floor(($height-$dh)/2)
        $output=[System.Drawing.Bitmap]::new($width,$height,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for($y=0;$y -lt $dh;$y++) {for($x=0;$x -lt $dw;$x++) {
                $sx=$left+[int][Math]::Floor(($x+.5)*$cw/$dw);$sy=$top+[int][Math]::Floor(($y+.5)*$ch/$dh)
                $color=$inputImage.GetPixel($sx,$sy)
                if($color.A -ge 128) {$output.SetPixel($ox+$x,$oy+$y,[System.Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B))}
            }}
            $output.Save((Join-Path $root ('Art/UI/Icons/'+$name+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally {$output.Dispose()}
    } finally {$inputImage.Dispose()}
}
Save-Sprite $StickSource 'LargeStick' 32 32
Save-Sprite $StickSource 'LargeStick_world' 32 16
Save-Sprite $TorchSource 'LargeTorch' 32 32
Save-Sprite $TorchSource 'LargeTorch_world' 32 64
Save-Sprite $LitSource 'IgnitedLargeTorch' 32 32
Save-Sprite $LitSource 'IgnitedLargeTorch_world' 32 64
