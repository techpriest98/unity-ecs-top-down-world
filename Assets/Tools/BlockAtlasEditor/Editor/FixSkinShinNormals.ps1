param([Parameter(Mandatory=$true)][string]$GeneratedPath,[switch]$ReferenceGuidedLegs)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$target=Join-Path $root 'Art/Player/Animations/Body/body_idle_normal_skin_draft.png'
$backup=Join-Path $root 'Docs/References/SkinChestNormals/body_idle_normal_skin_before_shins.png'
if($ReferenceGuidedLegs){$backup=Join-Path $root 'Docs/References/LegNormalRefinement/body_idle_normal_skin_before_sprite_n_legs.png'}
if(Test-Path $backup){throw 'Backup exists; refusing to overwrite a subsequent art revision'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
[uint32[]]$vertical=$palette[6..8]
$original=[Drawing.Bitmap]::new($target)
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$result=[Drawing.Bitmap]::new($original.Width,$original.Height)
function LegBounds($image,$column,$row,$generated){
 $x0=[int][Math]::Floor($column*$image.Width/2);$x1=[int][Math]::Floor(($column+1)*$image.Width/2)
 $y0=[int][Math]::Floor(($row+.55)*$image.Height/4);$y1=[int][Math]::Floor(($row+1)*$image.Height/4)
 $xs=@{};$ys=@{}
 for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
  $c=$image.GetPixel($x,$y)
  if($c.A -lt 128 -or ($generated -and [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -lt 100)){continue}
  $xs[$x]++;$ys[$y]++
 }}
 $threshold=if($generated){3}else{1}
 $xx=@($xs.Keys|Where-Object{$xs[$_] -ge $threshold}|Sort-Object)
 $yy=@($ys.Keys|Where-Object{$ys[$_] -ge $threshold}|Sort-Object)
 if($xx.Count -eq 0 -or $yy.Count -eq 0){throw 'Missing leg component'}
 return @($xx[0],$yy[0],($xx[-1]-$xx[0]+1),($yy[-1]-$yy[0]+1))
}
$changed=@(0,0,0,0,0,0,0,0)
try{
 for($row=0;$row -lt 4;$row++){for($col=0;$col -lt 2;$col++){
  $a=LegBounds $original $col $row $false;$b=LegBounds $proposal $col $row $true
  for($y=$row*128;$y -lt ($row+1)*128;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   $old=$original.GetPixel($x,$y);$next=$old
   # Restrict to upright calf/ankle area. Existing foot tops are untouched.
   $endY=if($ReferenceGuidedLegs){110}else{96}
   if(($y%128) -ge 82 -and ($y%128) -lt $endY -and $old.A -gt 0){
    $sx=[Math]::Clamp([int][Math]::Floor($b[0]+($x-$a[0]+.5)*$b[2]/$a[2]),0,$proposal.Width-1)
    $sy=[Math]::Clamp([int][Math]::Floor($b[1]+($y-$a[1]+.5)*$b[3]/$a[3]),0,$proposal.Height-1)
    $n=$proposal.GetPixel($sx,$sy)
    # Fallback is the existing x/z tilt, with all upward tilt removed.
    if($n.A -lt 128){$n=$old}
    if($ReferenceGuidedLegs -and ($y%128) -ge 96){
     [uint32]$rgb=[uint32]$n.R -bor ([uint32]$n.G -shl 8) -bor ([uint32]$n.B -shl 16)
     $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
    }else{
     [uint32]$rgb=[uint32]$n.R -bor ([uint32]128 -shl 8) -bor ([uint32]$n.B -shl 16)
     if($n.B -lt 145 -and [Math]::Abs([int]$n.R-128) -lt 20){$rgb=$vertical[1]}
     $rgb=[TurfPaletteRestriction]::Snap($rgb,$vertical)
    }
    $next=[Drawing.Color]::FromArgb($old.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255))
   }
   if($next.ToArgb() -ne $old.ToArgb()){$changed[$row*2+$col]++}
   $result.SetPixel($x,$y,$next)
  }}
 }}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $a=$original.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  $endY=if($ReferenceGuidedLegs){110}else{96}
  if(($y%128) -lt 82 -or ($y%128) -ge $endY -or $a.A -eq 0){
   if($a.ToArgb() -ne $b.ToArgb()){throw 'Changed outside opaque shins'}
  }else{
   [uint32]$rgb=[uint32]$b.R-bor([uint32]$b.G-shl 8)-bor([uint32]$b.B-shl 16)
   if(($y%128) -lt 96 -and $rgb -notin $vertical){throw 'Non-vertical calf normal'}
   if($rgb -notin $palette){throw 'Non-palette foot normal'}
  }
 }}
 $null=New-Item -ItemType Directory -Force -Path (Split-Path $backup)
 Copy-Item -LiteralPath $target -Destination $backup
 if($ReferenceGuidedLegs){Copy-Item -LiteralPath $GeneratedPath -Destination (Join-Path (Split-Path $backup) 'imagegen_leg_proposal.png')}
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $original.Dispose();[IO.File]::WriteAllBytes($target,$bytes)
 'PASS: shins restricted to Front/vertical picker row; unchanged alpha and other RGBA. Changes per frame: '+($changed-join ',')
}finally{$original.Dispose();$proposal.Dispose();$result.Dispose()}
