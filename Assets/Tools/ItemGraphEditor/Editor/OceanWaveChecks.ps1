param([switch]$Preview)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shaderPath=Join-Path $root 'Shaders/ChunkWaterProcedural.shader'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class OceanShaderCompile {
 [ComImport,Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Blob {[PreserveSig]IntPtr GetBufferPointer();[PreserveSig]UIntPtr GetBufferSize();}
 [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall)]
 static extern int D3DCompile(byte[] source,UIntPtr length,string name,IntPtr defines,IntPtr include,
  string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
 public static string Compile(string source,string entry,string target){
  byte[] bytes=Encoding.UTF8.GetBytes(source);IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
  try{int result=D3DCompile(bytes,(UIntPtr)bytes.Length,"ChunkWaterProcedural",IntPtr.Zero,IntPtr.Zero,entry,target,0x800,0,out code,out errors);
   string diagnostics="";
   if(errors!=IntPtr.Zero){var blob=(Blob)Marshal.GetObjectForIUnknown(errors);diagnostics=Marshal.PtrToStringAnsi(blob.GetBufferPointer(),checked((int)blob.GetBufferSize().ToUInt64()));Marshal.ReleaseComObject(blob);}
   if(result<0)throw new Exception(entry+" "+target+": "+diagnostics);
   return entry+"/"+target+" PASS";
  }finally{if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
 }
}
'@
$core=@'
#define TEXTURE2D(n) Texture2D<float4> n
#define SAMPLER(n) SamplerState n
#define SAMPLE_TEXTURE2D_LOD(t,s,u,l) t.SampleLevel(s,u,l)
float4 TransformWorldToHClip(float3 p) {return float4(p,1);}
'@
function ExpandLocalIncludes([string]$text){
 [regex]::Replace($text,'#include "([^"\r\n]+)"',{
  param($m)
  if($m.Groups[1].Value.StartsWith('Packages/')){return $core}
  ExpandLocalIncludes (Get-Content (Join-Path $root ('Shaders/'+$m.Groups[1].Value)) -Raw)
 })
}
$shader=Get-Content $shaderPath -Raw
$program=[regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
$expanded=ExpandLocalIncludes $program
foreach($clip in $false,$true){
 $text=if($clip){"#define _PLAYER_CLIPPING 1`n"+$expanded}else{$expanded}
 [OceanShaderCompile]::Compile($text,'Vert','vs_5_0')
 [OceanShaderCompile]::Compile($text,'Frag','ps_5_0')
}

$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$references=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$ocean=Get-Content (Join-Path $root 'Shaders/OceanWaves.hlsl') -Raw
$shore=Get-Content (Join-Path $root 'Shaders/ShoreFoam.hlsl') -Raw
$noise=$shore.Substring($shore.IndexOf('float ShoreNoiseHash'),$shore.IndexOf('float4 CompositeShoreWave')-$shore.IndexOf('float ShoreNoiseHash'))
$translated=($noise+"`n"+$ocean)-replace '(?m)^#.*$',''
$translated=$translated-replace '(?m)^TEXTURE2D\([^;]+;',''
$translated=$translated-replace '(?m)^(float[234]?|void) (\w+)\(', 'public static $1 $2('
$translated=$translated-replace '(?m)^(float[234]?) (_\w+);', 'public static $1 $2;'
$translated=$translated-replace '\bfloat([234])\(', 'new float$1('
$translated=$translated-replace '\b(uint|int)([234])\(', 'new $1$2('
$translated=$translated.Replace(',maskUv,variant)',',out maskUv,out variant)')
$translated=$translated-replace '\[unroll\]', ''
$translated=$translated.Replace('1u << (4u + corner)','1u << (int)(4u + corner)')
$translated=$translated-replace '(?<![\w.])((?:\d+\.\d*|\.\d+))(?![\w.])', '$1f'
$harness=@'
namespace Game.World.Chunks {public static class ChunkSettings{public const int SizeX=16,SizeZ=16,SizeY=256;}}
public class OceanFoamTexture {
 public float[] Alpha;public int Width,Height;
 public float4 Load(int3 p){if(p.x<0||p.y<0||p.x>=Width||p.y>=Height)throw new System.Exception("Atlas sample escaped bounds");
  return new float4(1,1,1,Alpha[(Height-1-p.y)*Width+p.x]);}
}
public static class OceanWaveMathChecks {
 public static OceanFoamTexture _OceanFoamAtlas=new OceanFoamTexture();
 public static void SetAtlas(float[] alpha,int w,int h){_OceanFoamAtlas.Alpha=alpha;_OceanFoamAtlas.Width=w;_OceanFoamAtlas.Height=h;
  _OceanFoamAtlas_TexelSize=new float4(1f/w,1f/h,w,h);_OceanFoamAvailable=1;}
 public static float4 _WorldWind,_ShoreViewRight,_ShoreViewAway,_ShoreWindDrift;
 public static float _ShoreFoamTime,_ShoreWindWaveTime;
 public static float fwidth(float value)=>0; // Derivatives are validated by the real shader compiler above.
 TRANSLATED
 static void Check(bool valid,string message){if(!valid)throw new System.Exception(message);}
 public static float[] Preview(int width,int height){
  var result=new float[width*height];
  for(int y=0;y<height;y++)for(int x=0;x<width;x++){
   float2 world=new float2(x/32f,y/16f);
   float2 projected=_OceanSurfaceOrigin.xy+new float2(dot(world,_ShoreViewRight.xy),dot(world,_ShoreViewAway.xy)*.5f);
   GetOceanWaves(projected,new float2(1,.5f),new float2(.5f),0,out var n,out var foam);
   result[y*width+x]=foam;
  }
  return result;
 }
 public static string Run(){
  _OceanWaveStrength=.75f;_OceanWaveLength=3.4f;_OceanWaveSpeed=1;_OceanNormalStrength=.22f;
  _OceanFoamStrength=.6f;_OceanFoamCoverage=.65f;_WorldWind=new float4(.8f,.6f,.7f,1);
  _OceanFoamPatchLength=2.8f;_OceanFoamPatchDepth=1;
  float2 world=new float2(-32.375f,48.625f),drift=new float2(.17f,-.23f),size=new float2(1,.5f);
  float3 first=default;float foamFirst=0;
  foreach(Game.World.Rendering.ViewDirection view in System.Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))){
   float2 forward=Game.World.Rendering.ViewDirectionUtility.Forward(view);
   float2 right=new float2(forward.y,-forward.x),away=-forward;
   _ShoreViewRight=new float4(right,0,0);_ShoreViewAway=new float4(away,0,0);
   float2 origin=Game.World.Rendering.WorldPositionProjectionUtility.Project(new float3(0,63,0),view)+new float2(0,.25f);
   _OceanSurfaceOrigin=new float4(origin,0,1);
   _ShoreWindDrift=new float4(dot(drift,right),dot(drift,away)*.5f,0,0);
   float2 projected=Game.World.Rendering.WorldPositionProjectionUtility.Project(new float3(world.x,63,world.y),view)+new float2(0,.25f);
   Check(distance(OceanWorldPosition(projected,size),world)<.0001f,"View rotation moved the world wave pattern");
   // Independently use the renderer's actual Top-quad geometry, not Project()
   // on both sides of the assertion. Exercise negative chunk boundaries too.
   foreach(int2 cell in new[]{new int2(-33,48),new int2(-16,-17),new int2(0,0),new int2(16,15)}){
    int2 chunk=(int2)floor((float2)cell/16f),local=cell-chunk*16;
    var v=Game.World.Rendering.ViewCoordinateUtility.WorldToView(new int3(local.x,63,local.y),new int2(16),view);
    float3 rp=Game.World.Rendering.ChunkRenderPositionUtility.GetPosition(chunk,view,1,.5f,0);
    float py=(256-1-63)*2+v.V;
    float2 rendered=new float2(rp.x-8+v.U+.25f,rp.y+264-(py+1)*.5f+.75f*.5f);
    float2 expected=(float2)cell+.5f+right*(-.25f)+forward*(-.25f);
    Check(distance(OceanWorldPosition(rendered,size),expected)<.0001f,"Sea-plane origin does not match rendered Top quad");
   }
   GetOceanWaves(projected,size,new float2(.5f),0,out float3 n,out float foam);
   Check(all(isfinite(n))&&abs(length(n)-1)<.0001f&&n.y>.5f&&foam>=0&&foam<=1,"Invalid wave output");
   if(forward.y==1){first=n;foamFirst=foam;}else Check(distance(first,n)<.0001f&&abs(foamFirst-foam)<.0001f,"Wave normal/foam changed with camera rotation");
   GetOceanWaves(projected,size,new float2(.5f),0,out float3 again,out float againFoam);
   Check(all(again==n)&&againFoam==foam,"Frozen clocks still animate");
   _OceanWaveStrength=0;GetOceanWaves(projected,size,new float2(.5f),0,out n,out foam);
   Check(all(n==new float3(0,1,0))&&foam==0,"Zero strength does not restore original water");_OceanWaveStrength=.75f;
  }
  Check(OceanShoreFade(new float2(.5f),0)==1,"Open ocean suppressed");
  float2 ridgeUv=new float2(.5f,.2f),tailUv=new float2(.5f,.8f);
  Check(OceanFoamEvolution(1,ridgeUv,0,.1f)==0,"Foam precedes the building ridge");
  Check(OceanFoamEvolution(1,ridgeUv,0,.34f)>OceanFoamEvolution(1,tailUv,0,.34f),"Leading foam does not break first");
  Check(OceanFoamEvolution(1,ridgeUv,0,.7f)==0,"Ridge foam outlives the break");
  float survivingTail=0;
  for(int x=1;x<31;x++)survivingTail+=OceanFoamEvolution(1,new float2((x+.5f)/32,.8f),0,.7f);
  Check(survivingTail>0,"No lingering tail after the ridge collapses");
  Check(OceanFoamEvolution(1,tailUv,0,.99f)==0,"Foam survives cycle reset");
  Check(OceanFoamEvolution(0,tailUv,0,.4f)==0,"Lifecycle adds foam outside artwork");
  for(int tile=0;tile<4;tile++){
   int occupied=0,rows=0;
   for(int y=0;y<16;y++){bool row=false;for(int x=0;x<32;x++){
    float sample=SampleOceanFoamMask(new float2((x+.5f)/32,(y+.5f)/16),tile);
    float expected=_OceanFoamAtlas.Alpha[(tile/2*16+y)*64+tile%2*32+x];
    Check(sample==expected,"Atlas quadrant/orientation or logical sample mismatch");
    if(sample>0){occupied++;row=true;}
    if(x==0||x==31||y==0||y==15)Check(sample==0,"Foam crosses cell borders");
   }if(row)rows++;}
   Check(occupied>20&&occupied<360&&rows>=4,"Foam patch is blank, solid or only a thin line");
  }
  int placements=0;
  for(int band=-4;band<=4;band++){
   for(int j=-1024;j<=1024;j++){
    float life=OceanFoamPlacement(j/16f,0,band,7,.8f,1,out float2 uv,out float variant);
    Check(variant>=0&&variant<=3&&variant==floor(variant),"Invalid mask variant");
    if(life>0){Check(all(uv>=0)&&all(uv<=1)&&life<=1,"Invalid mask footprint");placements++;}
   }
  }
  Check(placements>0,"No textured foam patches placed");
  for(int band=-4;band<=4;band++){
   float span=max(2.5f,_OceanFoamPatchLength*1.6f);
   float offset=ShoreNoiseHash(new float2(band,91.7f))*span;
   for(int segment=-8;segment<=8;segment++){
    float boundary=segment*span-offset;
    float left=OceanPatchAge(boundary-.0001f,band,7);
    float right=OceanPatchAge(boundary+.0001f,band,7);
    float delta=abs(left-right);
    Check(min(delta,1-delta)<.001f,"Lifetime seam at old segment boundary");
    Check(OceanConnectedRidge(boundary,0,band,.34f)>0,"Connected ridge cut at old segment boundary");
    Check(OceanConnectedRidge(boundary,1,band,.34f)==0,"Connected ridge escapes wave neighbourhood");
   }
  }
  Check(OceanConnectedRidge(0,0,0,.1f)==0&&OceanConnectedRidge(0,0,0,.7f)==0,"Connected ridge ignores break lifecycle");
  Check(OceanFoamPlacement(0,100,0,7,.8f,1,out var farUv,out var farVariant)==0,"Foam escapes crest neighbourhood");
  OceanFoamPlacement(0,.05f,0,7,.8f,1,out var frontUv,out var unusedA);
  OceanFoamPlacement(0,-.05f,0,7,.8f,1,out var backUv,out var unusedB);
  Check(frontUv.y<backUv.y,"Foam tail faces ahead of leading crest");
  for(uint i=0;i<4;i++){
   float2 uv=i==0?new float2(.5f,1):i==1?new float2(1,.5f):i==2?new float2(.5f,0):new float2(0,.5f);
   Check(OceanShoreFade(uv,1u<<(int)i)==0,"Shore edge not protected");
   float2 vertex=i==0?new float2(1,1):i==1?new float2(1,0):i==2?new float2(0,0):new float2(0,1);
   Check(OceanShoreFade(vertex,1u<<(int)(4+i))==0,"Rock corner not protected");
  }
  int visible=0;float sum=0;
  for(int z=-64;z<64;z++)for(int x=-64;x<64;x++){
   float2 projected=_OceanSurfaceOrigin.xy+new float2(-z*.25f,-x*.125f);
   GetOceanWaves(projected,size,new float2(.5f),0,out var n,out float foam);
   Check(all(isfinite(n))&&isfinite(foam)&&foam>=0&&foam<=1,"Nonfinite ocean sample");
   if(foam>.005f)visible++;sum+=foam;
  }
  Check(visible>0&&visible<16384/4,"Crests are absent or blanket the ocean");
  return $"Ocean math PASS: production HLSL expressions via math adapter, four views, negative world coordinates, integrated drift, frozen time, disable, shore/diagonal fade, finite sparse crests ({visible}/16384). GPU timing/image and derivative filtering not tested.";
 }
}
'@
$harness=$harness.Replace('TRANSLATED',$translated)
$harness=$harness-replace '(?<![\w.])(frac|dot|min|max|abs|length|smoothstep|normalize|floor|sin|saturate|lerp|all|isfinite|distance|step|clamp)\(', 'math.$1('
$projectionSources=''
foreach($file in 'ViewDirection.cs','ViewDirectionUtility.cs','ViewCoordinate.cs','ViewBounds.cs','ViewCoordinateUtility.cs','ChunkRenderPositionUtility.cs','WorldPositionProjectionUtility.cs'){
 $projectionSources+=(Get-Content (Join-Path $root ('Scripts/World/Rendering/'+$file)) -Raw)-replace '(?m)^using [^;]+;\r?\n',''
}
Add-Type -TypeDefinition ("using Unity.Mathematics;using Game.World.Chunks;`n"+$projectionSources+"`n"+$harness) -ReferencedAssemblies $references
Add-Type -AssemblyName System.Drawing
$mask=[Drawing.Bitmap]::new((Join-Path $root 'Resources/OceanFoamAtlas.png'))
try{
 if($mask.Width-ne 64-or $mask.Height-ne 32){throw 'Unexpected foam atlas dimensions.'}
 $maskValues=[float[]]::new($mask.Width*$mask.Height)
 for($y=0;$y-lt $mask.Height;$y++){for($x=0;$x-lt $mask.Width;$x++){
  $alpha=$mask.GetPixel($x,$y).A;if($alpha-notin @(0,80,160,255)){throw 'Intermediate alpha introduced into logical foam atlas.'}
  $maskValues[$y*$mask.Width+$x]=$alpha/255.0
 }}
 [OceanWaveMathChecks]::SetAtlas($maskValues,$mask.Width,$mask.Height)
}finally{$mask.Dispose()}
[OceanWaveMathChecks]::Run()
if($Preview){
 Add-Type -AssemblyName System.Drawing
 $w=768;$h=512;$values=[OceanWaveMathChecks]::Preview($w,$h)
 $bitmap=[Drawing.Bitmap]::new($w,$h)
 try{for($y=0;$y-lt $h;$y++){for($x=0;$x-lt $w;$x++){
  $foam=$values[$y*$w+$x]
  # Diagnostic of actual foam mask, not a capture of Unity lighting/rendering.
  $color=[Drawing.Color]::FromArgb([int](40+95*$foam),[int](61+100*$foam),[int](58+98*$foam))
  $bitmap.SetPixel($x,$y,$color)
 }};$bitmap.Save((Join-Path $root '.codex-ocean-crest-preview.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
}
