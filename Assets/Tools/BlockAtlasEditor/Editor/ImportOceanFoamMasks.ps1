param([Parameter(Mandatory=$true)][string]$Source)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$path=Join-Path $root 'Resources/OceanFoamAtlas.png'
if(Test-Path $path){throw 'Ocean foam atlas exists; refusing to overwrite later painting.'}
$src=[Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Source).Path)
$dst=[Drawing.Bitmap]::new(64,32)
try{
 if([Math]::Abs($src.Width/$src.Height-2)-gt .02){throw 'Expected 2:1 four-mask sheet.'}
 $total=0
 for($tile=0;$tile-lt 4;$tile++){
  $count=0;$ox=($tile%2)*32;$oy=[int][Math]::Floor($tile/2)*16
  for($y=0;$y-lt 16;$y++){for($x=0;$x-lt 32;$x++){
   $alpha=0
   if($x-gt 0-and $x-lt 31-and $y-gt 0-and $y-lt 15){
    $x0=[int][Math]::Floor(($ox+$x)*$src.Width/64);$x1=[int][Math]::Floor(($ox+$x+1)*$src.Width/64)
    $y0=[int][Math]::Floor(($oy+$y)*$src.Height/32);$y1=[int][Math]::Floor(($oy+$y+1)*$src.Height/32)
    $sum=0.0;$n=0
    for($sy=$y0;$sy-lt $y1;$sy++){for($sx=$x0;$sx-lt $x1;$sx++){
     $c=$src.GetPixel($sx,$sy);$luma=($c.R*.2126+$c.G*.7152+$c.B*.0722)/255
     $sum+=$c.A*$luma*$luma;$n++
    }}
    $value=$sum/[Math]::Max(1,$n)
    $alpha=if($value-lt 32){0}elseif($value-lt 112){80}elseif($value-lt 208){160}else{255}
   }
   if($alpha-gt 0){$count++;$total++}
   $dst.SetPixel($ox+$x,$oy+$y,[Drawing.Color]::FromArgb($alpha,255,255,255))
  }}
  if($count-lt 20-or $count-gt 360){throw "Invalid mask coverage in cell $tile : $count/512"}
  Write-Output "Foam mask $tile : $count/512 visible logical pixels"
 }
 $dst.Save($path,[Drawing.Imaging.ImageFormat]::Png)
 Write-Output "Ocean foam atlas PASS: 64x32 RGBA, four 32x16 masks, transparent cell borders, alpha levels 0/80/160/255; total $total/2048 visible."
}finally{$src.Dispose();$dst.Dispose()}
