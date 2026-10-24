$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/SkyLightSystem.cs') -Raw
$methods = $source.Substring($source.IndexOf('        private static void Calculate('))
$methods = $methods.Substring(0, $methods.LastIndexOf('    }'))
$methods = $methods.Replace('private static void Calculate(', 'public static void Calculate(')
# Compare the previous control flow with the production method bodies.
$old = $methods.Replace('for (int y = ChunkSettings.SizeY - 1;', 'bool openToSky = true; for (int y = ChunkSettings.SizeY - 1;')
$old = $old.Replace('// Everything below is shadowed; lateral light is handled by SpreadLight.' + "`r`n" + '                            break;', 'openToSky = false; continue;')
$old = $old.Replace('// Everything below is shadowed; lateral light is handled by SpreadLight.' + "`n" + '                            break;', 'openToSky = false; continue;')
$old = $old.Replace('SetSkyLight(voxelLight, index, MaximumLight);', 'if (!openToSky) continue; SetSkyLight(voxelLight, index, MaximumLight);')
$old = $old.Replace('if (BlockLightGeometry.IsHalf(block)) break;', 'if (BlockLightGeometry.IsHalf(block)) openToSky = false;')
$old = [regex]::Replace($old, 'if \(voxelLight\[index\]\.Sky >= light\)\s*return;', '')
$old = $old.Replace('if (BlockLightGeometry.IsFull(targetBlock) ||', 'if (voxelLight[index].Sky >= light || BlockLightGeometry.IsFull(targetBlock) ||')
# Restore the old order too, so geometry access counts represent the prior code.
$old = $old.Replace('if (voxelLight[index].Sky >= light || BlockLightGeometry.IsFull(targetBlock) ||', 'if (BlockLightGeometry.IsFull(targetBlock) || voxelLight[index].Sky >= light ||')
if ($old -eq $methods -or !$old.Contains('bool openToSky')) { throw 'Baseline reconstruction failed' }
$adapter = @'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
public enum Allocator { Temp }
public class NativeQueue<T> : Queue<T> { public NativeQueue(Allocator a) {} public void Dispose() {} }
public class DynamicBuffer<T> { public T[] Data; public long Reads; public int Length => Data.Length;
 public DynamicBuffer(int n) { Data = new T[n]; } public T this[int i] { get { Reads++; return Data[i]; } set { Data[i] = value; } } }
public enum BlockId { Air, Stone, OceanWater, RiverWater }
public enum BlockShape { Full, LowerHalf, UpperHalf }
public struct BlockData { public BlockId BlockId; public BlockShape Shape; }
public struct SplitCellData {}
// Inputs are resolved geometry views, covering all full/upper/lower/water states.
public static class SplitCellUtility { public static bool IsSplit(BlockData b) => false;
 public static Geometry Get(DynamicBuffer<BlockData> b, DynamicBuffer<SplitCellData> s, int i) => new Geometry { Value=b[i] }; }
public struct Geometry { public BlockData Value; public BlockData GeometryView() => Value; }
public static class BlockUtility { public static bool IsSolid(BlockId id) => id != BlockId.Air; }
public struct VoxelLightData { public byte Sky,R,G,B; }
public static class ChunkSettings { public const int SizeX=16,SizeZ=16,SizeY=256; }
public static class ChunkUtility { public static int ToIndex(int x,int y,int z) => x+16*(z+16*y);
 public static bool IsInside(int x,int y,int z) => x>=0&&x<16&&z>=0&&z<16&&y>=0&&y<256; }
