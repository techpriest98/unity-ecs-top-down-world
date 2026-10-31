param([Parameter(Mandatory=$true)][string]$UnlitSource,
      [Parameter(Mandatory=$true)][string]$LitSource)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -AssemblyName System.Drawing
$unlit=[System.Drawing.Bitmap]::FromFile($UnlitSource)
$lit=[System.Drawing.Bitmap]::FromFile($LitSource)
try {
    $left=$unlit.Width; $top=$unlit.Height; $right=-1; $bottom=-1
    for($y=0;$y -lt $unlit.Height;$y++) {for($x=0;$x -lt $unlit.Width;$x++) {
        if($unlit.GetPixel($x,$y).A -ge 128) {
            $left=[Math]::Min($left,$x);$right=[Math]::Max($right,$x)
            $top=[Math]::Min($top,$y);$bottom=[Math]::Max($bottom,$y)
        }
    }}
    if($bottom -lt $top){throw 'Empty unlit reference.'}
    # Register BOTH states to the unlit body. Independent alpha crops would
    # shrink/move the lit wood body to make room for its additional flame.
    $scale=48.0/($bottom-$top+1)
    $centre=($left+$right+1)/2.0
    foreach($spec in @(@($unlit,'LargeTorch'),@($lit,'IgnitedLargeTorch'))) {
        $source=$spec[0];$name=$spec[1]
        $output=[System.Drawing.Bitmap]::new(32,64,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            for($y=0;$y -lt 64;$y++){for($x=0;$x -lt 32;$x++) {
                $referenceX=$centre+($x+.5-16)/$scale
                $referenceY=$top+($y+.5-15)/$scale
                $sx=[int][Math]::Floor($referenceX*$source.Width/$unlit.Width)
                $sy=[int][Math]::Floor($referenceY*$source.Height/$unlit.Height)
                if($sx -lt 0 -or $sy -lt 0 -or $sx -ge $source.Width -or $sy -ge $source.Height){continue}
                $color=$source.GetPixel($sx,$sy)
                if($color.A -ge 128){$output.SetPixel($x,$y,[System.Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B))}
            }}
            $output.Save((Join-Path $root ('Art/UI/Icons/'+$name+'_world.png')),[System.Drawing.Imaging.ImageFormat]::Png)
            $icon=[System.Drawing.Bitmap]::new(32,32,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                for($y=0;$y -lt 32;$y++){for($x=0;$x -lt 16;$x++){
                    $icon.SetPixel($x+8,$y,$output.GetPixel($x*2+1,$y*2+1))
                }}
                $icon.Save((Join-Path $root ('Art/UI/Icons/'+$name+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)
            } finally {$icon.Dispose()}
        } finally {$output.Dispose()}
    }
} finally {$unlit.Dispose();$lit.Dispose()}
