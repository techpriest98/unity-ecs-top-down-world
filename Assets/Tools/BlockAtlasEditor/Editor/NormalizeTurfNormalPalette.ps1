param([switch]$CheckOnly,[switch]$PaletteOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$draftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$atlasPath=Join-Path $root 'Art/World/BlockAtlas_Normal.png'
$editor=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasEditorWindow.cs') -Raw
$picker=[regex]::Match($editor,'(?s)private void DrawNormalPicker\(\).*?Color32 encoded;(?<body>.*?)\s*DrawNormalSwatch\(encoded,')
if(!$picker.Success){throw 'Normal picker implementation changed; review palette extraction.'}
# Evaluate the CURRENT picker expression, including its intermediate Color32
# rounding, not idealized vectors or a hand-copied list of approximate RGBs.
$shim=@'
using System;
public struct Vector3 {
 public float x,y,z;
 public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public Vector3 normalized {get{float length=MathF.Sqrt(x*x+y*y+z*z);return new Vector3(x/length,y/length,z/length);}}
}
public struct Color {
 public float r,g,b,a;
 public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
}
public struct Color32 {
 public byte r,g,b,a;
 public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
 public static implicit operator Color32(Color c)=>new Color32(
  (byte)MathF.Round(Math.Clamp(c.r,0,1)*255f),(byte)MathF.Round(Math.Clamp(c.g,0,1)*255f),
  (byte)MathF.Round(Math.Clamp(c.b,0,1)*255f),(byte)MathF.Round(Math.Clamp(c.a,0,1)*255f));
}
public static class Mathf {
 public static float Sqrt(float value)=>MathF.Sqrt(value);
 public static int RoundToInt(float value)=>(int)MathF.Round(value);
}
public static class BlockAtlasEntry {
 public static Color32 NorthNormal {get{Vector3 n=new Vector3(0,1,1).normalized;return new Color(.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);}}
}
public static class TurfPaletteRestriction {
 public static Color32 Get(int row,int col){Color32 encoded;
 PICKER_BODY
 return encoded;}
 public static uint[] Palette(){var p=new uint[12];for(int r=0;r<4;r++)for(int c=0;c<3;c++){
  var v=Get(r,c);p[r*3+c]=(uint)(v.r|(v.g<<8)|(v.b<<16));
 }return p;}
 static double[] Decode(uint p){double x=(p&255)/127.5-1,y=((p>>8)&255)/127.5-1,z=((p>>16)&255)/127.5-1;
  double l=Math.Sqrt(x*x+y*y+z*z);return new[]{x/l,y/l,z/l};}
 public static uint Snap(uint p,uint[] palette){
  uint rgb=p&0xFFFFFFu;foreach(uint q in palette)if(q==rgb)return p;
  double[] v=Decode(p);double best=-2;uint selected=0;
  foreach(uint q in palette){var n=Decode(q);double dot=v[0]*n[0]+v[1]*n[1]+v[2]*n[2];
   if(dot>best){best=dot;selected=q;}}
  return selected|(p&0xFF000000u);
 }
}
'@
Add-Type -TypeDefinition ($shim.Replace('PICKER_BODY',$picker.Groups['body'].Value))
[uint32[]]$palette=[TurfPaletteRestriction]::Palette()
if(@($palette|Select-Object -Unique).Count-ne 12){throw 'Expected 12 distinct picker colors.'}
if($PaletteOnly){$palette;return}
$original=[IO.File]::ReadAllText($draftPath)
$entry=[regex]::Match($original,'(?ms)^  - Id: 9\r?\n.*?(?=^  - Id:|\z)')
if(!$entry.Success){throw 'Turf ID 9 missing.'}
$parts=$entry.Value-split '    NormalPixels:'
if($parts.Count-ne 2){throw 'Unexpected draft serialization.'}
[uint32[]]$old=@([regex]::Matches($parts[1],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
if($old.Length-ne 3072){throw 'Expected 64x48 normal data.'}
$db=Get-Content (Join-Path $root 'Configs/BlockDatabase.asset') -Raw
if($db-notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0'){throw 'Atlas mapping changed.'}
function PackColor($c){[uint32]([uint64]$c.R-bor([uint64]$c.G-shl 8)-bor([uint64]$c.B-shl 16)-bor([uint64]$c.A-shl 24))}
function PixelColor([uint32]$p){[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))}
$next=[uint32[]]::new(3072);$changed=0;$visibleChanged=0
for($i=0;$i-lt 3072;$i++){
 $next[$i]=[TurfPaletteRestriction]::Snap($old[$i],$palette)
 if(($next[$i]-band 16777215)-notin $palette){throw 'Snap emitted a non-palette RGB.'}
 if(($next[$i]-shr 24)-ne($old[$i]-shr 24)){throw 'Alpha changed.'}
 if($next[$i]-ne $old[$i]){$changed++;if(($old[$i]-shr 24)-ne 0){$visibleChanged++}}
}
$bitmap=[Drawing.Bitmap]::new($atlasPath)
$before=[uint32[]]::new($bitmap.Width*$bitmap.Height)
try{
 for($y=0;$y-lt $bitmap.Height;$y++){for($x=0;$x-lt $bitmap.Width;$x++){
  $before[$y*$bitmap.Width+$x]=PackColor ($bitmap.GetPixel($x,$y))
 }}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){
  if($before[$y*$bitmap.Width+$x]-ne $old[$y*64+$x]){throw "Draft/runtime normals differ at $x,$y; refusing to overwrite unsynchronized painting."}
  $bitmap.SetPixel($x,$y,(PixelColor $next[$y*64+$x]))
 }}
 $memory=[IO.MemoryStream]::new()
 try{$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$png=$memory.ToArray()}finally{$memory.Dispose()}
}finally{$bitmap.Dispose()}
if($CheckOnly){
 if($changed-ne 0){throw "Non-palette normal pixels: $changed total, $visibleChanged visible."}
 Write-Output 'Turf palette CHECK PASS: all 3072 draft/runtime pixels use exact current picker RGBs, including transparent texels.'
 exit
}
$backup=Join-Path $root 'Docs/Backups/TurfBeforeNormalPaletteRestriction'
if(Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')){throw 'Backup exists; refusing to overwrite subsequent painting.'}
$script:pixelIndex=0
$normalText=[regex]::Replace($parts[1],'rgba: (\d+)',{param($m) $p=$next[$script:pixelIndex];$script:pixelIndex++;'rgba: '+$p})
$newEntry=$parts[0]+'    NormalPixels:'+$normalText
$updated=$original.Substring(0,$entry.Index)+$newEntry+$original.Substring($entry.Index+$entry.Length)
if($updated.Replace($newEntry,'')-cne $original.Replace($entry.Value,'')){throw 'Other draft entries changed.'}
if(($newEntry-split '    NormalPixels:')[0]-cne $parts[0]){throw 'Color data changed.'}
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $draftPath -Destination (Join-Path $backup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $backup 'BlockAtlas_Normal.png.bak')
try{
 [IO.File]::WriteAllText($draftPath,$updated,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($atlasPath,$png)
 $check=[Drawing.Bitmap]::new($atlasPath)
 try{
  for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
   $actual=PackColor ($check.GetPixel($x,$y))
   $expected=if($x-lt 64-and $y-lt 48){$next[$y*64+$x]}else{$before[$y*$check.Width+$x]}
   if($actual-ne $expected){throw "PNG mismatch at $x,$y"}
  }}
 }finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas_Normal.png.bak') -Destination $atlasPath -Force
 throw
}
Write-Output "Turf palette PASS: $changed RGBs corrected ($visibleChanged visible); zero off-palette pixels, alpha preserved, color data/other blocks untouched, draft/runtime parity verified."
Write-Output ('Exact picker RGBs: '+(($palette|ForEach-Object{"$($_-band 255),$(($_-shr 8)-band 255),$(($_-shr 16)-band 255)"})-join ' | '))
