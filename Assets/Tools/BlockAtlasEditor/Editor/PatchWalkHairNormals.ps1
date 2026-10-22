param(
 [Parameter(Mandatory=$true)][string]$GeneratedPath,
 [string]$BackupName='body_walk_normals_before_hair.png'
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$target=Join-Path $folder 'body_walk_normals.png'
$backup=Join-Path (Join-Path $root 'Docs/References/WalkHairNormals') $BackupName
if(Test-Path $backup){throw 'Backup exists; refusing to overwrite a subsequent revision'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$diffuse=[Drawing.Bitmap]::new((Join-Path $folder 'body_walk.png'))
$before=[Drawing.Bitmap]::new($target)
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$result=[Drawing.Bitmap]::new(256,512)
function IsHair($x,$y){
 $row=[int][Math]::Floor($y/128);$ly=$y%128;$lx=$x%64
 if($row -eq 0){return $false}
 $c=$diffuse.GetPixel($x,$y)
 if($c.A -eq 0 -or $c.R -eq 0 -or $c.R -gt 160 -or [Math]::Abs([int]$c.R-$c.G) -gt 5 -or [Math]::Abs([int]$c.G-$c.B) -gt 12){return $false}
 if($row -eq 1){return $ly -le 31}
 if($ly -gt 28){return $false}
 if($ly -ge 22 -and (($row -eq 2 -and $lx -le 29) -or ($row -eq 3 -and $lx -ge 35))){return $false}
 return $true
}
$changed=0;$misses=0;$pixels=0;$counts=@()
try{
 if($before.Width -ne 256 -or $before.Height -ne 512 -or $before.Size -ne $diffuse.Size){throw 'Unexpected walk layout'}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){$result.SetPixel($x,$y,$before.GetPixel($x,$y))}}
 foreach($row in 1..3){foreach($col in 0..3){
  $points=[Collections.Generic.List[Drawing.Point]]::new()
  for($y=$row*128;$y -lt $row*128+32;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   if((IsHair $x $y) -and $before.GetPixel($x,$y).A -gt 0){$points.Add([Drawing.Point]::new($x,$y))}
  }}
  if($points.Count -eq 0){throw "Missing source hair $row/$col"}
  $minX=($points.X|Measure-Object -Minimum).Minimum;$maxX=($points.X|Measure-Object -Maximum).Maximum
  $minY=($points.Y|Measure-Object -Minimum).Minimum;$maxY=($points.Y|Measure-Object -Maximum).Maximum
  $xx=@{};$yy=@{}
  $x0=[int][Math]::Floor($col*$proposal.Width/4);$x1=[int][Math]::Floor(($col+1)*$proposal.Width/4)
  $y0=[int][Math]::Floor(($row-1)*$proposal.Height/3);$y1=[int][Math]::Floor($row*$proposal.Height/3)
  for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
   $c=$proposal.GetPixel($x,$y)
   if($c.A -ge 128 -and [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -ge 150){$xx[$x]++;$yy[$y]++}
  }}
  $xs=@($xx.Keys|Where-Object{$xx[$_] -ge 3}|Sort-Object);$ys=@($yy.Keys|Where-Object{$yy[$_] -ge 3}|Sort-Object)
  if($xs.Count -eq 0 -or $ys.Count -eq 0){throw "Missing proposal hair $row/$col"}
  foreach($p in $points){
   $sx=[int][Math]::Floor($xs[0]+($p.X-$minX+.5)*($xs[-1]-$xs[0]+1)/($maxX-$minX+1))
   $sy=[int][Math]::Floor($ys[0]+($p.Y-$minY+.5)*($ys[-1]-$ys[0]+1)/($maxY-$minY+1))
   $old=$before.GetPixel($p.X,$p.Y)
   $c=$proposal.GetPixel([Math]::Clamp($sx,0,$proposal.Width-1),[Math]::Clamp($sy,0,$proposal.Height-1))
   if($c.A -lt 128){$misses++;continue}
   [uint32]$rgb=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
   $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
   $next=[Drawing.Color]::FromArgb($old.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255))
   if($next.ToArgb() -ne $old.ToArgb()){$changed++}
   $result.SetPixel($p.X,$p.Y,$next)
  }
  $pixels+=$points.Count;$counts+=$points.Count
 }}
 if($misses -gt $pixels*.15){throw "Too many proposal omissions: $misses/$pixels"}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  $a=$before.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if($a.ToArgb() -ne $b.ToArgb()){
   if($y -lt 128 -or -not (IsHair $x $y)){throw 'First row or nonhair changed'}
   [uint32]$rgb=[uint32]$b.R -bor ([uint32]$b.G -shl 8) -bor ([uint32]$b.B -shl 16)
   if($rgb -notin $palette){throw 'Non-palette hair RGB'}
  }
 }}
 $null=New-Item -ItemType Directory -Force -Path (Split-Path $backup)
 Copy-Item -LiteralPath $target -Destination $backup
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $before.Dispose();[IO.File]::WriteAllBytes($target,$bytes)
 $saved=[Drawing.Bitmap]::new($target)
 try{for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  if($saved.GetPixel($x,$y).ToArgb() -ne $result.GetPixel($x,$y).ToArgb()){throw 'Saved PNG differs'}
 }}}finally{$saved.Dispose()}
 "PASS saved PNG: first row and nonhair RGBA exact, alpha exact, changed hair RGB in picker palette. Changed=$changed; fallback=$misses/$pixels; masks per frame=$($counts -join ',')"
}finally{$diffuse.Dispose();$before.Dispose();$proposal.Dispose();$result.Dispose()}
