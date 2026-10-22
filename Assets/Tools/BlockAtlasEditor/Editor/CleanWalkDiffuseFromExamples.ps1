$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$target=Join-Path $root 'Art/Player/Animations/Body/body_walk_diffuse_clean.png'
$backup=Join-Path $root 'Docs/References/WalkDiffuseCleanup/body_walk_diffuse_clean_before.png'
if(Test-Path $backup){throw 'Backup exists; refusing to overwrite a later artwork revision'}
$source=[Drawing.Bitmap]::new($target)
$result=[Drawing.Bitmap]::new($source.Width,$source.Height)
$changes=New-Object int[] 16
try{
 if($source.Width -ne 256 -or $source.Height -ne 512){throw 'Unexpected walk sheet dimensions'}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  $a=$source.GetPixel($x,$y);$b=$a;$ly=$y%128
  $authored=$y -lt 128 -and $x -lt 192
  if(-not $authored -and $a.A -gt 0){
   $rg=[int]$a.R-$a.G;$gb=[int]$a.G-$a.B
   # Only material-specific warm ramps; grayscale hair/beard/ink is untouched.
   if($ly -ge 34 -and $ly -lt 88 -and $a.R -gt 40 -and [Math]::Abs($rg) -le 10 -and $gb -ge 9){
    $b=[Drawing.Color]::FromArgb($a.A,129,125,106)
   }elseif($a.R -gt 60 -and $rg -ge 6 -and $gb -ge 4 -and $gb -le $rg*1.8){
    $b=[Drawing.Color]::FromArgb($a.A,162,150,139)
   }
  }
  $result.SetPixel($x,$y,$b)
  if($a.ToArgb() -ne $b.ToArgb()){$changes[([int][Math]::Floor($y/128))*4+[int][Math]::Floor($x/64)]++}
 }}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
  $a=$source.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if($y -lt 128 -and $x -lt 192 -and $a.ToArgb() -ne $b.ToArgb()){throw 'Authored frame changed'}
  $gray=[Math]::Abs([int]$a.R-$a.G) -le 5 -and [Math]::Abs([int]$a.G-$a.B) -le 8
  if(($gray -or $a.A -eq 0) -and $a.ToArgb() -ne $b.ToArgb()){throw 'Hair/ink/transparency changed'}
 }}
 $null=New-Item -ItemType Directory -Force -Path (Split-Path $backup)
 Copy-Item -LiteralPath $target -Destination $backup
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $source.Dispose()
 [IO.File]::WriteAllBytes($target,$bytes)
 $saved=[Drawing.Bitmap]::new($target);$before=[Drawing.Bitmap]::new($backup)
 try{
  for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 256;$x++){
   if($saved.GetPixel($x,$y).ToArgb() -ne $result.GetPixel($x,$y).ToArgb()){throw 'Saved PNG mismatch'}
   if($saved.GetPixel($x,$y).A -ne $before.GetPixel($x,$y).A){throw 'Saved alpha mismatch'}
  }}
 }finally{$saved.Dispose();$before.Dispose()}
 'PASS saved PNG: first three frames exact, alpha/silhouettes and grayscale hair/ink exact. Changes per frame: '+($changes -join ',')
}finally{$source.Dispose();$result.Dispose()}
