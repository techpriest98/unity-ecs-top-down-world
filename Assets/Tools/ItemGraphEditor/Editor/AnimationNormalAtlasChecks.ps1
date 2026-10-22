$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$builder=Get-Content (Join-Path $root 'Tools/CharacterAnimationEditor/Editor/AnimationTextureArrayBuilder.cs') -Raw
$start=$builder.IndexOf('        private static void CopyPainted(')
$end=$builder.IndexOf('        private static AnimationDefinition.FrameLayer[] GetEntries(', $start)
if($start -lt 0 -or $end -lt 0){throw 'Atlas copy methods not found'}
$copy=$builder.Substring($start,$end-$start)
$harness=@'
using System;
struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} }
struct RectInt { public int x,y,width,height; public RectInt(int x,int y,int w,int h){this.x=x;this.y=y;width=w;height=h;} }
static class Mathf { public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(v,b)); }
public static class AnimationNormalAtlasChecks {
 const int Padding=1,PageSize=16;
 class PendingFrame { public Color32[] SourcePixels,NormalPixels,PaintedPixels; public int SourceWidth,SourceX,SourceY; }
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 public static void Run(){
  var source=new PendingFrame {SourceWidth=12,SourceX=4,SourceY=3,
   SourcePixels=new Color32[144],NormalPixels=new Color32[144]};
  for(int i=0;i<144;i++){source.SourcePixels[i]=new Color32((byte)i,1,2,255);source.NormalPixels[i]=new Color32(3,(byte)i,4,255);}
  var rect=new RectInt(2,5,3,4);var diffuse=new Color32[256];var normal=new Color32[256];
  CopyFrame(source,diffuse,rect);CopyNormals(source,normal,rect);
  var painted=new Color32[256];CopyPainted(source,painted,rect);
  for(int y=-1;y<5;y++)for(int x=-1;x<4;x++){
   int index=(rect.y+y)*16+rect.x+x;
   int expected=(3+Mathf.Clamp(y,0,3))*12+4+Mathf.Clamp(x,0,2);
   Check(diffuse[index].r==expected && normal[index].g==expected,"Diffuse/normal coordinates or padding differ");
   Check(painted[index].r==diffuse[index].r,"Absent painted map must copy clean diffuse");
  }
  source.PaintedPixels=new Color32[144];
  for(int i=0;i<144;i++)source.PaintedPixels[i]=new Color32(99,88,77,255);
  source.PaintedPixels[3*12+4]=new Color32(255,255,255,0);
  CopyPainted(source,painted,rect);
  Check(painted[5*16+2].r==diffuse[5*16+2].r,"Painted hole must fall back to diffuse");
  Check(painted[6*16+3].r==99,"Painted texture not copied at shared placement");
  source.NormalPixels=null;CopyNormals(source,normal,rect);
  for(int y=-1;y<5;y++)for(int x=-1;x<4;x++){
   var n=normal[(rect.y+y)*16+rect.x+x];
   Check(n.r==128&&n.g==128&&n.b==255&&n.a==0,"Missing map must select legacy lighting");
  }
 }
COPY_METHODS
}
'@
Add-Type -TypeDefinition $harness.Replace('COPY_METHODS',$copy)
[AnimationNormalAtlasChecks]::Run()
if($builder -notmatch 'TextureFormat.RGBA32, false, true' -or
 $builder -notmatch 'CopyNormals\(item, pages\[pageIndex\].Normals, atlasRect\)'){
 throw 'Normal array must be linear and reuse diffuse placement'
}
$shader=Get-Content (Join-Path $root 'Shaders/PlayerNormalTest.shader') -Raw
if($shader -match '_BodyTest|_BodyPainted|AmbientDetail|_BodyNormalTest' -or
 $shader -notmatch 'SAMPLE_TEXTURE2D_ARRAY\(_NormalArray, player_point_clamp_sampler, uv, page\)'){
 throw 'Shader must use matching normal atlas UV/page without test textures or painted detail'
}
'PASS actual atlas copy methods: identical diffuse/normal placement and extruded padding; missing-map fallback; linear array and production shader contracts.'
