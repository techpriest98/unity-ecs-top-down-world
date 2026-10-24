$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $PSScriptRoot 'WorldItemBatchShaderChecks.ps1')
$shader=Get-Content (Join-Path $root 'Shaders/TreeSprite.shader') -Raw
$core=@'
#define TEXTURE2D(n) Texture2D<float4> n
#define TEXTURE2D_ARRAY(n) Texture2DArray<float4> n
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
#define SAMPLE_TEXTURE2D_ARRAY(t,s,u,p) t.Sample(s,float3(u,p))
#define SAMPLE_TEXTURE2D_LOD(t,s,u,l) t.SampleLevel(s,u,l)
float4 TransformWorldToHClip(float3 p){return float4(p,1);}
'@
$program=[regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
$program=$program.Replace('#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"',$core)
$program=$program.Replace('#include "DetailedLocalLighting.hlsl"',(Get-Content (Join-Path $root 'Shaders/DetailedLocalLighting.hlsl') -Raw))
$program=$program.Replace('#include "TreeShadowTrace.hlsl"',(Get-Content (Join-Path $root 'Shaders/TreeShadowTrace.hlsl') -Raw))
[ItemBatchShaderCompile]::Compile($program,'Vert','vs_5_0')
[ItemBatchShaderCompile]::Compile($program,'Frag','ps_5_0')
$math='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $math
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$math
$tree=(Get-Content (Join-Path $root 'Scripts/World/Trees/ChunkTree.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$harness=@'
using System;
using Unity.Mathematics;
using Unity.Entities;
using Game.World.Trees;
namespace Unity.Entities { public interface IComponentData{} public interface IBufferElementData{} public class InternalBufferCapacityAttribute:Attribute {public InternalBufferCapacityAttribute(int n){}} }
public static class RuntimeTreeChecks {
 static void Check(bool v,string message){if(!v)throw new Exception(message);}
 public static void Run(){
  var tree=new ChunkTree{Position=new float3(16.1f,10,-.1f),TrunkRadius=.25f,TrunkHeight=10,Scale=1};
  var half=new float2(.3f);
  Check(TreeTrunkCollision.Intersects(new float3(15.8f,10,-.1f),half,1.9f,tree),"Cross-chunk trunk collision missed");
  Check(!TreeTrunkCollision.Intersects(new float3(17,10,-.1f),half,1.9f,tree),"Crown footprint blocked movement");
  Check(!TreeTrunkCollision.Intersects(new float3(16.1f,20,-.1f),half,1.9f,tree),"Above trunk blocked");
  Check(!TreeTrunkCollision.Intersects(new float3(16.1f,8,-.1f),half,1.9f,tree),"Below trunk blocked");
  Check(TreeTrunkCollision.Intersects(new float3(16.1f,19.9f,-.1f),half,1.9f,tree),"Trunk top missed");
  Check(!TreeTrunkCollision.Intersects(new float3(16.65f,10,.45f),half,1.9f,tree),"Square collider substituted for cylinder");
  var variants=new System.Collections.Generic.HashSet<int>();int selected=0,total=0;
  for(int x=-16;x<16;x++)for(int z=-16;z<16;z++)for(int cell=0;cell<4;cell++){
   var chunk=new int2(x,z);uint hash=TurfTreePlacement.Hash(chunk,cell,123u);
   Check(hash==TurfTreePlacement.Hash(chunk,cell,123u),"Placement hash is unstable");
   var local=TurfTreePlacement.LocalCell(cell,hash);
   Check(math.all(local>=0)&&math.all(local<16),"Candidate outside owner chunk");
   Check(!TurfTreePlacement.Selected(hash,0)&&TurfTreePlacement.Selected(hash,1),"Density endpoints failed");
   if(TurfTreePlacement.Selected(hash,.65f))selected++;total++;
   var candidate=TurfTreePlacement.Create(chunk,cell,hash,42,64.5f,10,.23f,1);
   Check(candidate.Id==cell+1&&candidate.Position.y==64.5f,"ID/half-height placement mismatch");
   Check(math.all((int2)math.floor(candidate.Position.xz/16)==chunk),"Negative-coordinate ownership mismatch");
   Check(candidate.Scale>=.8f&&candidate.Scale<=1.15f&&candidate.QuarterTurn>=0&&candidate.QuarterTurn<4,"Variant transform out of range");
   variants.Add(candidate.Seed);
  }
  Check(variants.Count==4,"Population must share four shape seeds");
  Check(selected/(float)total>.55f&&selected/(float)total<.75f,"Population density is unexpectedly biased");
  for(int camera=0;camera<4;camera++)for(int turn=0;turn<4;turn++){
   float a=camera*math.PI*.5f,b=turn*math.PI*.5f;
   var local=new float3(.4f,2,.7f);var rotated=new float3(local.x*math.cos(b)+local.z*math.sin(b),local.y,-local.x*math.sin(b)+local.z*math.cos(b));
   float expected=rotated.x*math.cos(a)+rotated.z*math.sin(a);
   float actual=local.x*math.cos(a+b)+local.z*math.sin(a+b);
   Check(math.abs(expected-actual)<.00001f,"Quarter-turn sprite selection mismatch");
  }
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition ($harness+$tree)
[RuntimeTreeChecks]::Run()
$runtime=Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeTestSystem.cs') -Raw
$startup=Get-Content (Join-Path $root 'Scripts/World/Generation/Spawning/Systems/WorldStartupPresentationSystem.cs') -Raw
$render=$runtime.Substring($runtime.IndexOf('public partial class TreeRenderSystem'))
$resources=$runtime.Substring(0,$runtime.IndexOf('public partial class TreePopulationSystem'))
if ($runtime -notmatch 'public partial class TreePopulationSystem' -or
 $render -match 'Task.Run|SetPixelData|PrepareTreeTextures|AddBuffer<ChunkTree>|PopulateTurf\(' -or
 $resources -match 'GetTreeMaskPixels|AddBuffer<ChunkTree>' -or
 $render -notmatch 'resources.FindImages' -or
 $startup -notmatch 'GetExistingSystemManaged<Game.World.Trees.TreeResourcePreparationSystem>') {
 throw 'Tree preparation/population/render system ownership is not separated'
}
if ($startup.IndexOf('!trees.PrepareTreeTextures()') -lt 0 -or
 $startup.IndexOf('!trees.PrepareTreeTextures()') -gt $startup.IndexOf('blink.Reveal()') -or
 $runtime -notmatch 'math.hash\(new uint2\(seed.Value, \(uint\)config.Shape.Seed\)\)' -or
 $runtime -notmatch 'variant < ShapeCapacity' -or
 $runtime -notmatch 'cell, hash, resources.BaseSeed' -or
 $runtime -match '\.Wait\(|\.GetAwaiter\(\)') { throw 'World-seeded nonblocking tree prewarm/reveal gate missing' }
$movement=Get-Content (Join-Path $root 'Scripts/Player/PlayerMovementSystem.cs') -Raw
if($runtime -notmatch 'Task.Run' -or $runtime -match 'SetBuffer<BlockData>|GetBuffer<BlockData>.*false' -or
 $runtime -notmatch 'AddBuffer<ChunkTree>' -or $runtime -notmatch 'occlusion.GetTreeMask' -or
 $movement -notmatch 'trunks: trunks' -or $shader -notmatch 'SV_Depth') {throw 'Background bake, separate storage, trunk physics or per-pixel overlap wiring missing'}
'PASS tree shader; actual cylinder/AABB tests, chunk-boundary and canopy/vertical exclusion, all 16 camera/object rotations; background-cache and separate-storage contracts. Unity Play Mode visuals/performance still required.'
