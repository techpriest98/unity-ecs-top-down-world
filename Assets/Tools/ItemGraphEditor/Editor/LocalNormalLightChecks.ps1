$ErrorActionPreference = 'Stop'
$lightRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$lightMath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $lightMath
$lightReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $lightMath
$lightSource = Get-Content (Join-Path $lightRoot 'Scripts/World/Lighting/Utilities/VoxelLightSmoothingUtility.cs') -Raw
$renderSource = (Get-Content (Join-Path $lightRoot 'Scripts/World/Rendering/ProjectedCellRenderData.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$lightHarness = @'
namespace Unity.Collections {
 public enum Allocator {Temp}
 public struct NativeParallelHashMap<K,V> : System.IDisposable where K:notnull {
  private System.Collections.Generic.Dictionary<K,V> values;
  public NativeParallelHashMap(int capacity,Allocator allocator) {values=new System.Collections.Generic.Dictionary<K,V>();Capacity=capacity;}
  public bool IsCreated=>values!=null;
  public int Capacity {get;set;}
  public int Count()=>values.Count;
  public bool TryGetValue(K key,out V value)=>values.TryGetValue(key,out value);
  public bool TryAdd(K key,V value)=>values.TryAdd(key,value);
  public void Dispose(){values=null;}
 }
}
namespace Game.World.Blocks {
 public enum BlockId:byte { Air,Stone }
 public struct BlockData { public bool Full; }
 public static class BlockLightGeometry {public static bool IsFull(BlockData block)=>block.Full;}
}
namespace Game.World.Chunks {
 public static class ChunkSettings {public const int SizeX=16,SizeZ=16;}
 public class ChunkBlockAccessor {
  public System.Collections.Generic.HashSet<int3> Solid=new System.Collections.Generic.HashSet<int3>();
  public bool TryGetBlock(int2 c,int x,int y,int z,out Game.World.Blocks.BlockData b) {
   b=new Game.World.Blocks.BlockData{Full=Solid.Contains(new int3(x+c.x*16,y,z+c.y*16))};return true;
  }
 }
}
namespace Game.World.Lighting {
 public struct VoxelLightData {public byte R,G,B,Sky;}
 public class ChunkLightAccessor {
  public int Reads;
  public System.Collections.Generic.Dictionary<int3,VoxelLightData> Values=new System.Collections.Generic.Dictionary<int3,VoxelLightData>();
  public bool TryGetLight(int2 c,int3 p,out VoxelLightData light) {Reads++;return Values.TryGetValue(p+new int3(c.x*16,0,c.y*16),out light);}
 }
}
namespace Game.World.Rendering {
 public enum ProjectedFaceType:byte { Top }
 public static class ProjectedCellPacking {
  public static Game.World.Blocks.BlockId GetBlockId(uint value)=>0;
  public static ProjectedFaceType GetFaceType(uint value)=>0;
  public static ushort GetX(uint value)=>0;
  public static ushort GetY(uint value)=>0;
 }
}
public static class LocalNormalLightChecks {
 static void Check(bool ok,string text) {if(!ok)throw new System.Exception(text);}
 static Game.World.Lighting.VoxelLightData L(byte value,byte sky=0)=>new Game.World.Lighting.VoxelLightData{R=value,G=value,B=value,Sky=sky};
 static float3 Decode(uint packed)=>math.normalizesafe(new float3(packed&255u,(packed>>8)&255u,(packed>>16)&255u)/127.5f-1);
 public static string Run() {
  var blocks=new Game.World.Chunks.ChunkBlockAccessor();var lights=new Game.World.Lighting.ChunkLightAccessor();
  foreach(var axis in new[]{new int3(1,0,0),new int3(-1,0,0),new int3(0,1,0),new int3(0,-1,0),new int3(0,0,1),new int3(0,0,-1)}) {
   lights.Values.Clear();blocks.Solid.Clear();
   lights.Values[int3.zero]=L(80);lights.Values[axis]=L(100);
   uint packed=Game.World.Lighting.VoxelLightSmoothingUtility.SampleDirection(int2.zero,int3.zero,blocks,lights);
   Check((packed>>24)==255 && math.dot(Decode(packed),(float3)axis)>.999f,"Wrong world-space incoming light direction");
   blocks.Solid.Add(axis);
   Check(Game.World.Lighting.VoxelLightSmoothingUtility.SampleDirection(int2.zero,int3.zero,blocks,lights)==0,"Direction sampled light through solid wall");
   blocks.Solid.Clear();lights.Values[-axis]=L(100);
   Check(Game.World.Lighting.VoxelLightSmoothingUtility.SampleDirection(int2.zero,int3.zero,blocks,lights)==0,"Opposite equal lights did not cancel");
  }
  lights.Values.Clear();lights.Values[int3.zero]=L(0,15);lights.Values[new int3(1,0,0)]=L(0,0);
  Check(Game.World.Lighting.VoxelLightSmoothingUtility.SampleDirection(int2.zero,int3.zero,blocks,lights)==0,"Sky light leaked into local direction");
  lights.Values[int3.zero]=L(200);lights.Values[new int3(1,0,0)]=L(100);
  Check(Game.World.Lighting.VoxelLightSmoothingUtility.SampleDirection(int2.zero,int3.zero,blocks,lights)==0,"Source maximum invented an incoming direction");
  Check(System.Runtime.InteropServices.Marshal.SizeOf<Game.World.Rendering.ProjectedCellRenderData>()==48,"GPU stride changed");
  lights.Values.Clear();blocks.Solid.Clear();
  for(int x=-2;x<=3;x++)for(int y=-1;y<=1;y++)for(int z=-2;z<=3;z++)
   lights.Values[new int3(x,y,z)]=L((byte)(100+x*10));
  byte ca=Game.World.Lighting.VoxelLightSmoothingUtility.SampleCornerDirection(int2.zero,int3.zero,new int3(1,0,0),new int3(0,0,1),blocks,lights);
  byte cb=Game.World.Lighting.VoxelLightSmoothingUtility.SampleCornerDirection(int2.zero,new int3(1,0,0),new int3(-1,0,0),new int3(0,0,1),blocks,lights);
  Check(ca==cb,"Adjacent faces disagree on shared corner direction");
  Check(math.dot(Game.World.Lighting.VoxelLightSmoothingUtility.UnpackCornerDirection(ca),new float3(1,0,0))>.8f,
   "Compact corner direction points away from light");
  Check(math.length(Game.World.Lighting.VoxelLightSmoothingUtility.UnpackCornerDirection(0))==0,"Neutral corner is not neutral");
  Check((int)System.Runtime.InteropServices.Marshal.OffsetOf<Game.World.Rendering.ProjectedCellRenderData>("LocalLightDirection")==28,"Direction did not reuse GPU padding");
  var record=new Game.World.Rendering.ProjectedCellRenderData {
   BlockData=11,Position=22,FaceData=33,ChunkPosition=new float3(4,5,6)};
  Check(record.HasSameGeometry(11,22,33) && !record.HasSameGeometry(12,22,33) &&
   !record.HasSameGeometry(11,23,33) && !record.HasSameGeometry(11,22,34),"Geometry changes can enter light-only upload");
  Check(record.UpdateLighting(1,2,3,4,5,6),"Changed light data was not detected");
  Check(!record.UpdateLighting(1,2,3,4,5,6),"Unchanged light triggered a redundant upload");
  Check(record.HasSameGeometry(11,22,33) && math.all(record.ChunkPosition==new float3(4,5,6)) &&
   record.LightData==1 && record.LocalLightDirection==2 && record.LocalLightTopRight==6,"Lighting update overwrote geometry or lost channels");
  Check(Game.World.Rendering.ProjectedCellRenderData.IsValidRange(3,2,5) &&
   Game.World.Rendering.ProjectedCellRenderData.IsValidRange(5,0,5) &&
   !Game.World.Rendering.ProjectedCellRenderData.IsValidRange(-1,2,5) &&
   !Game.World.Rendering.ProjectedCellRenderData.IsValidRange(3,3,5) &&
   !Game.World.Rendering.ProjectedCellRenderData.IsValidRange(int.MaxValue,1,int.MaxValue),"Invalid GPU range accepted");
  System.Console.WriteLine("Light ranges PASS: geometry guards, unchanged-upload rejection, all light channels, stable geometry and safe range bounds. GPU calls not tested.");
  lights.Values.Clear();blocks.Solid.Clear();
  for(int x=-2;x<=18;x++)for(int y=-1;y<=1;y++)for(int z=-2;z<=18;z++)lights.Values[new int3(x,y,z)]=L((byte)(80+x*3+z));
  var expected=new byte[16*16*4];int i=0;lights.Reads=0;
  for(int z=0;z<16;z++)for(int x=0;x<16;x++)foreach(var a in new[]{-1,1})foreach(var b in new[]{-1,1})
   expected[i++]=Game.World.Lighting.VoxelLightSmoothingUtility.SampleCornerDirection(int2.zero,new int3(x,0,z),new int3(a,0,0),new int3(0,0,b),blocks,lights);
  int baselineReads=lights.Reads;lights.Reads=0;i=0;
  var cache=new Game.World.Lighting.LocalLightDirectionCache(2,Unity.Collections.Allocator.Temp);
  for(int z=0;z<16;z++)for(int x=0;x<16;x++)foreach(var a in new[]{-1,1})foreach(var b in new[]{-1,1}) {
   byte actual=Game.World.Lighting.VoxelLightSmoothingUtility.SampleCornerDirection(int2.zero,new int3(x,0,z),new int3(a,0,0),new int3(0,0,b),blocks,lights,ref cache);
   Check(actual==expected[i++],"Cache changed a corner's lighting direction");
  }
  int cachedReads=lights.Reads;
  Check(cachedReads<baselineReads/2 && cache.CacheHits>0,"Cache did not eliminate repeated light reads");
  int samples=cache.SamplesComputed;
  uint first=cache.Sample(int2.zero,int3.zero,blocks,lights);
  uint edge=cache.Sample(new int2(-1,0),new int3(16,0,0),blocks,lights);
  Check(first==edge && cache.SamplesComputed==samples,"Chunk boundary aliases were not cached together");
  cache.Dispose();
  lights.Values[int3.zero]=L(255);
  cache=new Game.World.Lighting.LocalLightDirectionCache(2,Unity.Collections.Allocator.Temp);
  Check(cache.Sample(int2.zero,int3.zero,blocks,lights)==0,"New lighting pass reused stale source data");cache.Dispose();
  System.Console.WriteLine($"Cache PASS: identical 1024 corners, light reads {baselineReads} -> {cachedReads}, negative chunk alias, growth and fresh-pass invalidation (managed map adapter).");
  return "Local normals PASS: six directions, shared corners, compact direction, solid rejection, cancellation, sky exclusion and unchanged 48-byte GPU stride. GPU/FPS not tested.";
 }
}
'@
Add-Type -TypeDefinition ("using System.Runtime.InteropServices;`n" + $lightSource + "`n" + $renderSource + "`n" + $lightHarness) -ReferencedAssemblies $lightReferences
[LocalNormalLightChecks]::Run()
