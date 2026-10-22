param([Parameter(Mandatory=$true)][string]$GeneratedPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$layerPath=Join-Path $folder 'body_walk_normal_cloth.png'
$combinedPath=Join-Path $folder 'body_walk_normals.png'
if((Test-Path $layerPath) -or (Test-Path $combinedPath)){throw 'Output exists; refusing to overwrite artwork'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$diffuse=[Drawing.Bitmap]::new((Join-Path $folder 'body_walk.png'))
$baseline=[Drawing.Bitmap]::new((Join-Path $folder 'sprite_walk_palette.png'))
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$layer=[Drawing.Bitmap]::new(256,512)
$combined=[Drawing.Bitmap]::new(256,512)
function IsCloth($c,$y){
 return $c.A -gt 0 -and $y -ge 34 -and $y -le 87 -and $c.R -gt 40 -and [Math]::Abs([int]$c.R-$c.G) -le 8 -and ([int]$c.G-$c.B) -ge 10
}
$counts=@();$misses=0;$total=0
try{
 if($diffuse.Width -ne 256 -or $diffuse.Height -ne 512 -or $baseline.Size -ne $diffuse.Size){throw 'Unexpected walk layout'}
 for($row=0;$row -lt 4;$row++){for($col=0;$col -lt 4;$col++){
  $mask=[Collections.Generic.List[Drawing.Point]]::new()
  for($y=0;$y -lt 128;$y++){for($x=0;$x -lt 64;$x++){
   if(IsCloth ($diffuse.GetPixel($col*64+$x,$row*128+$y)) $y){$mask.Add([Drawing.Point]::new($col*64+$x,$row*128+$y))}
  }}
  if($mask.Count -eq 0){throw "No cloth in frame $row/$col"}
  $minX=($mask.X|Measure-Object -Minimum).Minimum;$maxX=($mask.X|Measure-Object -Maximum).Maximum
  $minY=($mask.Y|Measure-Object -Minimum).Minimum;$maxY=($mask.Y|Measure-Object -Maximum).Maximum
  $xx=@{};$yy=@{}
  $x0=[int][Math]::Floor($col*$proposal.Width/4);$x1=[int][Math]::Floor(($col+1)*$proposal.Width/4)
  $y0=[int][Math]::Floor($row*$proposal.Height/4);$y1=[int][Math]::Floor(($row+1)*$proposal.Height/4)
  for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
   $c=$proposal.GetPixel($x,$y)
   if($c.A -ge 128 -and [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -ge 150){$xx[$x]++;$yy[$y]++}
  }}
  $xs=@($xx.Keys|Where-Object{$xx[$_] -ge 3}|Sort-Object);$ys=@($yy.Keys|Where-Object{$yy[$_] -ge 3}|Sort-Object)
  if($xs.Count -eq 0 -or $ys.Count -eq 0){throw "Missing generated frame $row/$col"}
  foreach($p in $mask){
   $sx=[int][Math]::Floor($xs[0]+($p.X-$minX+.5)*($xs[-1]-$xs[0]+1)/($maxX-$minX+1))
   $sy=[int][Math]::Floor($ys[0]+($p.Y-$minY+.5)*($ys[-1]-$ys[0]+1)/($maxY-$minY+1))
   $c=$proposal.GetPixel([Math]::Clamp($sx,0,$proposal.Width-1),[Math]::Clamp($sy,0,$proposal.Height-1))
   if($c.A -lt 128){$misses++;$c=$baseline.GetPixel($p.X,$p.Y)}
   [uint32]$rgb=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
   $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
   $layer.SetPixel($p.X,$p.Y,[Drawing.Color]::FromArgb($diffuse.GetPixel($p.X,$p.Y).A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255)))
  }
  $counts+=$mask.Count;$total+=$mask.Count
 }}
 if($misses -gt $total*.15){throw "Too many proposal omissions: $misses/$total"}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  $old=$baseline.GetPixel($x,$y);$c=$layer.GetPixel($x,$y)
  if($c.A -gt 0 -and $old.A -gt 0){$next=[Drawing.Color]::FromArgb($old.A,$c.R,$c.G,$c.B)}else{$next=$old}
  $combined.SetPixel($x,$y,$next)
  if($next.A -ne $old.A){throw 'Combined alpha changed'}
  if($c.A -eq 0 -and $next.ToArgb() -ne $old.ToArgb()){throw 'Non-cloth changed'}
 }}
 $layer.Save($layerPath,[Drawing.Imaging.ImageFormat]::Png)
 $combined.Save($combinedPath,[Drawing.Imaging.ImageFormat]::Png)
 "PASS 16 frames, exact picker palette on cloth; combined alpha and noncloth RGBA preserved. Cloth pixels: $total; fallback: $misses. Per-frame mask counts: $($counts -join ',')"
}finally{$diffuse.Dispose();$baseline.Dispose();$proposal.Dispose();$layer.Dispose();$combined.Dispose()}
