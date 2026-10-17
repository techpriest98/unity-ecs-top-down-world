param([string]$GeneratedImage,[switch]$SoilRim)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$draftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$text=[IO.File]::ReadAllText($draftPath)
$entries=@{}
foreach($id in 9,2,3){
 $m=[regex]::Match($text,"(?ms)^  - Id: $id\r?\n.*?(?=^  - Id:|\z)")
 if(!$m.Success){throw "Block $id missing."}
 $parts=$m.Value-split '    NormalPixels:'
 $entries[$id]=@{Match=$m;Parts=$parts;Pixels=[uint32[]]@([regex]::Matches($parts[0],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})}
}
function PixelColor([uint32]$p){[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))}
$layout=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.cs') -Raw
$elements=@([regex]::Matches($layout,'new AtlasElement\("([^"]+)",\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)')|ForEach-Object{
 @{Name=$_.Groups[1].Value;X=[int]$_.Groups[2].Value;Y=[int]$_.Groups[3].Value;W=[int]$_.Groups[4].Value;H=[int]$_.Groups[5].Value;DX=[int]$_.Groups[6].Value;DY=[int]$_.Groups[7].Value}
})
if($elements.Count-ne 23){throw 'Unexpected block layout.'}
if(!$GeneratedImage){
 $b=[Drawing.Bitmap]::new(1536,384)
 try{
  $panel=0
  foreach($id in 9,2,3){
   $pixels=$entries[$id].Pixels
   for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
    $p=$pixels[$y*64+$x];$c=PixelColor $p
    if(($p-shr 24)-eq 0){$c=[Drawing.Color]::FromArgb(20,20,24)}
    for($dy=0;$dy-lt 8;$dy++){for($dx=0;$dx-lt 8;$dx++){$b.SetPixel($panel*512+$x*8+$dx,$y*8+$dy,$c)}}
   }}
   $panel++
   foreach($e in $elements[3..22]){$count=0;for($y=$e.Y;$y-lt $e.Y+$e.H;$y++){for($x=$e.X;$x-lt $e.X+$e.W;$x++){if(($pixels[$y*64+$x]-shr 24)-ne 0){$count++}}};Write-Output "$id $($e.Name): $count/$($e.W*$e.H) opaque"}
  }
  $b.Save((Join-Path $root '.codex-autotile-reference.png'),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$b.Dispose()}
 $base=[Drawing.Bitmap]::new(512,768)
 try{for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 32;$x++){
  $c=PixelColor $entries[9].Pixels[$y*64+$x]
  for($dy=0;$dy-lt 16;$dy++){for($dx=0;$dx-lt 16;$dx++){$base.SetPixel($x*16+$dx,$y*16+$dy,$c)}}
 }};$base.Save((Join-Path $root '.codex-turf-base.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$base.Dispose()}
 exit
}
$original=$entries[9].Pixels
foreach($e in $elements[3..22]){for($y=$e.Y;$y-lt $e.Y+$e.H;$y++){for($x=$e.X;$x-lt $e.X+$e.W;$x++){
 if(!$SoilRim-and ($original[$y*64+$x]-shr 24)-ne 0){throw 'Turf overlays now contain authored pixels; refusing to overwrite them.'}
}}}
$backupName=if($SoilRim){'TurfBeforeSoilAutotileRim'}else{'TurfBeforeAutotileColor'}
$backup=Join-Path $root ('Docs/Backups/'+$backupName)
if(Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')){throw 'Backup exists; refusing to overwrite subsequent painting.'}
$atlasPath=Join-Path $root 'Art/World/BlockAtlas.png'
$database=Get-Content (Join-Path $root 'Configs/BlockDatabase.asset') -Raw
if($database-notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0'){throw 'Review changed atlas mapping.'}
$source=[Drawing.Bitmap]::new((Resolve-Path -LiteralPath $GeneratedImage).Path)
$study=[uint32[]]::new(32*48)
try{
 if([Math]::Abs($source.Width/$source.Height-2.0/3)-gt .01){throw 'Expected 2:3 full-block edge study.'}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 32;$x++){
  $c=$source.GetPixel([int][Math]::Floor(($x+.5)*$source.Width/32),[int][Math]::Floor(($y+.5)*$source.Height/48))
  $study[$y*32+$x]=[uint32]([uint64]$c.R-bor([uint64]$c.G-shl 8)-bor([uint64]$c.B-shl 16)-bor(255L-shl 24))
 }}
}finally{$source.Dispose()}
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public static class TurfAutotileColor {
 static int R(uint p)=>(int)(p&255);static int G(uint p)=>(int)((p>>8)&255);static int B(uint p)=>(int)((p>>16)&255);
 public static uint Nearest(uint p,uint[] palette){long best=long.MaxValue;uint result=0;
  foreach(uint q in palette){long r=R(p)-R(q),g=G(p)-G(q),b=B(p)-B(q),d=r*r+g*g+b*b;
   if(d<best){best=d;result=q;}}
  return result;
 }
 static int Width(uint p,bool soil)=>soil?1+(R(p)+G(p))%2:1+(R(p)+G(p)+B(p))%3;
 static bool TopMask(uint[] p,int x,int y,int flags)=>
  ((flags&1)!=0&&y<Width(p[x],false))||
  ((flags&2)!=0&&15-y<Width(p[15*64+x],false))||
  ((flags&4)!=0&&x<Width(p[y*64],false))||
  ((flags&8)!=0&&31-x<Width(p[y*64+31],false));
 static bool SideMask(uint[] p,int x,int y,int flags)=>
  ((flags&1)!=0&&y-16<3)||
  ((flags&2)!=0&&47-y<Width(p[47*64+x],true))||
  ((flags&4)!=0&&x<Width(p[y*64],y>=20))||
  ((flags&8)!=0&&31-x<Width(p[y*64+31],y>=20));
 public static uint[] SoilRim(uint[] p,uint[] study,int[] rects){
  var colors=new List<uint>();var soil=new List<uint>();
  for(int y=0;y<48;y++)for(int x=0;x<32;x++){
   uint q=p[y*64+x];if((q>>24)!=255)continue;
   if(!colors.Contains(q))colors.Add(q);
   if(R(q)<150&&G(q)<=R(q)&&!soil.Contains(q))soil.Add(q);
  }
  if(soil.Count<3)throw new Exception("Expected existing brown soil palette");
  colors.Sort();soil.Sort();uint[] palette=colors.ToArray(),brown=soil.ToArray();
  var result=(uint[])p.Clone();
  foreach(int element in new[]{3,5,6,7,8,11,12}){
   int o=element*6,sx=rects[o],sy=rects[o+1],w=rects[o+2],h=rects[o+3],dx=rects[o+4],dy=rects[o+5];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    int mx=dx+x,my=dy+y,i=(sy+y)*64+sx+x;
    // The complete front band and its corner intersections are protected.
    if(my>=12||(p[i]>>24)==0)continue;
    int depth=Math.Min(my,Math.Min(mx,31-mx));
    uint q=Nearest(study[my*32+mx],depth<2?brown:palette);
    result[i]=(q&0xFFFFFFu)|(p[i]&0xFF000000u);
   }
  }
  return result;
 }
 public static uint[] Build(uint[] p,uint[] study,int[] rects){
  var colors=new List<uint>();
  for(int y=0;y<48;y++)for(int x=0;x<32;x++){uint q=p[y*64+x];if((q>>24)==255&&!colors.Contains(q))colors.Add(q);}
  colors.Sort();uint[] palette=colors.ToArray();var painted=(uint[])p.Clone();
  for(int i=0;i<study.Length;i++)study[i]=Nearest(study[i],palette);
  // The same master coordinates feed strips and corners, so a corner junction
  // cannot introduce a different edge pattern than the neighbouring strip.
  for(int element=3;element<23;element++){
   int offset=element*6,sx=rects[offset],sy=rects[offset+1],w=rects[offset+2],h=rects[offset+3],dx=rects[offset+4],dy=rects[offset+5];
   int flags=element switch{3=>1,4=>2,5=>4,6=>8,7=>5,8=>9,9=>6,10=>10,
    15=>5,16=>9,17=>6,18=>10,19=>4,20=>8,21=>1,22=>2,_=>0};
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    int mx=dx+x,my=dy+y;bool top=element<=14,visible;
    if(element>=11&&element<=14){
     int u=(element==12||element==14)?3-x:x;
     int v=(element==13||element==14)?3-y:y;
     visible=u+v<=1; // Tiny concave notch, not two exposed straight edges.
    }else visible=top?TopMask(p,mx,my,flags):SideMask(p,mx,my,flags);
    uint q=study[(top&&my>=13?my+1:my)*32+mx];
    painted[(sy+y)*64+sx+x]=visible?q:0u;
   }
  }
  return painted;
 }
}
'@
[int[]]$rects=@($elements|ForEach-Object{$_.X;$_.Y;$_.W;$_.H;$_.DX;$_.DY})
$next=if($SoilRim){[TurfAutotileColor]::SoilRim($original,$study,$rects)}else{[TurfAutotileColor]::Build($original,$study,$rects)}
$allowed=@{}
for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 32;$x++){if(($original[$y*64+$x]-shr 24)-eq 255){$allowed[$original[$y*64+$x]]=1}}}
$target=[bool[]]::new(3072);$opaque=0
foreach($e in $elements[3..22]){
 $count=0
 for($y=$e.Y;$y-lt $e.Y+$e.H;$y++){for($x=$e.X;$x-lt $e.X+$e.W;$x++){
  $i=$y*64+$x;$target[$i]=!$SoilRim-or $next[$i]-ne $original[$i]
  if(($next[$i]-shr 24)-ne 0){
   if(!$allowed.ContainsKey($next[$i])){throw 'Overlay introduced a color or intermediate alpha outside the original opaque palette.'}
   $count++;$opaque++
  }
 }}
 if($count-eq 0){throw "Empty generated element: $($e.Name)"}
 Write-Output "$($e.Name): $count visible pixels"
}
for($i=0;$i-lt 3072;$i++){if(!$target[$i]-and $next[$i]-ne $original[$i]){throw 'Base or unused pixel changed.'}}
if($SoilRim){
 foreach($e in $elements[3..22]){for($y=0;$y-lt $e.H;$y++){for($x=0;$x-lt $e.W;$x++){
  $i=($e.Y+$y)*64+$e.X+$x
  if(($next[$i]-shr 24)-ne ($original[$i]-shr 24)){throw 'Soil rim changed overlay alpha.'}
  if(($e.DY+$y-ge 12-or $e.Name-eq 'Top / Front')-and $next[$i]-ne $original[$i]){throw 'Front or side overlay changed.'}
 }}}
}
$script:pixelIndex=0
$newColor=[regex]::Replace($entries[9].Parts[0],'rgba: (\d+)',{param($m) $p=$next[$script:pixelIndex];$script:pixelIndex++;'rgba: '+$p})
$newEntry=$newColor+'    NormalPixels:'+$entries[9].Parts[1]
$match=$entries[9].Match
$updated=$text.Substring(0,$match.Index)+$newEntry+$text.Substring($match.Index+$match.Length)
if($updated.Replace($newEntry,'')-cne $text.Replace($match.Value,'')){throw 'Other block entries changed.'}
if(($newEntry-split '    NormalPixels:')[1]-cne $entries[9].Parts[1]){throw 'Normals changed.'}
$atlas=[Drawing.Bitmap]::new($atlasPath)
$before=[int[]]::new($atlas.Width*$atlas.Height)
try{
 for($y=0;$y-lt $atlas.Height;$y++){for($x=0;$x-lt $atlas.Width;$x++){$before[$y*$atlas.Width+$x]=$atlas.GetPixel($x,$y).ToArgb()}}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){if($target[$y*64+$x]){$atlas.SetPixel($x,$y,(PixelColor $next[$y*64+$x]))}}}
 $memory=[IO.MemoryStream]::new()
 try{$atlas.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$png=$memory.ToArray()}finally{$memory.Dispose()}
}finally{$atlas.Dispose()}
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $draftPath -Destination (Join-Path $backup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $backup 'BlockAtlas.png.bak')
try{
 [IO.File]::WriteAllText($draftPath,$updated,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($atlasPath,$png)
 $check=[Drawing.Bitmap]::new($atlasPath)
 try{for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
  $inEdit=$x-lt 64-and $y-lt 48-and $target[$y*64+$x]
  $expected=if($inEdit){(PixelColor $next[$y*64+$x]).ToArgb()}else{$before[$y*$check.Width+$x]}
  if($check.GetPixel($x,$y).ToArgb()-ne $expected){throw "PNG mismatch at $x,$y"}
 }}}finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas.png.bak') -Destination $atlasPath -Force
 throw
}
if($SoilRim){Write-Output 'Soil rim PASS: back/left/right top rim recolored, front band/corners, all side overlays and all alpha bytes preserved; only existing palette colors; base/normal/other-block data untouched.'}
else{Write-Output "Turf autotile PASS: all 20 color overlays, $opaque opaque palette pixels; base art, unused slots, normal maps and other blocks untouched; runtime/draft overlay parity verified."}
