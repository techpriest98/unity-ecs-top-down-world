$ErrorActionPreference = 'Stop'
$lightRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$lightMath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $lightMath
$lightReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $lightMath
$lightSource = Get-Content (Join-Path $lightRoot 'Scripts/World/Lighting/Utilities/VoxelLightSmoothingUtility.cs') -Raw
$renderSource = (Get-Content (Join-Path $lightRoot 'Scripts/World/Rendering/ProjectedCellRenderData.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$lightHarness = @'
namespace Game.World.Blocks {
 public enum BlockId:byte { Air,Stone }
 public struct BlockData { public bool Full; }
 public static class BlockLightGeometry {public static bool IsFull(BlockData block)=>block.Full;}
}
namespace Game.World.Chunks {
 public class ChunkBlockAccessor {
  public System.Collections.Generic.HashSet<int3> Solid=new System.Collections.Generic.HashSet<int3>();
  public bool TryGetBlock(int2 c,int x,int y,int z,out Game.World.Blocks.BlockData b) {
   b=new Game.World.Blocks.BlockData{Full=Solid.Contains(new int3(x,y,z))};return true;
  }
 }
}
namespace Game.World.Lighting {
 public struct VoxelLightData {public byte R,G,B,Sky;}
 public class ChunkLightAccessor {
  public System.Collections.Generic.Dictionary<int3,VoxelLightData> Values=new System.Collections.Generic.Dictionary<int3,VoxelLightData>();
  public bool TryGetLight(int2 c,int3 p,out VoxelLightData light)=>Values.TryGetValue(p,out light);
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
  Check((int)System.Runtime.InteropServices.Marshal.OffsetOf<Game.World.Rendering.ProjectedCellRenderData>("LocalLightDirection")==28,"Direction did not reuse GPU padding");
  return "Local normals PASS: six directions, solid rejection, cancellation, sky exclusion, source maximum and unchanged 48-byte GPU stride. GPU/FPS not tested.";
 }
}
'@
Add-Type -TypeDefinition ("using System.Runtime.InteropServices;`n" + $lightSource + "`n" + $renderSource + "`n" + $lightHarness) -ReferencedAssemblies $lightReferences
[LocalNormalLightChecks]::Run()
