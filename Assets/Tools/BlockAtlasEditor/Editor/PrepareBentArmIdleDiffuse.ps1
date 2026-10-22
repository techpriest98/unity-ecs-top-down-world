$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/LeftArm'
$target=Join-Path $folder 'left_arm_bent_idle_diffuse.png'
if(Test-Path $target){throw 'Output exists; refusing to replace artwork'}
$source=[Drawing.Bitmap]::new((Join-Path $folder 'left_arm_bent_idle.png'))
$output=[Drawing.Bitmap]::new($source.Width,$source.Height)
$changes=New-Object int[] 8
try{
 if($source.Width -ne 128 -or $source.Height -ne 512){throw 'Unexpected bent arm layout'}
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $a=$source.GetPixel($x,$y);$b=$a
  if($a.A -gt 0){
   $rg=[int]$a.R-$a.G;$gb=[int]$a.G-$a.B
   if($a.R -gt 40 -and [Math]::Abs($rg) -le 10 -and $gb -ge 9){$b=[Drawing.Color]::FromArgb($a.A,129,125,106)}
   elseif($a.R -gt 60 -and $rg -ge 6 -and $gb -ge 4 -and $gb -le $rg*1.8){$b=[Drawing.Color]::FromArgb($a.A,162,150,139)}
  }
  $output.SetPixel($x,$y,$b)
  if($a.ToArgb() -ne $b.ToArgb()){$changes[([int][Math]::Floor($y/128))*2+[int][Math]::Floor($x/64)]++}
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if(($a.A -eq 0 -or [Math]::Max($a.R,[Math]::Max($a.G,$a.B)) -le 40) -and $a.ToArgb() -ne $b.ToArgb()){throw 'Ink or transparency RGB changed'}
 }}
 $output.Save($target,[Drawing.Imaging.ImageFormat]::Png)
 $saved=[Drawing.Bitmap]::new($target)
 try{for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  if($saved.GetPixel($x,$y).ToArgb() -ne $output.GetPixel($x,$y).ToArgb()){throw 'Saved PNG mismatch'}
 }}}finally{$saved.Dispose()}
 'PASS saved bent idle diffuse: original alpha/ink preserved, flat skin and cloth. Changes per frame: '+($changes -join ',')
}finally{$source.Dispose();$output.Dispose()}
