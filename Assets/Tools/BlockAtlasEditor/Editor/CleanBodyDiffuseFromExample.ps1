$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$destination=Join-Path $folder 'body_idle_diffuse_clean.png'
if(Test-Path $destination){throw 'Clean diffuse exists; refusing to overwrite artwork.'}
$source=[Drawing.Bitmap]::new((Join-Path $folder 'body_idle.png'))
$example=[Drawing.Bitmap]::new((Join-Path $folder 'body_idle_test.png'))
$result=[Drawing.Bitmap]::new($source.Width,$source.Height)
function Hex($c){'{0:X2}{1:X2}{2:X2}' -f $c.R,$c.G,$c.B}
function Rgb($hex,$alpha){[Drawing.Color]::FromArgb($alpha,[Convert]::ToInt32($hex.Substring(0,2),16),[Convert]::ToInt32($hex.Substring(2,2),16),[Convert]::ToInt32($hex.Substring(4,2),16))}
try {
 if($source.Width -ne 128 -or $source.Height -ne 512 -or $example.Size -ne $source.Size){throw 'Unexpected sheet layout'}
 # Learn dominant source->target palette substitutions in the authored frame.
 # Do not propagate authored alpha/outline edits into other animation poses.
 $votes=@{}
 for($y=128;$y -lt 256;$y++){for($x=0;$x -lt 64;$x++){
  $a=$source.GetPixel($x,$y);$b=$example.GetPixel($x,$y)
  if($a.A -eq 0 -or $b.A -eq 0){continue}
  $key=Hex $a;$value=Hex $b
  if(-not $votes.ContainsKey($key)){$votes[$key]=@{}}
  $votes[$key][$value]++
 }}
 $mapping=@{}
 foreach($key in $votes.Keys){$winner=$votes[$key].GetEnumerator()|Sort-Object Value -Descending|Select-Object -First 1;$mapping[$key]=$winner.Name}
 $changes=@(0,0,0,0,0,0,0,0)
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $a=$source.GetPixel($x,$y);$localY=$y%128;$color=Hex $a;$target=$color
  if($x -lt 64 -and $y -ge 128 -and $y -lt 256){
   # The reference is authoritative, including its twelve alpha edits.
   $result.SetPixel($x,$y,$example.GetPixel($x,$y));continue
  }
  if($a.A -eq 0 -or [Math]::Max($a.R,[Math]::Max($a.G,$a.B)) -le 8){
   $result.SetPixel($x,$y,$a);continue
  }
  # Eye material is not shading. Protect pupils and whites near a bright,
  # nearly neutral eye-white pixel, but not the darker grey hair highlights.
  $eye=$false
  if($localY -ge 17 -and $localY -le 31){
   for($dy=-2;$dy -le 2 -and -not $eye;$dy++){for($dx=-2;$dx -le 2;$dx++){
    $sx=$x+$dx;$sy=$y+$dy
    if($sx -lt 0 -or $sx -ge 128 -or [int][Math]::Floor($sx/64) -ne [int][Math]::Floor($x/64)){continue}
    $p=$source.GetPixel($sx,$sy)
    if($p.A -gt 0 -and $p.R -gt 180 -and $p.G -gt 180 -and $p.B -gt 170 -and [Math]::Abs([int]$p.R-$p.G) -lt 5){$eye=$true;break}
   }}
  }
  $rg=[int]$a.R-$a.G;$gb=[int]$a.G-$a.B
  if(-not $eye){
   if($localY -ge 34 -and $localY -lt 88 -and $a.R -gt 40 -and [Math]::Abs($rg) -le 10 -and $gb -ge 9){$target='817D6A'}
   elseif(($a.R -gt 60 -and $rg -ge 6 -and $gb -ge 4 -and $gb -le $rg*1.8) -or
          ($localY -ge 28 -and $localY -le 42 -and $color -eq '696861')){$target='A2968B'}
   elseif($localY -lt 43 -and $a.R -le 90 -and [Math]::Abs($rg) -le 5 -and [Math]::Abs($gb) -le 6){$target='343432'}
   elseif($mapping.ContainsKey($color) -and $mapping[$color] -in @('817D6A','A2968B','343432')){$target=$mapping[$color]}
  }
  $result.SetPixel($x,$y,(Rgb $target $a.A))
  if($target -ne $color){$changes[([int][Math]::Floor($y/128))*2+[int][Math]::Floor($x/64)]++}
 }}
 # Independently verify preservation contracts before saving.
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $actual=$result.GetPixel($x,$y)
  if($x -lt 64 -and $y -ge 128 -and $y -lt 256){
   if($actual.ToArgb() -ne $example.GetPixel($x,$y).ToArgb()){throw 'Authored frame changed'}
  }else{
   $original=$source.GetPixel($x,$y)
   if($actual.A -ne $original.A){throw 'Animation silhouette changed'}
   if(($original.R+$original.G+$original.B) -eq 0 -and $actual.ToArgb() -ne $original.ToArgb()){throw 'Black ink changed'}
  }
 }}
 $result.Save($destination,[Drawing.Imaging.ImageFormat]::Png)
 'PASS: exact authored frame; other frames retain source alpha/black ink. Changed RGB pixels per frame: '+($changes -join ',')
 'Saved '+$destination
} finally {$source.Dispose();$example.Dispose();$result.Dispose()}
