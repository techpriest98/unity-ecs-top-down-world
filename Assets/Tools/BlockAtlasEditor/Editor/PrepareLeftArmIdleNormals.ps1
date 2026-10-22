param([Parameter(Mandatory=$true)][string]$GeneratedPath,[string]$OutputName='left_arm_idle_normals.png',[switch]$Walk,[switch]$Bent)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/LeftArm'
if($Walk -and -not $PSBoundParameters.ContainsKey('OutputName')){$OutputName='left_arm_walk_normals.png'}
if($Bent -and -not $PSBoundParameters.ContainsKey('OutputName')){$OutputName='left_arm_bent_idle_normals.png'}
if($Bent -and $Walk){throw 'Bent Walk is not supported by this preset'}
$destination=Join-Path $folder $OutputName
if(Test-Path $destination){throw 'Output exists; refusing to replace artwork'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$sourceName=if($Walk){'left_arm_walk.png'}else{'left_arm_idle.png'}
if($Bent){$sourceName='left_arm_bent_idle.png'}
$columns=if($Walk){4}else{2}
$proposalRows=if($Walk -or $Bent){4}else{3}
$source=[Drawing.Bitmap]::new((Join-Path $folder $sourceName))
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$output=[Drawing.Bitmap]::new($columns*64,512)
$counts=@();$misses=0;$total=0
try{
 if($source.Width -ne $columns*64 -or $source.Height -ne 512){throw 'Unexpected arm layout'}
 foreach($row in 0..($proposalRows-1)){foreach($col in 0..($columns-1)){
  $points=[Collections.Generic.List[Drawing.Point]]::new()
  for($y=$row*128;$y -lt ($row+1)*128;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   if($source.GetPixel($x,$y).A -gt 0){$points.Add([Drawing.Point]::new($x,$y))}
  }}
  if($points.Count -eq 0){$counts+=0;continue}
  $minX=($points.X|Measure-Object -Minimum).Minimum;$maxX=($points.X|Measure-Object -Maximum).Maximum
  $minY=($points.Y|Measure-Object -Minimum).Minimum;$maxY=($points.Y|Measure-Object -Maximum).Maximum
  $xx=@{};$yy=@{}
  # Idle frame two has the same arm orientation, not mirrored anatomy.
  # Reuse the proposal's first column for both source silhouettes.
  $x0=0;$x1=[int][Math]::Floor($proposal.Width/2)
  if($Walk){$x0=[int][Math]::Floor($col*$proposal.Width/$columns);$x1=[int][Math]::Floor(($col+1)*$proposal.Width/$columns)}
  $y0=[int][Math]::Floor($row*$proposal.Height/$proposalRows);$y1=[int][Math]::Floor(($row+1)*$proposal.Height/$proposalRows)
  for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
   $c=$proposal.GetPixel($x,$y)
   if($c.A -ge 250 -and [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -ge 200){$xx[$x]++;$yy[$y]++}
  }}
  $xs=@($xx.Keys|Where-Object{$xx[$_] -ge 3}|Sort-Object);$ys=@($yy.Keys|Where-Object{$yy[$_] -ge 3}|Sort-Object)
  if($xs.Count -eq 0 -or $ys.Count -eq 0){throw "Missing generated arm $row/$col"}
  foreach($p in $points){
   $sx=[int][Math]::Floor($xs[0]+($p.X-$minX+.5)*($xs[-1]-$xs[0]+1)/($maxX-$minX+1))
   $sy=[int][Math]::Floor($ys[0]+($p.Y-$minY+.5)*($ys[-1]-$ys[0]+1)/($maxY-$minY+1))
   $sx=[Math]::Clamp($sx,$x0,$x1-1);$sy=[Math]::Clamp($sy,$y0,$y1-1)
   $c=$proposal.GetPixel($sx,$sy)
   [uint32]$rgb=$palette[7]
   if($c.A -lt 128){
    $misses++
    # Extend the nearest valid normal horizontally to the exact original outline.
    for($dx=1;$dx -lt ($xs[-1]-$xs[0]+1);$dx++){
     $a=$proposal.GetPixel([Math]::Clamp($sx-$dx,$x0,$x1-1),$sy)
     $b=$proposal.GetPixel([Math]::Clamp($sx+$dx,$x0,$x1-1),$sy)
     if($a.A -ge 128){$c=$a;break}
     if($b.A -ge 128){$c=$b;break}
    }
   }
   if($c.A -ge 128){
    [uint32]$packed=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
    $rgb=[TurfPaletteRestriction]::Snap($packed,$palette)
   }
   $output.SetPixel($p.X,$p.Y,[Drawing.Color]::FromArgb($source.GetPixel($p.X,$p.Y).A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255)))
  }
  $total+=$points.Count;$counts+=$points.Count
 }}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt $source.Width;$x++){
  $a=$source.GetPixel($x,$y);$b=$output.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha differs from original (including empty row)'}
  if($b.A -gt 0){
   [uint32]$rgb=[uint32]$b.R -bor ([uint32]$b.G -shl 8) -bor ([uint32]$b.B -shl 16)
   if($rgb -notin $palette){throw 'Non-palette visible RGB'}
  }
 }}
 $output.Save($destination,[Drawing.Imaging.ImageFormat]::Png)
 $saved=[Drawing.Bitmap]::new($destination)
 try{for($y=0;$y -lt 512;$y++){for($x=0;$x -lt $source.Width;$x++){
  if($saved.GetPixel($x,$y).ToArgb() -ne $output.GetPixel($x,$y).ToArgb()){throw 'Saved PNG mismatch'}
 }}}finally{$saved.Dispose()}
 "PASS $($source.Width)x512 original alpha at every pixel, empty cells retained, visible normals in picker palette. Visible=$total; edge extensions=$misses; frame counts=$($counts -join ',')"
}finally{$source.Dispose();$proposal.Dispose();$output.Dispose()}