'@
$geometry = Get-Content (Join-Path $root 'Scripts/World/Lighting/Utilities/VoxelShadowUtility.cs') -Raw
$geometry = $geometry.Substring($geometry.IndexOf('    public static class BlockLightGeometry'))
$geometry = $geometry.Substring(0, $geometry.LastIndexOf('}'))
$tests = @'
public static class SkyChecks {
 public static string Run() {
  long before=0,after=0;
  for(int scene=0;scene<9;scene++) {
   var blocks=new DynamicBuffer<BlockData>(65536); var splits=new DynamicBuffer<SplitCellData>(0);
   var a=new DynamicBuffer<VoxelLightData>(65536); var b=new DynamicBuffer<VoxelLightData>(65536);
   var random=new System.Random(7100+scene);
   for(int y=0;y<256;y++) for(int z=0;z<16;z++) for(int x=0;x<16;x++) {
    int i=ChunkUtility.ToIndex(x,y,z);
    BlockData block=default;
    if(scene==1 || (scene==2&&y<90) || (scene>=3&&y<100+(x+z)%9)) block.BlockId=BlockId.Stone;
    if(scene>=3&&y>65&&y<110&&random.Next(4)==0) { block.BlockId=(BlockId)random.Next(4); block.Shape=(BlockShape)random.Next(3); }
    if(scene==3&&x==8) block=default; // open shaft through cave/overhangs
    if(scene==4&&y==255) block=new BlockData {BlockId=BlockId.Stone,Shape=BlockShape.UpperHalf};
    if(scene==5&&y==255) block=new BlockData {BlockId=BlockId.Stone,Shape=BlockShape.LowerHalf};
    blocks[i]=block;
    a[i]=b[i]=new VoxelLightData {Sky=9,R=3,G=7,B=11};
   }
   blocks.Reads=0; Previous.Calculate(blocks,splits,a); before+=blocks.Reads;
   blocks.Reads=0; Current.Calculate(blocks,splits,b); after+=blocks.Reads;
   for(int i=0;i<65536;i++) if(a[i].Sky!=b[i].Sky || b[i].R!=3 || b[i].G!=7 || b[i].B!=11)
    throw new Exception("Skylight/RGB mismatch scene="+scene+" voxel="+i);
  }
  if(after>=before) throw new Exception("No reduction in block reads");
  return "PASS: identical skylight for 9 full-size chunks (air, solid, terrain, caves, shaft, upper/lower halves, water); RGB preserved. Block reads: "+before+" -> "+after+". Managed adapters, not Unity FPS.";
 }
}
'@
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
Add-Type -TypeDefinition ($adapter+$geometry+'public static class Current { const byte MaximumLight=15,SpreadAttenuation=2;'+$methods+'}public static class Previous { const byte MaximumLight=15,SpreadAttenuation=2;'+$old+'}'+$tests) -ReferencedAssemblies $refs
[SkyChecks]::Run()
$update=$source.Substring($source.IndexOf('public void OnUpdate'),$source.IndexOf('        private static void Calculate(')-$source.IndexOf('public void OnUpdate'))
if ($update -match 'DynamicBuffer<VoxelLightData>|GetBufferLookup<VoxelLightData>\(' -or
 ([regex]::Matches($update,'state.EntityManager.GetBuffer<VoxelLightData>\(entity\)')).Count -ne 2 -or
 $update.IndexOf('state.EntityManager.GetBuffer<VoxelLightData>(entity)') -lt $update.IndexOf('if (!needsSkyLight.ValueRO')) {
 throw 'Skylight must acquire writable voxel buffers only after a pending-work guard, not through broad query access'
}
foreach($path in @('Scripts/World/Lighting/Systems/ChunkLightingSystem.cs','Scripts/World/Lighting/Systems/ChunkLocalLightUpdateSystem.cs')) {
 $reader=Get-Content (Join-Path $root $path) -Raw
 if ($reader -notmatch 'GetBufferLookup<VoxelLightData>\(true\)' -or
  $reader -match 'SystemAPI.Query<[^;]*?DynamicBuffer<VoxelLightData>' -or
  $reader -match 'GetBufferLookup<VoxelLightData>\(\)') {
  throw "Projection light consumer must not acquire writable voxel buffers: $path"
 }
}
'PASS voxel-light access contracts: lazy writable skylight buffers and read-only projection light lookups. Actual ECS change-version/FPS validation remains in Unity.'
