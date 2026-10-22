$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$path=Join-Path $root 'Art/Player/Animations/Body/body_walk_normal_cloth.png'
$backup=Join-Path $root 'Docs/References/ClothNormalRefinement/body_walk_normal_cloth_before_center_folds.png'
if(Test-Path $backup){throw 'Backup exists; refusing to overwrite a later revision'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$left=$palette[3];$center=$palette[4]
function Packed($c){[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)}
$source=[Drawing.Bitmap]::new($path)
$result=[Drawing.Bitmap]::new($source.Width,$source.Height)
$changed=0;$retained=0
try{
 for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
  $a=$source.GetPixel($x,$y);$b=$a
  if($a.A -gt 0 -and (Packed $a) -eq $left){
   # Retain one pixel on the left edge of each contiguous upward fold band.
   $previous=if($x%64 -gt 0){$source.GetPixel($x-1,$y)}else{[Drawing.Color]::Empty}
   if($previous.A -gt 0 -and (Packed $previous) -eq $left){
    $b=[Drawing.Color]::FromArgb($a.A,($center-band 255),(($center-shr 8)-band 255),(($center-shr 16)-band 255));$changed++
   }else{$retained++}
  }
  $result.SetPixel($x,$y,$b)
 }}
 for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
  $a=$source.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if($a.ToArgb() -ne $b.ToArgb() -and ((Packed $a) -ne $left -or (Packed $b) -ne $center)){throw 'Unrelated normal changed'}
 }}
 Copy-Item -LiteralPath $path -Destination $backup
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $source.Dispose();[IO.File]::WriteAllBytes($path,$bytes)
 "PASS cloth only: $changed fold pixels Left -> Center; $retained left-edge pixels retained; alpha and all other colors unchanged."
}finally{$source.Dispose();$result.Dispose()}
