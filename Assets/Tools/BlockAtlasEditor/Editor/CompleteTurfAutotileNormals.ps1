param([switch]$CheckOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
[uint32[]]$palette=@(& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly)
if($palette.Length-ne 12){throw 'Expected exact current normal picker colors.'}
$draftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$atlasPath=Join-Path $root 'Art/World/BlockAtlas_Normal.png'
$text=[IO.File]::ReadAllText($draftPath)
$match=[regex]::Match($text,'(?ms)^  - Id: 9\r?\n.*?(?=^  - Id:|\z)')
if(!$match.Success){throw 'Turf missing.'}
$parts=$match.Value-split '    NormalPixels:'
if($parts.Count-ne 2){throw 'Unexpected serialization.'}
[uint32[]]$colors=@([regex]::Matches($parts[0],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
[uint32[]]$old=@([regex]::Matches($parts[1],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
if($colors.Length-ne 3072-or $old.Length-ne 3072){throw 'Expected 64x48 block.'}
$layout=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.cs') -Raw
$elements=@([regex]::Matches($layout,'new AtlasElement\("([^"]+)",\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)')|ForEach-Object{
 @{Name=$_.Groups[1].Value;X=[int]$_.Groups[2].Value;Y=[int]$_.Groups[3].Value;W=[int]$_.Groups[4].Value;H=[int]$_.Groups[5].Value;DX=[int]$_.Groups[6].Value;DY=[int]$_.Groups[7].Value}
})
if($elements.Count-ne 23){throw 'Unexpected layout.'}
[int[]]$rects=@($elements|ForEach-Object{$_.X;$_.Y;$_.W;$_.H;$_.DX;$_.DY})
$target=[bool[]]::new(3072)
foreach($e in $elements[3..22]){for($y=$e.Y;$y-lt $e.Y+$e.H;$y++){for($x=$e.X;$x-lt $e.X+$e.W;$x++){$target[$y*64+$x]=$true}}}
Add-Type -TypeDefinition @'
using System;
public static class TurfOverlayNormalArt {
 static int R(uint p)=>(int)(p&255);static int G(uint p)=>(int)((p>>8)&255);static int B(uint p)=>(int)((p>>16)&255);
 static bool Grass(uint p)=>G(p)>R(p)||(R(p)==201&&G(p)==185&&B(p)==149)||(R(p)==165&&G(p)==160&&B(p)==119);
 static bool Stone(uint p)=>R(p)==112&&G(p)==120&&B(p)==112;
 static double Height(uint p){
  if(Stone(p))return .30;
  int r=R(p),g=G(p);
  if(r==201&&g==185)return .17;if(r==165&&g==160)return .13;
  if(r==133&&g==135)return .10;if(r==102&&g==111)return .06;if(r==72&&g==84)return .01;
  if(r==48&&g==39)return -.29;if(r==54&&g==44)return -.26;
  if(r==61&&g==51)return -.22;if(r==71&&g==57)return -.14;
  return 0;
 }
 static uint Sample(uint[] image,int x,int y,int lo,int hi)=>image[Math.Clamp(y,lo,hi)*32+(x%32+32)%32];
 static double H(uint[] image,int x,int y,int lo,int hi){uint p=Sample(image,x,y,lo,hi);double h=Height(p);
  if(Stone(p)){foreach(var d in new[]{(-1,0),(1,0),(0,-1),(0,1)})if(Stone(Sample(image,x+d.Item1,y+d.Item2,lo,hi)))h+=.035;}
  return h;
 }
 static double[] Decode(uint p){double x=(p&255)/127.5-1,y=((p>>8)&255)/127.5-1,z=((p>>16)&255)/127.5-1;
  double l=Math.Sqrt(x*x+y*y+z*z);return new[]{x/l,y/l,z/l};}
 static uint Direction(double x,double y,double z,uint[] palette){
  double length=Math.Sqrt(x*x+y*y+z*z),best=-2;uint selected=0;
  foreach(uint q in palette){var n=Decode(q);double dot=(n[0]*x+n[1]*y+n[2]*z)/length;
   if(dot>best){best=dot;selected=q;}}
  return selected;
 }
 static uint Snap(uint p,uint[] palette){uint rgb=p&0xFFFFFFu;foreach(uint q in palette)if(q==rgb)return rgb;
  var n=Decode(p);return Direction(n[0],n[1],n[2],palette);}
 static void Paint(uint[] master,uint[] color,int[] r,int element,bool foot){
  int o=element*6,sx=r[o],sy=r[o+1],w=r[o+2],h=r[o+3],dx=r[o+4],dy=r[o+5];
  for(int y=0;y<h;y++)for(int x=0;x<w;x++){uint p=color[(sy+y)*64+sx+x];if((p>>24)==0)continue;
   master[(foot?dy+y-44:dy+y)*32+dx+x]=p;}
 }
 public static uint[] Build(uint[] c,uint[] old,int[] r,uint[] palette){
  var result=(uint[])old.Clone();var master=new uint[32*48];var footMaster=new uint[32*48];
  for(int y=0;y<48;y++)for(int x=0;x<32;x++){master[y*32+x]=c[y*64+x];footMaster[y*32+x]=c[(y%16)*64+x];}
  foreach(int e in new[]{3,4,5,6,19,20,21,7,8,9,10,15,16})Paint(master,c,r,e,false);
  foreach(int e in new[]{22,17,18})Paint(footMaster,c,r,e,true);
  for(int e=3;e<23;e++){
   int o=e*6,sx=r[o],sy=r[o+1],w=r[o+2],h=r[o+3],dx=r[o+4],dy=r[o+5];
   bool foot=e==17||e==18||e==22,top=e<=14;
   uint[] context=(uint[])(foot?footMaster:master).Clone();
   // Inner corners and any individually repainted variations use their actual
   // local colors, not a regenerated or mirrored copy of an older straight edge.
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    uint p=c[(sy+y)*64+sx+x];if((p>>24)!=0)context[(foot?dy+y-44:dy+y)*32+dx+x]=p;
   }
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    int i=(sy+y)*64+sx+x,mx=dx+x,my=foot?dy+y-44:dy+y;
    uint color=c[i],alpha=color>>24;
    if(alpha==0){result[i]=palette[7];continue;} // Exact color alpha; no invisible coverage.
    int baseY=foot?my:dy+y,baseIndex=baseY*64+mx;
    uint normal;
    bool upward=top||(foot&&(e==22||Grass(color)||color==c[baseIndex]));
    if(color==c[baseIndex])normal=Snap(old[baseIndex],palette);
    else{
     int lo=upward?0:16,hi=upward?15:47;
     double gx=H(context,mx+1,my,lo,hi)-H(context,mx-1,my,lo,hi);
     double gy=H(context,mx,my+1,lo,hi)-H(context,mx,my-1,lo,hi);
     if(upward){
      double nx=-gx*4,nz=-gy*4;
      // Exposed soil rolls off the non-front rim. A foot is a continuation
      // of a foreground Top, not a vertical wall and not a new rounded rim.
      if(top&&!Grass(color)&&!Stone(color)){
       if(mx<3)nx-=(3-mx)*.35;if(mx>28)nx+=(mx-28)*.35;
       if(my<3)nz-=(3-my)*.35;if(my>12)nz+=(my-12)*.35;
      }
      normal=Direction(nx,1,nz,palette);
     }else{
      double ny=gy*2.5,nz=1;
      if(Grass(color)&&dy+y<20){double angle=((dy+y-16)*25+15)*Math.PI/180;ny=Math.Cos(angle);nz=Math.Sin(angle);}
      normal=Direction(-gx*2.5,ny,nz,palette);
     }
    }
    result[i]=normal|(alpha<<24);
   }
  }
  return result;
 }
}
'@
$next=if($CheckOnly){$old}else{[TurfOverlayNormalArt]::Build($colors,$old,$rects,$palette)}
$visible=0;$empty=0;$changed=0
for($i=0;$i-lt 3072;$i++){
 if(!$target[$i]){if($next[$i]-ne $old[$i]){throw 'Base or unused normal pixel changed.'};continue}
 if(($next[$i]-shr 24)-ne ($colors[$i]-shr 24)){throw "Color/normal alpha mismatch at pixel $i"}
 if(($next[$i]-band 16777215)-notin $palette){throw "Off-palette normal at pixel $i"}
 if(($colors[$i]-shr 24)-eq 0){$empty++}else{$visible++}
 if($next[$i]-ne $old[$i]){$changed++}
}
$db=Get-Content (Join-Path $root 'Configs/BlockDatabase.asset') -Raw
if($db-notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0'){throw 'Review changed atlas mapping.'}
function Pack($c){[uint32]([uint64]$c.R-bor([uint64]$c.G-shl 8)-bor([uint64]$c.B-shl 16)-bor([uint64]$c.A-shl 24))}
function PixelColor([uint32]$p){[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))}
$bitmap=[Drawing.Bitmap]::new($atlasPath);$before=[uint32[]]::new($bitmap.Width*$bitmap.Height)
try{
 for($y=0;$y-lt $bitmap.Height;$y++){for($x=0;$x-lt $bitmap.Width;$x++){$before[$y*$bitmap.Width+$x]=Pack ($bitmap.GetPixel($x,$y))}}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  $i=$y*64+$x;if(!$target[$i]){continue}
  if($CheckOnly-and $before[$y*$bitmap.Width+$x]-ne $old[$i]){throw 'Runtime/draft overlay normal mismatch.'}
  $bitmap.SetPixel($x,$y,(PixelColor $next[$i]))
 }}
 $stream=[IO.MemoryStream]::new()
 try{$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$png=$stream.ToArray()}finally{$stream.Dispose()}
}finally{$bitmap.Dispose()}
if($CheckOnly){Write-Output "Autotile normal CHECK PASS: exact 12-color picker RGBs, $visible visible / $empty transparent texels, pixel-exact color alpha, runtime/draft parity.";exit}
$backup=Join-Path $root 'Docs/Backups/TurfBeforeAutotileNormals'
if(Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')){throw 'Backup exists; refusing to overwrite later painting.'}
$script:pixelIndex=0
$normalText=[regex]::Replace($parts[1],'rgba: (\d+)',{param($m) $p=$next[$script:pixelIndex];$script:pixelIndex++;'rgba: '+$p})
$newEntry=$parts[0]+'    NormalPixels:'+$normalText
$updated=$text.Substring(0,$match.Index)+$newEntry+$text.Substring($match.Index+$match.Length)
if(($newEntry-split '    NormalPixels:')[0]-cne $parts[0]){throw 'Color data changed.'}
if($updated.Replace($newEntry,'')-cne $text.Replace($match.Value,'')){throw 'Other block entries changed.'}
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $draftPath -Destination (Join-Path $backup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $backup 'BlockAtlas_Normal.png.bak')
try{
 [IO.File]::WriteAllText($draftPath,$updated,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($atlasPath,$png)
 $check=[Drawing.Bitmap]::new($atlasPath)
 try{for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
  $inEdit=$x-lt 64-and $y-lt 48-and $target[$y*64+$x]
  $expected=if($inEdit){$next[$y*64+$x]}else{$before[$y*$check.Width+$x]}
  if((Pack ($check.GetPixel($x,$y)))-ne $expected){throw "PNG mismatch at $x,$y"}
 }}}finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas_Normal.png.bak') -Destination $atlasPath -Force
 throw
}
Write-Output "Autotile normals PASS: 20 elements, $changed updated pixels, $visible visible / $empty transparent; only picker colors; exact alpha; all color/base-normal/other-block data unchanged; PNG/draft parity."
