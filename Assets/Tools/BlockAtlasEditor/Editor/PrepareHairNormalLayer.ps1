param([Parameter(Mandatory=$true)][string]$GeneratedPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$destination=Join-Path $folder 'body_idle_normal_hair.png'
if(Test-Path $destination){throw 'Hair layer exists; refusing to overwrite artwork'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$source=[Drawing.Bitmap]::new((Join-Path $folder 'body_idle.png'))
$base=[Drawing.Bitmap]::new((Join-Path $folder 'sprite_n_palette.png'))
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$output=[Drawing.Bitmap]::new(128,512)
function HairPixel($x,$y){
 $c=$source.GetPixel($x,$y);$row=[int][Math]::Floor($y/128);$ly=$y%128;$lx=$x%64
 # Grayscale hair ramps only. Skin, eye whites, pure black ink are excluded.
 if($c.A -eq 0 -or $c.R -eq 0 -or $c.R -gt 160 -or [Math]::Abs([int]$c.R-$c.G) -gt 5 -or [Math]::Abs([int]$c.G-$c.B) -gt 12){return $false}
 if($row -eq 1){return $ly -le 31}
 if($ly -gt 28){return $false}
 if($ly -ge 22){
  if($row -eq 0 -and $lx -ge 25 -and $lx -le 40){return $false}
  if($row -eq 2 -and $lx -le 29){return $false}
  if($row -eq 3 -and $lx -ge 35){return $false}
 }
 return $true
}
$counts=@(0,0,0,0,0,0,0,0);$misses=0
try{
 foreach($row in 0..3){foreach($col in 0..1){
  $xs=@();$ys=@()
  for($y=$row*128;$y -lt ($row+1)*128;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   if(HairPixel $x $y){$xs+=$x;$ys+=$y}
  }}
  if($xs.Count -eq 0){throw 'Missing source hair'}
  $a=@(($xs|Measure-Object -Minimum).Minimum,($ys|Measure-Object -Minimum).Minimum,
   (($xs|Measure-Object -Maximum).Maximum-($xs|Measure-Object -Minimum).Minimum+1),
   (($ys|Measure-Object -Maximum).Maximum-($ys|Measure-Object -Minimum).Minimum+1))
  $gx=@{};$gy=@{}
  $x0=[int][Math]::Floor($col*$proposal.Width/2);$x1=[int][Math]::Floor(($col+1)*$proposal.Width/2)
  $y0=[int][Math]::Floor($row*$proposal.Height/4);$y1=[int][Math]::Floor(($row+1)*$proposal.Height/4)
  for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
   $c=$proposal.GetPixel($x,$y)
   if($c.A -lt 128 -or [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -lt 150){continue}
   $gx[$x]++;$gy[$y]++
  }}
  $xx=@($gx.Keys|Where-Object{$gx[$_] -ge 3}|Sort-Object);$yy=@($gy.Keys|Where-Object{$gy[$_] -ge 3}|Sort-Object)
  if($xx.Count -eq 0 -or $yy.Count -eq 0){throw 'Missing generated hair'}
  $b=@($xx[0],$yy[0],($xx[-1]-$xx[0]+1),($yy[-1]-$yy[0]+1))
  for($y=$row*128;$y -lt ($row+1)*128;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   [uint32]$rgb=$palette[7];$alpha=0
   if(HairPixel $x $y){
    $sx=[Math]::Clamp([int][Math]::Floor($b[0]+($x-$a[0]+.5)*$b[2]/$a[2]),0,$proposal.Width-1)
    $sy=[Math]::Clamp([int][Math]::Floor($b[1]+($y-$a[1]+.5)*$b[3]/$a[3]),0,$proposal.Height-1)
    $c=$proposal.GetPixel($sx,$sy)
    if($c.A -lt 128){$misses++;$c=$base.GetPixel($x,$y)}
    [uint32]$packed=[uint32]$c.R-bor([uint32]$c.G-shl 8)-bor([uint32]$c.B-shl 16)
    $rgb=[TurfPaletteRestriction]::Snap($packed,$palette);$alpha=$source.GetPixel($x,$y).A
    $counts[$row*2+$col]++
   }
   if($rgb -notin $palette){throw 'Non-palette normal'}
   $output.SetPixel($x,$y,[Drawing.Color]::FromArgb($alpha,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255)))
  }}
 }}
 $output.Save($destination,[Drawing.Imaging.ImageFormat]::Png)
 'PASS 128x512 palette hair-only layer. Pixels per frame: '+($counts-join ',')+"; proposal omissions using latest normal-map fallback: $misses"
}finally{$source.Dispose();$base.Dispose();$proposal.Dispose();$output.Dispose()}
