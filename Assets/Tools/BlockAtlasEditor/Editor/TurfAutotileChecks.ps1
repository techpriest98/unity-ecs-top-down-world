param([string]$DraftPath,[switch]$SkipPreview)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if(!$DraftPath){$DraftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'}
$draft=Get-Content -LiteralPath $DraftPath -Raw
function ReadPixels([int]$id){
 $entry=[regex]::Match($draft,"(?ms)^  - Id: $id\r?\n.*?(?=^  - Id:|\z)").Value
 [uint32[]]@([regex]::Matches(($entry-split '    NormalPixels:')[0],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
}
$source=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.cs') -Raw
$source=$source-replace '(?m)^using [^;]+;\r?\n',''-replace '(?m)^#if UNITY_EDITOR\r?\n',''-replace '(?m)^#endif\r?\n?',''
$shim=@'
using System;
using System.Collections.Generic;
using Game.World.Blocks;
using UnityEngine;
namespace Game.World.Blocks {public enum BlockId:byte{Air=0,Stone=2,Turf=9}}
namespace UnityEngine {
 public class ScriptableObject{}
 public class CreateAssetMenuAttribute:Attribute {public string fileName,menuName;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public Vector3 normalized{get{float l=MathF.Sqrt(x*x+y*y+z*z);return new Vector3(x/l,y/l,z/l);}}}
 public struct Vector2Int {public int x,y;public Vector2Int(int x,int y){this.x=x;this.y=y;}}
 public struct RectInt {
  public int x,y,width,height;
  public int xMin=>x;public int xMax=>x+width;public int yMin=>y;public int yMax=>y+height;
  public RectInt(int x,int y,int w,int h){this.x=x;this.y=y;width=w;height=h;}
  public bool Contains(Vector2Int p)=>p.x>=xMin&&p.x<xMax&&p.y>=yMin&&p.y<yMax;
 }
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
  public static Color white=>new Color(1,1,1,1);public static Color black=>new Color(0,0,0,1);
  public static Color gray=>new Color(.5f,.5f,.5f,1);public static Color clear=>new Color(0,0,0,0);
 }
 public struct Color32 {public byte r,g,b,a;public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
  public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
  public static implicit operator Color32(Color c)=>new Color32((byte)MathF.Round(Math.Clamp(c.r,0,1)*255f),
   (byte)MathF.Round(Math.Clamp(c.g,0,1)*255f),(byte)MathF.Round(Math.Clamp(c.b,0,1)*255f),(byte)MathF.Round(Math.Clamp(c.a,0,1)*255f));
 }
}
'@
$harness=@'
public static class TurfAutotileChecks {
 static Game.EditorTools.BlockAtlas.BlockAtlasEntry Entry(uint[] pixels){var b=new Game.EditorTools.BlockAtlas.BlockAtlasEntry();
  for(int i=0;i<pixels.Length;i++){uint p=pixels[i];b.Pixels[i]=new Color32((byte)p,(byte)(p>>8),(byte)(p>>16),(byte)(p>>24));}return b;}
 static uint P(Color32 c)=>(uint)(c.r|(c.g<<8)|(c.b<<16)|(c.a<<24));
 public static uint[] Compose(uint[] pixels,uint[] foot,int mask,bool above){
  var flags=new bool[8];for(int i=0;i<8;i++)flags[i]=(mask&(1<<i))!=0;
  var color=Game.EditorTools.BlockAtlas.BlockAtlasLayout.Compose(Entry(pixels),foot==null?null:Entry(foot),flags,above);
  var result=new uint[color.Length];for(int i=0;i<result.Length;i++)result[i]=P(color[i]);return result;
 }
 public static string Run(uint[] turf,uint[] stone){
  var allowed=new HashSet<uint>();foreach(uint p in turf)if((p>>24)!=0)allowed.Add(p);foreach(uint p in stone)if((p>>24)!=0)allowed.Add(p);
  int count=0;
  for(int mask=0;mask<256;mask++)foreach(bool above in new[]{false,true})foreach(var foot in new[]{null,turf,stone}){
   foreach(uint p in Compose(turf,foot,mask,above))if((p>>24)!=0&&!allowed.Contains(p))throw new Exception("Composition introduced color/alpha");
   count++;
  }
  var e=Game.EditorTools.BlockAtlas.BlockAtlasLayout.Elements;
  int[] a={3,3,4,4,21,21,22,22},b={5,6,5,6,19,20,19,20},corners={7,8,9,10,15,16,17,18};
  for(int i=0;i<corners.Length;i++)for(int y=0;y<4;y++)for(int x=0;x<4;x++){
   var c=e[corners[i]];int dx=c.Destination.x+x,dy=c.Destination.y+y;uint expected=0;
   foreach(int edge in new[]{a[i],b[i]}){var s=e[edge];int sx=dx-s.Destination.x,sy=dy-s.Destination.y;
    uint p=turf[(s.Source.y+sy)*64+s.Source.x+sx];if((p>>24)!=0){
     // Bottom grass belongs to the foreground foot material, not to the
     // lateral dirt rim. Its corner must prefer the foot wherever it exists.
     if(expected!=0&&p!=expected&&i<6)throw new Exception("Edge colors disagree");
     if(expected==0||i<6)expected=p;}}
   uint actual=turf[(c.Source.y+y)*64+c.Source.x+x];
   if(actual!=expected)throw new Exception("Outer corner disagrees with exposed straight edges");
  }
  foreach(int i in new[]{11,12,13,14}){var c=e[i];int opaque=0;for(int y=0;y<4;y++)for(int x=0;x<4;x++)if((turf[(c.Source.y+y)*64+c.Source.x+x]>>24)!=0)opaque++;
   if(opaque==0||opaque==16)throw new Exception("Concave corner is empty or an opaque rectangle");}
  return $"Autotile PASS: actual production Compose, {count} neighbour/above/foot combinations, exact outer-corner/strip joins, concave transparency. Headless color adapters; GPU appearance not tested.";
 }
}
'@
Add-Type -TypeDefinition ($shim+"`n"+$source+"`n"+$harness)
$turf=ReadPixels 9;$stone=ReadPixels 2
[TurfAutotileChecks]::Run($turf,$stone)
if($SkipPreview){return}
$preview=[Drawing.Bitmap]::new(832,640)
$g=[Drawing.Graphics]::FromImage($preview)
try{
 $g.Clear([Drawing.Color]::FromArgb(28,28,32))
 $cases=@(@('Isolated',0,$false,$null),@('Front edge',251,$false,$null),@('Outer corner',243,$false,$null),@('Inner corners',15,$false,$null),
  @('Stone / grass foot',0,$false,$turf,$stone),@('Above + foot',0,$true,$turf),@('Left edge',247,$false,$null),@('Right edge',253,$false,$null))
 # View-space bits: back=1 right=2 front=4 left=8, then the four diagonals.
 $font=[Drawing.Font]::new('Segoe UI',10)
 try{for($n=0;$n-lt $cases.Count;$n++){
  $item=$cases[$n];$ox=($n%4)*208+8;$oy=[int][Math]::Floor($n/4)*320+24
  $g.DrawString($item[0],$font,[Drawing.Brushes]::White,[single]$ox,[single]($oy-22))
  $main=if($item.Count-gt 4){$item[4]}else{$turf}
  $pixels=[TurfAutotileChecks]::Compose($main,$item[3],[int]$item[1],[bool]$item[2])
  for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 32;$x++){
   $p=$pixels[$y*32+$x];if(($p-shr 24)-eq 0){continue}
   $c=[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))
   for($dy=0;$dy-lt 6;$dy++){for($dx=0;$dx-lt 6;$dx++){$preview.SetPixel($ox+$x*6+$dx,$oy+$y*6+$dy,$c)}}
  }}
 }}finally{$font.Dispose()}
 $preview.Save((Join-Path $root 'Docs/References/Turf-Autotile-Color-Preview.png'),[Drawing.Imaging.ImageFormat]::Png)
}finally{$g.Dispose();$preview.Dispose()}
