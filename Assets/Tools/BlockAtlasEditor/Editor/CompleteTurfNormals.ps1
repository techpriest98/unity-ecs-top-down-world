# Numeric edit of the editor's native Color32 vector data, not an image-generation filter.
# Run with painting suspended. Existing backup prevents accidental repeated application.
param([switch]$GrassTop, [switch]$GrassEdge, [switch]$SideLower)
$ErrorActionPreference = 'Stop'
if(($GrassTop -and $GrassEdge)-or($SideLower -and ($GrassTop-or $GrassEdge))){throw 'Choose one edit mode.'}
Add-Type -AssemblyName System.Drawing
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$draftPath = Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$atlasPath = Join-Path $root 'Art/World/BlockAtlas_Normal.png'
$backupName = if($SideLower){'TurfBeforeSideLowerNormals'}elseif($GrassEdge){'TurfBeforeRoundedGrassEdge'}elseif($GrassTop){'TurfTopBeforeBladeNormals'}else{'TurfNormalsBeforeCompletion'}
$backup = Join-Path $root ('Docs/Backups/'+$backupName)
if (Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')) { throw 'Backup exists; refusing to overwrite later painting.' }
$original = [IO.File]::ReadAllText($draftPath)
$match = [regex]::Match($original, '(?ms)^  - Id: 9\r?\n.*?(?=^  - Id:|\z)')
if (!$match.Success) { throw 'Turf ID 9 missing.' }
$parts = $match.Value -split '    NormalPixels:'
if ($parts.Count -ne 2) { throw 'Unexpected normal serialization.' }
[uint32[]]$colors = @([regex]::Matches($parts[0], 'rgba: (\d+)') | ForEach-Object { [uint32]$_.Groups[1].Value })
[uint32[]]$old = @([regex]::Matches($parts[1], 'rgba: (\d+)') | ForEach-Object { [uint32]$_.Groups[1].Value })
if ($colors.Length -ne 3072 -or $old.Length -ne 3072) { throw 'Expected 64x48 native block.' }
$database = [IO.File]::ReadAllText((Join-Path $root 'Configs/BlockDatabase.asset'))
if ($database -notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0') { throw 'Review changed atlas mapping.' }
Add-Type -TypeDefinition @'
using System;
public static class TurfNormalCompletion {
 public static int Preserved;
 public static int Blades;
 public static int EdgePixels;
 public static int LowerPixels;
 public static bool Grass(uint p) {
  if((p>>24)==0)return false;
  int r=R(p),g=G(p),b=B(p);
  return (r==201&&g==185&&b==149)||(r==165&&g==160&&b==119)||
   (r==133&&g==135&&b==97)||(r==102&&g==111&&b==83)||(r==72&&g==84&&b==66);
 }
 static int R(uint p)=>(int)(p&255); static int G(uint p)=>(int)((p>>8)&255); static int B(uint p)=>(int)((p>>16)&255);
 static uint N(double x,double y,double z,uint a) {
  double l=Math.Sqrt(x*x+y*y+z*z);
  return (uint)Math.Round((x/l*.5+.5)*255) | ((uint)Math.Round((y/l*.5+.5)*255)<<8) |
   ((uint)Math.Round((z/l*.5+.5)*255)<<16) | (a<<24);
 }
 // Deliberately material-specific relief, NOT luminance-to-height conversion.
 // Light straw is a thin blade/root; grey inclusions protrude; dark soil cracks recede.
 static double Height(uint p,bool top) {
  int r=R(p),g=G(p),b=B(p);
  if ((p>>24)==0) return 0;
  if(r==112&&g==120&&b==112) return .26; // stone
  if(r==201&&g==185&&b==149) return top?.12:.17; // straw / exposed root
  if(r==165&&g==160&&b==119) return top?.09:.15;
  if(r==133&&g==135&&b==97) return top?.065:.13;
  if(r==102&&g==111&&b==83) return top?.035:.10;
  if(r==72&&g==84&&b==66) return top?0:.07;
  if(r==48&&g==39&&b==35) return -.23;
  if(r==54&&g==44&&b==38) return -.19;
  if(r==61&&g==51&&b==44) return -.12;
  if(r==71&&g==57&&b==49) return -.04;
  return 0; // soil body and unfinished checker: no invented noise
 }
 static bool Authored(uint n) {
  int r=R(n),g=G(n),b=B(n);
  // Large center-color fills are background, the directional details are hand-painted.
  return !(r==128 && ((g==128&&b==255)||(g==37&&b==218)));
 }
 static double H(uint[] c,int x,int y,int cx,int cy,bool top) {
  x=(x%32+32)%32;
  if(top) y=(y%16+16)%16; else y=Math.Clamp(y,16,47);
  uint p=c[y*64+x];
  // Do not differentiate an unfinished checkerboard into fake terrain geometry.
  if(!top&&y>=32) return 0;
  return Height(p,top);
 }
 public static uint[] Build(uint[] c,uint[] old) {
  Preserved=0; var result=(uint[])old.Clone();
  for(int y=0;y<48;y++)for(int x=0;x<64;x++) {
   int i=y*64+x; uint a=c[i]>>24;
   if(a==0) { result[i]=old[i]&0xFFFFFFu; continue; }
   if(x>=32) { // Future authored overlays: retain their existing vectors and color coverage.
    result[i]=(old[i]&0xFFFFFFu)|(a<<24); continue;
   }
   if(y>=16 && y<32 && Authored(old[i])) {result[i]=old[i];Preserved++;continue;}
   if(y>=32) {result[i]=N(0,0,1,a);continue;}
   bool top=y<16;
   double dx=H(c,x+1,y,x,y,top)-H(c,x-1,y,x,y,top);
   double dy=H(c,x,y+1,x,y,top)-H(c,x,y-1,x,y,top);
   // Keep relief subtle. Top base faces UP; side base faces OUT, not down.
   result[i]=top?N(-dx*1.8,1,-dy*1.8,a):N(-dx*2.2,dy*2.2,1,a);
  }
  return result;
 }
 public static uint[] BuildGrassTop(uint[] c,uint[] old) {
  var result=(uint[])old.Clone(); Blades=0;
  for(int y=0;y<16;y++)for(int x=0;x<32;x++) {
   int i=y*64+x; uint p=c[i],a=p>>24;
   if(a==0)continue;
   int r=R(p),g=G(p),b=B(p);
   bool straw=(r==201&&g==185&&b==149)||(r==165&&g==160&&b==119);
   bool blade=straw||(r==133&&g==135&&b==97);
   if(!blade)continue; // Keep the previously painted substrate, soil and stone normals.
   double dx=H(c,x+1,y,x,y,true)-H(c,x-1,y,x,y,true);
   double dy=H(c,x,y+1,x,y,true)-H(c,x,y-1,x,y,true);
   double length=Math.Sqrt(dx*dx+dy*dy);
   if(length<.008) {
    // A one-pixel blade has equal neighbours on opposite sides: central differences
    // vanish. Use its exposed edge instead of leaving every thin blade flat.
    double drop=0,center=Height(p,true);
    foreach(var d in new[]{(-1,0),(1,0),(0,-1),(0,1)}) {
     double exposed=center-H(c,x+d.Item1,y+d.Item2,x,y,true);
     if(exposed>drop+.0001){drop=exposed;dx=-d.Item1;dy=-d.Item2;}
    }
    if(drop<.008)continue; // Interior of a broad patch remains upward-facing.
    length=Math.Sqrt(dx*dx+dy*dy);
   }
   // A leaf is a tilted facet, not a tall spike. Use ~45-52 degree slopes on
   // straw and ~39 degree slopes on olive blades; never flip the up component.
   double tilt=straw?1.25:.8;
   result[i]=N(-dx/length*tilt,1,-dy/length*tilt,a); Blades++;
  }
  return result;
 }
 public static uint[] BuildGrassEdge(uint[] c,uint[] old) {
  var result=(uint[])old.Clone();EdgePixels=0;
  for(int x=0;x<32;x++) {
   // Follow only the connected grass cap. A root below a soil gap is not a rim.
   int depth=0;
   while(depth<8 && Grass(c[(16+depth)*64+x]))depth++;
   for(int d=0;d<depth;d++) {
    int i=(16+d)*64+x;
    // A soft turn from UP to OUT: 15 / 45 / 65 / 80 degrees from up.
    // This uses the 45-up picker vector (128,218,218) in the middle of the cap.
    double degrees=d==0?15:d==1?45:d==2?65:80;
    double angle=degrees*Math.PI/180;
    // Retain some blade variation horizontally; don't carry the old flat-side
    // Y/Z across, because they caused the abrupt seam in the first place.
    uint detail=d==0?old[15*64+x]:old[i];
    double nx=(R(detail)/127.5-1)*.35;
    result[i]=N(nx,Math.Cos(angle),Math.Sin(angle),c[i]>>24);
    EdgePixels++;
   }
  }
  return result;
 }
 static uint SidePixel(uint[] c,int x,int y)=>c[Math.Clamp(y,16,47)*64+(x%32+32)%32];
 static bool Stone(uint p)=>R(p)==112&&G(p)==120&&B(p)==112;
 static double SoilHeight(uint[] c,int x,int y) {
  uint p=SidePixel(c,x,y);
  if(Stone(p)) {
   // Broad stone centers sit above their bevels; grey never becomes a pit.
   int inside=0;
   foreach(var d in new[]{(-1,0),(1,0),(0,-1),(0,1)})
    if(Stone(SidePixel(c,x+d.Item1,y+d.Item2)))inside++;
   return .26+.035*inside;
  }
  int r=R(p),g=G(p),b=B(p);
  if(r==71&&g==57&&b==49)return -.14;
  if(r==61&&g==51&&b==44)return -.22;
  if(r==54&&g==44&&b==38)return -.26;
  if(r==48&&g==39&&b==35)return -.29;
  return Height(p,false);
 }
 public static uint[] BuildSideLower(uint[] c,uint[] old) {
  var result=(uint[])old.Clone(); LowerPixels=0;
  for(int y=32;y<48;y++)for(int x=0;x<32;x++) {
   int i=y*64+x;uint a=c[i]>>24;
   if(a==0)continue;
   double dx=SoilHeight(c,x+1,y)-SoilHeight(c,x-1,y);
   double dy=SoilHeight(c,x,y+1)-SoilHeight(c,x,y-1);
   uint normal=N(-dx*2.5,dy*2.5,1,a);
   // The color importer repeats the boundary soil row. Match its existing
   // normals as well, without repainting the authored SideUpper.
   if(y==32 && c[i]==c[31*64+x])normal=(old[31*64+x]&0xFFFFFFu)|(a<<24);
   result[i]=normal;
   if(normal!=old[i])LowerPixels++;
  }
  // Geometry sanity fixture: positive height on the right means a raised
  // stone's left bevel must point left; a crack wall points into the hollow.
  uint left=N(-.8,.0,1,255),right=N(.8,.0,1,255);
  if(!(R(left)<128&&R(right)>128&&G(left)==128&&B(left)>128))
   throw new Exception("Mirrored side relief");
  return result;
 }
}
'@
$next = if($SideLower){[TurfNormalCompletion]::BuildSideLower($colors,$old)}elseif($GrassEdge){[TurfNormalCompletion]::BuildGrassEdge($colors,$old)}elseif($GrassTop){[TurfNormalCompletion]::BuildGrassTop($colors,$old)}else{[TurfNormalCompletion]::Build($colors,$old)}
if($SideLower){
 if([TurfNormalCompletion]::LowerPixels-lt 32){throw 'Too few lower-side relief pixels; review current color map.'}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  if(($x-ge 32-or $y-lt 32)-and $next[$y*64+$x]-ne $old[$y*64+$x]){throw 'Lower-side edit touched another normal element.'}
 }}
}
if($GrassEdge){
 if([TurfNormalCompletion]::EdgePixels-lt 32){throw 'Grass rim not found; review the current color palette.'}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  $i=$y*64+$x
  if($next[$i]-eq $old[$i]){continue}
  if($x-ge 32-or $y-lt 16-or $y-ge 24-or ![TurfNormalCompletion]::Grass($colors[$i])){throw 'Rim edit touched protected pixels.'}
 }}
}
if($GrassTop){
 if([TurfNormalCompletion]::Blades -lt 20){throw 'Too few recognizable grass pixels; review the current color palette.'}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  if(($x-ge 32-or $y-ge 16)-and $next[$y*64+$x]-ne $old[$y*64+$x]){throw 'Top-only edit changed another element.'}
 }}
}
$script:pixelIndex=0
$normalText=[regex]::Replace($parts[1], 'rgba: (\d+)', {
 param($pixel)
 $value=$next[$script:pixelIndex]; $script:pixelIndex++
 'rgba: '+$value
})
$newEntry=$parts[0]+'    NormalPixels:'+$normalText
$updated=$original.Substring(0,$match.Index)+$newEntry+$original.Substring($match.Index+$match.Length)
if($updated.Replace($newEntry,'') -cne $original.Replace($match.Value,'')) { throw 'Unrelated draft entries changed.' }
if(($newEntry -split '    NormalPixels:')[0] -cne $parts[0]) { throw 'Color pixels changed.' }
$bitmap=[Drawing.Bitmap]::new($atlasPath)
$beforePixels=[uint32[]]::new($bitmap.Width*$bitmap.Height)
try {
 for($y=0;$y-lt $bitmap.Height;$y++){for($x=0;$x-lt $bitmap.Width;$x++){
  $c=$bitmap.GetPixel($x,$y)
  $beforePixels[$y*$bitmap.Width+$x]=[uint32]([uint64]$c.R-bor([uint64]$c.G-shl 8)-bor([uint64]$c.B-shl 16)-bor([uint64]$c.A-shl 24))
 }}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  if($SideLower -and ($x-ge 32-or $y-lt 32)){continue}
  if($GrassEdge -and $next[$y*64+$x]-eq $old[$y*64+$x]){continue}
  if($GrassTop -and ($x-ge 32-or $y-ge 16)){continue}
  $p=$next[$y*64+$x]
  if(($p-shr 24)-ne($colors[$y*64+$x]-shr 24)) { throw 'Coverage mismatch.' }
  $vx=($p-band 255)/127.5-1;$vy=(($p-shr 8)-band 255)/127.5-1;$vz=(($p-shr 16)-band 255)/127.5-1
  if(($p-shr 24)-gt 0 -and [Math]::Abs([Math]::Sqrt($vx*$vx+$vy*$vy+$vz*$vz)-1)-gt .015){throw 'Invalid normal length.'}
  $bitmap.SetPixel($x,$y,[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255)))
 }}
 $memory=[IO.MemoryStream]::new()
 try{$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$png=$memory.ToArray()}finally{$memory.Dispose()}
}finally{$bitmap.Dispose()}
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $draftPath -Destination (Join-Path $backup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $backup 'BlockAtlas_Normal.png.bak')
try {
 [IO.File]::WriteAllText($draftPath,$updated,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($atlasPath,$png)
 $check=[Drawing.Bitmap]::new($atlasPath)
 try{
  for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
   $c=$check.GetPixel($x,$y)
   $p=[uint32]([uint64]$c.R-bor([uint64]$c.G-shl 8)-bor([uint64]$c.B-shl 16)-bor([uint64]$c.A-shl 24))
   $inEdit=if($SideLower){$x-lt 32-and $y-ge 32-and $y-lt 48}elseif($GrassEdge){$x-lt 64-and $y-lt 48-and $next[$y*64+$x]-ne $old[$y*64+$x]}elseif($GrassTop){$x-lt 32-and $y-lt 16}else{$x-lt 64-and $y-lt 48}
   $expected=if($inEdit){$next[$y*64+$x]}else{$beforePixels[$y*$check.Width+$x]}
   if($p-ne $expected){throw "PNG mismatch at $x,$y"}
  }}
 }finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas_Normal.png.bak') -Destination $atlasPath -Force
 throw
}
if($SideLower){Write-Output "SideLower normals PASS: changed pixels=$([TurfNormalCompletion]::LowerPixels); convex stones, recessed cracks, boundary row continuity; Top, SideUpper, overlays, color data and other blocks unchanged; coverage, unit vectors and PNG/draft parity verified. Backup: $backup"}
elseif($GrassEdge){Write-Output "Grass rim PASS: rounded pixels=$([TurfNormalCompletion]::EdgePixels); top/blade normals, soil, stones, overlays, color data and other blocks unchanged; coverage, unit vectors and target PNG/draft parity verified. Backup: $backup"}
elseif($GrassTop){Write-Output "Grass top PASS: strengthened facets=$([TurfNormalCompletion]::Blades); sides, overlays, substrate normals, color data and other blocks unchanged; coverage, unit vectors and target PNG/draft parity verified. Backup: $backup"}
else{Write-Output "Turf normal data PASS: 3072 pixels; authored directional side pixels preserved=$([TurfNormalCompletion]::Preserved); color data / other blocks unchanged; coverage, unit vectors, PNG/draft parity verified. Backup: $backup"}
