param([Parameter(Mandatory=$true)][string]$GeneratedPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$outputPath=Join-Path $folder 'body_walk_normals_torso_v2.png'
$layerPath=Join-Path $folder 'body_walk_normal_torso.png'
if((Test-Path $outputPath) -or (Test-Path $layerPath)){throw 'Output exists; refusing to replace artwork'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$idle=[Drawing.Bitmap]::new((Join-Path $folder 'body_idle.png'))
$idleNormal=[Drawing.Bitmap]::new((Join-Path $folder 'body_idle_normals.png'))
$walk=[Drawing.Bitmap]::new((Join-Path $folder 'body_walk.png'))
$baseline=[Drawing.Bitmap]::new((Join-Path $folder 'body_walk_normals.png'))
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$result=[Drawing.Bitmap]::new(256,512)
$layer=[Drawing.Bitmap]::new(256,512)
function Skin($c){
 $rg=[int]$c.R-$c.G;$gb=[int]$c.G-$c.B
 return $c.A -gt 0 -and (($c.R -gt 60 -and $rg -ge 6 -and $gb -ge 4 -and $gb -le $rg*1.8) -or ($c.R -eq 105 -and $c.G -eq 104 -and $c.B -eq 97))
}
$stats=@();$authored=0;$generated=0;$retained=0;$changed=0
try{
 if($idle.Width -ne 128 -or $walk.Width -ne 256 -or $baseline.Size -ne $walk.Size){throw 'Unexpected sheet layout'}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){$result.SetPixel($x,$y,$baseline.GetPixel($x,$y))}}
 for($row=0;$row -lt 4;$row++){for($col=0;$col -lt 4;$col++){
  $start=if($row -eq 1){28}else{34};$end=if($row -eq 1){38}else{47}
  $bestScore=[double]::NegativeInfinity;$best=@(0,0,0)
  # Match original painted anatomy, including neckline context. Translation only:
  # no scaling of the author's carefully drawn muscle planes.
  for($ic=0;$ic -lt 2;$ic++){for($dy=-2;$dy -le 2;$dy++){for($dx=-3;$dx -le 3;$dx++){
   $score=0.0
   for($y=$start;$y -le $end;$y++){for($x=18;$x -lt 46;$x++){
    $a=$walk.GetPixel($col*64+$x,$row*128+$y)
    $b=$idle.GetPixel($ic*64+$x+$dx,$row*128+$y+$dy)
    $sa=Skin $a;$sb=Skin $b
    if($sa -and $sb){$score+=8; if($a.ToArgb() -eq $b.ToArgb()){$score+=3}}
    elseif($sa -ne $sb){$score-=5}
   }}
   if($score -gt $bestScore){$bestScore=$score;$best=@($ic,$dx,$dy)}
  }}}
  $count=0
  for($y=$start;$y -le $end;$y++){for($x=16;$x -lt 48;$x++){
   $wx=$col*64+$x;$wy=$row*128+$y
   $skin=$walk.GetPixel($wx,$wy);$old=$baseline.GetPixel($wx,$wy)
   if(-not (Skin $skin) -or $old.A -eq 0){continue}
   $count++;$ix=$best[0]*64+$x+$best[1];$iy=$row*128+$y+$best[2]
   $normal=$old;$found=$false
   if((Skin ($idle.GetPixel($ix,$iy))) -and $idleNormal.GetPixel($ix,$iy).A -gt 0){
    $normal=$idleNormal.GetPixel($ix,$iy);$authored++;$found=$true
   }
   if(-not $found){
    # Only newly exposed pixels use the generated anatomical proposal.
    $px=[int][Math]::Floor(($col+($x-16+.5)/32)*$proposal.Width/4)
    $py=[int][Math]::Floor(($row+($y-28+.5)/20)*$proposal.Height/4)
    $candidate=$proposal.GetPixel([Math]::Clamp($px,0,$proposal.Width-1),[Math]::Clamp($py,0,$proposal.Height-1))
    if($candidate.A -ge 128){$normal=$candidate;$generated++}else{$retained++}
   }
   [uint32]$rgb=[uint32]$normal.R -bor ([uint32]$normal.G -shl 8) -bor ([uint32]$normal.B -shl 16)
   $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
   $next=[Drawing.Color]::FromArgb($old.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255))
   $result.SetPixel($wx,$wy,$next)
   $layer.SetPixel($wx,$wy,[Drawing.Color]::FromArgb(255,$next.R,$next.G,$next.B))
   if($old.ToArgb() -ne $next.ToArgb()){$changed++}
  }}
  $stats+="row=$row frame=$col idle=$($best[0]) dx=$($best[1]) dy=$($best[2]) skin=$count"
 }}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  $a=$baseline.GetPixel($x,$y);$b=$result.GetPixel($x,$y);$mask=$layer.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if($mask.A -eq 0 -and $a.ToArgb() -ne $b.ToArgb()){throw 'Non-torso changed'}
  if($mask.A -gt 0){
   if(-not (Skin ($walk.GetPixel($x,$y)))){throw 'Changed outside skin'}
   [uint32]$rgb=[uint32]$b.R -bor ([uint32]$b.G -shl 8) -bor ([uint32]$b.B -shl 16)
   if($rgb -notin $palette){throw 'Non-palette torso normal'}
  }
 }}
 $result.Save($outputPath,[Drawing.Imaging.ImageFormat]::Png)
 $layer.Save($layerPath,[Drawing.Imaging.ImageFormat]::Png)
 "PASS all alpha and non-torso RGBA exact; torso RGB in picker palette. Changed=$changed authored=$authored generated=$generated retained=$retained"
 $stats
}finally{$idle.Dispose();$idleNormal.Dispose();$walk.Dispose();$baseline.Dispose();$proposal.Dispose();$result.Dispose();$layer.Dispose()}
