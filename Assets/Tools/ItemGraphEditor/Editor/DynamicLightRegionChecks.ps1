$ErrorActionPreference='Stop'
$regionRoot=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$regionMath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $regionMath
$regionRefs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$regionMath
$regionSystem=Get-Content (Join-Path $regionRoot 'Scripts/World/Lighting/Systems/DynamicLightSystem.cs') -Raw
$regionClear=$regionSystem.Substring($regionSystem.IndexOf('        private static uint PackRgb'),$regionSystem.IndexOf('        private void ClearLocalLight')-$regionSystem.IndexOf('        private static uint PackRgb'))
$regionSpread=$regionSystem.Substring($regionSystem.IndexOf('        private static void SpreadSource'),$regionSystem.IndexOf('        private static bool IsOpaque')-$regionSystem.IndexOf('        private static void SpreadSource'))
$regionHelpers=$regionSystem.Substring($regionSystem.IndexOf('        private static byte ToByte'))
$regionHelpers=$regionHelpers.Substring(0,$regionHelpers.LastIndexOf("`n    }"))
$regionState=(Get-Content (Join-Path $regionRoot 'Scripts/World/Lighting/Components/DynamicLightSourceState.cs') -Raw)-replace '(?m)^using [^;]+;\r?\n',''
$regionSource=(Get-Content (Join-Path $regionRoot 'Scripts/World/Lighting/Components/DynamicLightSource.cs') -Raw)-replace '(?m)^using [^;]+;\r?\n',''
$regionHarness=@'
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Game.World.Chunks;
using Game.World.Blocks;
namespace Unity.Entities {public interface IComponentData {} public interface IEnableableComponent {} public struct Entity {public int Index;} }
namespace Unity.Collections {
 public class NativeList<T>:List<T> {public int Length=>Count;}
 public class NativeParallelHashSet<T>:HashSet<T> {}
 public class NativeParallelHashMap<K,V>:Dictionary<K,V> where K:notnull {
  public new int Capacity {get;set;}=1;
  public new int Count()=>base.Count;
 }
}
namespace Game.World.Blocks {
 public struct BlockData {public bool Solid;}
 public static class BlockLightGeometry {public static bool IsFull(BlockData block)=>block.Solid;}
}
namespace Game.World.Chunks {
 public static class ChunkSettings {public const int SizeX=16,SizeZ=16,SizeY=256;}
 public class ChunkBlockAccessor {
  public HashSet<int3> Solids=new();
  public bool TryGetBlock(int2 c,int x,int y,int z,out BlockData b) {b=new BlockData{Solid=Solids.Contains(new int3(c.x*16+x,y,c.y*16+z))};return y>=0&&y<256;}
 }
}
namespace Game.World.Lighting {
 public struct VoxelLightData {public byte R,G,B,Sky;}
 public class ChunkVoxelLightAccessor {
  public Dictionary<int3,VoxelLightData> Data=new();
  public bool TryGet(int2 c,int x,int y,int z,out VoxelLightData light) {
   var p=new int3(c.x*16+x,y,c.y*16+z);if(!Data.TryGetValue(p,out light))light=new VoxelLightData{Sky=8};return y>=0&&y<256;
  }
  public bool TrySet(int2 c,int x,int y,int z,VoxelLightData light,out Entity entity,out int2 coordinate) {
   var p=new int3(c.x*16+x,y,c.y*16+z);coordinate=(int2)math.floor((float2)p.xz/16f);entity=new Entity{Index=coordinate.x*1000+coordinate.y};Data[p]=light;return true;
  }
 }
 public static class DynamicLightVisibilityUtility {
  public static int Rays;
  // Visibility is an adapter; the production DDA itself is unchanged.
  public static bool IsVisible(int2 c,int3 from,int3 to,ChunkBlockAccessor blocks) {
   Rays++;var start=(float3)from+.5f;var delta=(float3)(to-from);int n=(int)math.ceil(math.length(delta)*4);
   for(int i=1;i<n;i++){var p=(int3)math.floor(start+delta*(i/(float)n));if(!blocks.TryGetBlock(c,p.x,p.y,p.z,out var b)||b.Solid)return false;}return true;
  }
 }
 public static class DynamicRegionChecks {
  private const float MaximumRadius=8f;
'@
$regionTests=@'
  static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
  static DynamicLightSource S(float3 color,float radius=5,float intensity=1)=>new DynamicLightSource{Color=color,Radius=radius,Intensity=intensity};
  static void Full((DynamicLightSource s,float3 p)[] sources,ChunkBlockAccessor blocks,ChunkVoxelLightAccessor light) {
   foreach(var key in light.Data.Keys.ToArray()){var v=light.Data[key];v.R=v.G=v.B=0;light.Data[key]=v;}
   foreach(var s in sources)SpreadSource(s.s,s.p,blocks,light,new(),new(),false,new(),new());
  }
  static DynamicLightBounds B((DynamicLightSource s,float3 p) s)=>DynamicLightBounds.Around((int3)math.floor(s.p),s.s.Radius);
  static void Partial((DynamicLightSource s,float3 p)[] sources,NativeList<DynamicLightBounds> bounds,ChunkBlockAccessor blocks,ChunkVoxelLightAccessor light) {
   var old=new NativeParallelHashMap<int3,uint>();ClearRegions(bounds,light,ref old);
   foreach(var s in sources)SpreadSource(s.s,s.p,blocks,light,new(),new(),true,bounds,old);
  }
  static void Same(ChunkVoxelLightAccessor a,ChunkVoxelLightAccessor b) {
   foreach(var p in a.Data.Keys.Union(b.Data.Keys)) {
    a.TryGet(int2.zero,p.x,p.y,p.z,out var va);b.TryGet(int2.zero,p.x,p.y,p.z,out var vb);
    Check(PackRgb(va)==PackRgb(vb)&&va.Sky==vb.Sky,"Partial RGB/sky differs from full rebuild at "+p);
   }
  }
  public static string Run() {
   var blocks=new ChunkBlockAccessor();for(int y=0;y<=14;y++)for(int z=-7;z<=7;z++)if(z!=0)blocks.Solids.Add(new int3(-2,y,z));
   var moving=(S(new float3(1,.4f,.1f)),new float3(-.5f,7,.5f));
   var neighbour=(S(new float3(.1f,.3f,1),6),new float3(3,7,0));
   var far=(S(new float3(.3f,1,.2f)),new float3(50,7,50));
   var current=new[]{moving,neighbour,far};var incremental=new ChunkVoxelLightAccessor();var baseline=new ChunkVoxelLightAccessor();Full(current,blocks,incremental);
   int before=0,after=0;
   foreach(int mode in new[]{0,1,2,3,4,5}) {
    var old=current;
    if(mode==0)current=new[]{(moving.Item1,new float3(.5f,7,0)),neighbour,far};
    if(mode==1)current=new[]{(S(new float3(.2f,1,.5f),3,.7f),new float3(.5f,7,0)),neighbour,far};
    if(mode==2)current=new[]{neighbour,far}; // remove or disable
    if(mode==3)current=new[]{(moving.Item1,new float3(-17,7,-1)),neighbour,far}; // add across negative chunk boundary
    if(mode==4)current=new[]{(moving.Item1,new float3(18,7,-1)),neighbour,far}; // disjoint teleport bounds
    if(mode==5)current=new[]{(S(new float3(1),0),new float3(18,7,-1)),neighbour,far};
    var bounds=new NativeList<DynamicLightBounds>();
    // Only the first source changed; the neighbour and distant source did not.
    if(mode!=3)bounds.Add(B(old[0]));if(mode!=2)bounds.Add(B(current[0]));
    DynamicLightVisibilityUtility.Rays=0;Partial(current,bounds,blocks,incremental);after+=DynamicLightVisibilityUtility.Rays;
    DynamicLightVisibilityUtility.Rays=0;Full(current,blocks,baseline);before+=DynamicLightVisibilityUtility.Rays;Same(incremental,baseline);
   }
   Check(after<before,"Local rebuild did not reduce visibility tests");
   // Two changed lights with overlapping bounds must clear each voxel once.
   var next=new[]{(moving.Item1,new float3(0,7,1)),(neighbour.Item1,new float3(2,7,1)),far};
   var both=new NativeList<DynamicLightBounds>{B(current[0]),B(current[1]),B(next[0]),B(next[1])};
   Partial(next,both,blocks,incremental);Full(next,blocks,baseline);Same(incremental,baseline);
   return $"Dynamic regions PASS: move, color/intensity/radius, remove/disable, add, negative chunks, teleport, overlapping changes, sky preservation; visibility tests {before} -> {after} in fixture. Native/ECS/GPU runtime not tested.";
  }
 }
}
'@
Add-Type -TypeDefinition ($regionHarness+$regionClear+$regionSpread+$regionHelpers+$regionTests+$regionState+$regionSource) -ReferencedAssemblies $regionRefs
[Game.World.Lighting.DynamicRegionChecks]::Run()
