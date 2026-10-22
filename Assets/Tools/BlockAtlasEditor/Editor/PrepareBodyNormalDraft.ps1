param([Parameter(Mandatory=$true)][string]$GeneratedPath,
 [ValidateSet('Legacy','Cloth','Skin','HeadDetails')][string]$Layer='Legacy')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
if($Layer -eq 'HeadDetails'){
 # Ignore detached generator speckles when registering sprite silhouettes.
 Add-Type -ReferencedAssemblies (@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+[Drawing.Bitmap].Assembly.Location+[Drawing.Rectangle].Assembly.Location) -TypeDefinition @'
using System;
using System.Drawing;
public static class BodyNormalRegistration {
 public static int[] Bounds(Bitmap image,int column,int row){
  int left=column*image.Width/2,top=row*image.Height/4;
  int w=(column+1)*image.Width/2-left,h=(row+1)*image.Height/4-top;
  var solid=new bool[w*h];var seen=new bool[w*h];var queue=new int[w*h];
  for(int y=0;y<h;y++)for(int x=0;x<w;x++)solid[y*w+x]=image.GetPixel(left+x,top+y).A>=128;
  int best=0;int[] bounds=null;
  for(int start=0;start<solid.Length;start++){
   if(!solid[start]||seen[start])continue;
   int count=0,end=1,x0=w,y0=h,x1=0,y1=0;queue[0]=start;seen[start]=true;
   while(count<end){int p=queue[count++],x=p%w,y=p/w;
    x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);
    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){
     int nx=x+dx,ny=y+dy;if(nx<0||nx>=w||ny<0||ny>=h)continue;
     int n=ny*w+nx;if(!solid[n]||seen[n])continue;seen[n]=true;queue[end++]=n;
    }
   }
   if(count>best){best=count;bounds=new[]{left+x0,top+y0,x1-x0+1,y1-y0+1};}
  }
  if(bounds==null)throw new Exception("Missing sprite");return bounds;
 }
}
'@
}
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$source=[Drawing.Bitmap]::new((Join-Path $root 'Art/Player/Animations/Body/body_idle.png'))
$draft=[Drawing.Bitmap]::new($GeneratedPath)
$filename=switch($Layer){
 'Cloth' {'body_idle_normal_cloth_v2.png'}
 'Skin' {'body_idle_normal_skin_draft.png'}
 'HeadDetails' {'body_idle_normal_head_details_draft.png'}
 default {'body_idle_normal_draft.png'}
}
$destination=Join-Path $root ('Art/Player/Animations/Body/'+$filename)
if(Test-Path $destination){throw 'Draft exists; refusing to overwrite artwork.'}
function Bounds($bitmap,$column,$row) {
 if($Layer -eq 'HeadDetails'){return [BodyNormalRegistration]::Bounds($bitmap,$column,$row)}
 $x0=[int][Math]::Floor($column*$bitmap.Width/2);$x1=[int][Math]::Floor(($column+1)*$bitmap.Width/2)
 $y0=[int][Math]::Floor($row*$bitmap.Height/4);$y1=[int][Math]::Floor(($row+1)*$bitmap.Height/4)
 $left=$x1;$right=$x0;$top=$y1;$bottom=$y0
 for($y=$y0;$y -lt $y1;$y++){for($x=$x0;$x -lt $x1;$x++){
  if($bitmap.GetPixel($x,$y).A -lt 128){continue}
  $left=[Math]::Min($left,$x);$right=[Math]::Max($right,$x)
  $top=[Math]::Min($top,$y);$bottom=[Math]::Max($bottom,$y)
 }}
 if($right -lt $left -or $bottom -lt $top){throw 'Missing sprite in draft'}
 return @($left,$top,($right-$left+1),($bottom-$top+1))
}
$output=[Drawing.Bitmap]::new($source.Width,$source.Height)
$used=[Collections.Generic.HashSet[uint32]]::new()
try {
 foreach($row in 0..3){foreach($column in 0..1){
  $a=Bounds $source $column $row;$b=Bounds $draft $column $row
  for($y=[int]($row*$source.Height/4);$y -lt ($row+1)*$source.Height/4;$y++){
   for($x=[int]($column*$source.Width/2);$x -lt ($column+1)*$source.Width/2;$x++){
    $c=$source.GetPixel($x,$y);[uint32]$rgb=0xff8080
    # Separate material masks. Start at the actual shoulders (row 35),
    # not row 39 as in the original draft. Neither mask includes black.
    $minimumY=if($Layer -eq 'Legacy'){39}else{34}
    $cloth=$c.A -gt 0 -and ($y%128) -ge $minimumY -and ($y%128) -lt 88 -and
     $c.R -gt 40 -and [Math]::Abs([int]$c.R-$c.G) -le 8 -and ($c.G-$c.B) -ge 10
    $redGreen=[int]$c.R-$c.G;$greenBlue=[int]$c.G-$c.B
    $skin=$c.A -gt 0 -and $c.R -gt 60 -and $redGreen -ge 6 -and
     $greenBlue -ge 4 -and $greenBlue -le ($redGreen*1.8)
    # Head details are the remaining head pixels after the established skin
    # and cloth masks. Preserve pure black ink instead of embossing it.
    $headDetails=$c.A -gt 0 -and ($y%128) -le 40 -and -not $skin -and -not $cloth -and
     ([int]$c.R+$c.G+$c.B) -gt 0
    $selected=switch($Layer){'Skin' {$skin};'HeadDetails' {$headDetails};default {$cloth}}
    if($selected){
     $sx=[Math]::Clamp([int][Math]::Floor($b[0]+($x-$a[0]+.5)*$b[2]/$a[2]),0,$draft.Width-1)
     $sy=[Math]::Clamp([int][Math]::Floor($b[1]+($y-$a[1]+.5)*$b[3]/$a[3]),0,$draft.Height-1)
     $n=$draft.GetPixel($sx,$sy)
     if($n.A -gt 127){
      [uint32]$packed=[uint32]$n.R -bor ([uint32]$n.G -shl 8) -bor ([uint32]$n.B -shl 16)
      $rgb=[TurfPaletteRestriction]::Snap($packed,$palette)
     }
    }
    if($rgb -notin $palette){throw 'Non-palette output'}
    $null=$used.Add($rgb)
    $alpha=if($Layer -eq 'Legacy' -or $selected){$c.A}else{0}
    $output.SetPixel($x,$y,[Drawing.Color]::FromArgb($alpha,($rgb -band 255),(($rgb -shr 8)-band 255),(($rgb -shr 16)-band 255)))
   }
  }
 }}
 $output.Save($destination,[Drawing.Imaging.ImageFormat]::Png)
 "Draft saved: $destination; $($source.Width)x$($source.Height); layer $Layer, original alpha within material mask; $($used.Count) picker colours. Approximate bounding-box registration, NOT production-validated normals."
} finally {$source.Dispose();$draft.Dispose();$output.Dispose()}
