$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/LeftArm'
$backupFolder=Join-Path $root 'Docs/References/LeftArmDiffuseCleanup'
foreach($state in @('idle','walk')){
 $path=Join-Path $folder "left_arm_${state}_diffuse.png"
 $backup=Join-Path $backupFolder "left_arm_${state}_diffuse_before.png"
 if(Test-Path $backup){throw "Backup exists for $state; refusing to overwrite artwork revision"}
 $source=[Drawing.Bitmap]::new($path)
 $output=[Drawing.Bitmap]::new($source.Width,$source.Height)
 $changes=New-Object int[] ([int]($source.Width/64)*4)
 try{
  if($source.Height -ne 512 -or $source.Width -notin @(128,256)){throw 'Unexpected arm sheet'}
  for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
   $a=$source.GetPixel($x,$y);$b=$a
   if(-not ($x -lt 64 -and $y -lt 128) -and $a.A -gt 0){
    $rg=[int]$a.R-$a.G;$gb=[int]$a.G-$a.B
    if($a.R -gt 40 -and [Math]::Abs($rg) -le 10 -and $gb -ge 9){
     $b=[Drawing.Color]::FromArgb($a.A,129,125,106)
    }elseif($a.R -gt 60 -and $rg -ge 6 -and $gb -ge 4 -and $gb -le $rg*1.8){
     $b=[Drawing.Color]::FromArgb($a.A,162,150,139)
    }
   }
   $output.SetPixel($x,$y,$b)
   if($a.ToArgb() -ne $b.ToArgb()){
    $index=([int][Math]::Floor($y/128))*[int]($source.Width/64)+[int][Math]::Floor($x/64)
    $changes[$index]++
   }
  }}
  for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
   $a=$source.GetPixel($x,$y);$b=$output.GetPixel($x,$y)
   if($a.A -ne $b.A){throw 'Alpha changed'}
   if(($x -lt 64 -and $y -lt 128) -and $a.ToArgb() -ne $b.ToArgb()){throw 'Authored frame changed'}
   if(($a.A -eq 0 -or [Math]::Max($a.R,[Math]::Max($a.G,$a.B)) -le 40) -and $a.ToArgb() -ne $b.ToArgb()){throw 'Transparent pixels or ink changed'}
  }}
  $null=New-Item -ItemType Directory -Force -Path $backupFolder
  Copy-Item -LiteralPath $path -Destination $backup
  $memory=[IO.MemoryStream]::new()
  try{$output.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
  $source.Dispose();[IO.File]::WriteAllBytes($path,$bytes)
  $saved=[Drawing.Bitmap]::new($path)
  try{for($y=0;$y -lt $saved.Height;$y++){for($x=0;$x -lt $saved.Width;$x++){
   if($saved.GetPixel($x,$y).ToArgb() -ne $output.GetPixel($x,$y).ToArgb()){throw 'Saved PNG mismatch'}
  }}}finally{$saved.Dispose()}
  "PASS $state saved PNG: first frame exact, original alpha/empty cells and ink unchanged; changed pixels per frame: $($changes -join ',')"
 }finally{$source.Dispose();$output.Dispose()}
}
