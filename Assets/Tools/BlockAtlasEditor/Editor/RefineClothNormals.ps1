param([Parameter(Mandatory=$true)][string]$GeneratedPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$target=Join-Path $root 'Art/Player/Animations/Body/body_idle_normal_cloth_v2.png'
$reference=Join-Path $root 'Docs/References/ClothNormalRefinement'
$backup=Join-Path $reference 'body_idle_normal_cloth_before_sprite_n.png'
if(Test-Path $backup){throw 'Backup exists; refusing to replace a subsequent art revision'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$original=[Drawing.Bitmap]::new($target)
$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$result=[Drawing.Bitmap]::new($original.Width,$original.Height)
function ClothBounds($image,$col,$row,$generated){
 $x0=[int][Math]::Floor($col*$image.Width/2);$x1=[int][Math]::Floor(($col+1)*$image.Width/2)
 $y0=[int][Math]::Floor($row*$image.Height/4);$y1=[int][Math]::Floor(($row+1)*$image.Height/4)
 $xx=@{};$yy=@{}
 for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
  $c=$image.GetPixel($x,$y)
  if($c.A -lt 128 -or ($generated -and [Math]::Max($c.R,[Math]::Max($c.G,$c.B)) -lt 150)){continue}
  $xx[$x]++;$yy[$y]++
 }}
 $minimum=if($generated){3}else{1}
 $xs=@($xx.Keys|Where-Object{$xx[$_] -ge $minimum}|Sort-Object)
 $ys=@($yy.Keys|Where-Object{$yy[$_] -ge $minimum}|Sort-Object)
 if($xs.Count -eq 0 -or $ys.Count -eq 0){throw 'Missing cloth silhouette'}
 return @($xs[0],$ys[0],($xs[-1]-$xs[0]+1),($ys[-1]-$ys[0]+1))
}
$changes=@(0,0,0,0,0,0,0,0);$misses=0;$visible=0
try{
 if($original.Width -ne 128 -or $original.Height -ne 512){throw 'Unexpected target layout'}
 for($row=0;$row -lt 4;$row++){for($col=0;$col -lt 2;$col++){
  $a=ClothBounds $original $col $row $false;$b=ClothBounds $proposal $col $row $true
  for($y=$row*128;$y -lt ($row+1)*128;$y++){for($x=$col*64;$x -lt ($col+1)*64;$x++){
   $old=$original.GetPixel($x,$y);$next=$old
   if($old.A -gt 0){
    $visible++
    $sx=[Math]::Clamp([int][Math]::Floor($b[0]+($x-$a[0]+.5)*$b[2]/$a[2]),0,$proposal.Width-1)
    $sy=[Math]::Clamp([int][Math]::Floor($b[1]+($y-$a[1]+.5)*$b[3]/$a[3]),0,$proposal.Height-1)
    $c=$proposal.GetPixel($sx,$sy)
    # Preserve original normal where proposal omitted cloth, not background RGB.
    if($c.A -lt 128){$misses++;$c=$old}
    [uint32]$rgb=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
    $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
    $next=[Drawing.Color]::FromArgb($old.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255))
   }
   if($next.ToArgb() -ne $old.ToArgb()){$changes[$row*2+$col]++}
   $result.SetPixel($x,$y,$next)
  }}
 }}
 if($misses -gt $visible*.15){throw "Proposal misses too much cloth ($misses/$visible); review registration before replacing"}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $a=$original.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Cloth alpha changed'}
  if($a.A -eq 0 -and $a.ToArgb() -ne $b.ToArgb()){throw 'Noncloth pixel changed'}
  if($b.A -gt 0){
   [uint32]$rgb=[uint32]$b.R -bor ([uint32]$b.G-shl 8) -bor ([uint32]$b.B-shl 16)
   if($rgb -notin $palette){throw 'Non-picker normal RGB'}
  }
 }}
 $null=New-Item -ItemType Directory -Force -Path $reference
 Copy-Item -LiteralPath $target -Destination $backup
 Copy-Item -LiteralPath $GeneratedPath -Destination (Join-Path $reference 'imagegen_cloth_proposal.png')
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $original.Dispose();[IO.File]::WriteAllBytes($target,$bytes)
 'PASS exact cloth alpha, unchanged noncloth RGBA, all visible RGB in current picker palette.'
 'Changed pixels per frame: '+($changes-join ',')+"; proposal omissions retained from original: $misses/$visible"
}finally{$original.Dispose();$proposal.Dispose();$result.Dispose()}
