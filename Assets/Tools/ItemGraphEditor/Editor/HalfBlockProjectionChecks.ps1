# Headless algorithm checks: actual projection sources and Unity math,
# managed buffer/occupancy adapters. This does not test GPU rendering or ECS safety.
$ErrorActionPreference = 'Stop'
$halfAssetsRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$halfMathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $halfMathPath
$halfReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $halfMathPath
$halfFiles = @(
 'Scripts/World/Blocks/BlockId.cs','Scripts/World/Blocks/BlockShape.cs','Scripts/World/Blocks/BlockUtility.cs',
 'Scripts/World/Blocks/Components/BlockData.cs','Scripts/World/Blocks/SplitCellUtility.cs',
 'Scripts/Items/BlockPlacementMode.cs',
 'Scripts/World/Generation/WorldBiome.cs','Scripts/World/Generation/Components/WorldGenerationSettingsComponent.cs',
 'Scripts/World/Generation/Biomes/BiomeTerrainSample.cs',
 'Scripts/World/Generation/Biomes/RockyShore/RockyShoreTerrain.cs',
 'Scripts/World/Generation/Biomes/RockyShore/Components/RockyShoreSettingsComponent.cs',
 'Scripts/World/Chunks/ChunkSettings.cs','Scripts/World/Chunks/ChunkUtility.cs',
 'Scripts/World/Rendering/ProjectedFaceType.cs','Scripts/World/Rendering/Components/ProjectedCellData.cs',
 'Scripts/World/Rendering/ProjectedCellPacking.cs','Scripts/World/Rendering/Components/ProjectedWaterCellData.cs',
 'Scripts/World/Rendering/ProjectedClipCell.cs','Scripts/World/Rendering/ProjectionOccupancy.cs',
 'Scripts/World/Rendering/ProjectionWriter.cs','Scripts/World/Rendering/WaterProjectionWriter.cs',
 'Scripts/World/Rendering/ViewDirection.cs','Scripts/World/Rendering/ViewDirectionUtility.cs',
 'Scripts/World/Rendering/ViewCoordinate.cs','Scripts/World/Rendering/ViewBounds.cs',
 'Scripts/World/Rendering/ViewCoordinateUtility.cs','Scripts/World/Rendering/Autotiling/TopNeighborMaskUtility.cs',
 'Scripts/World/Rendering/ChunkProjectionBuilder.cs','Scripts/World/Rendering/ChunkProjectionBuilder.Split.cs',
 'Scripts/World/Interaction/Components/SelectedProjectedCell.cs','Scripts/World/Interaction/ProjectedCellInteractionUtility.cs',
 'Scripts/Player/Collision/Components/PlayerCollisionShape.cs','Scripts/Player/Collision/PlayerCollisionUtility.cs',
 'Scripts/World/Saving/WorldStateData.cs','Scripts/World/Saving/WorldStateStorage.cs',
 'Scripts/World/Saving/Components/WorldBlockChanges.cs',
 'Scripts/World/Lighting/Utilities/VoxelShadowUtility.cs')
$halfUsings = @('using System;','using System.Collections.Generic;','using System.Linq;','using Game.World.Rendering;')
$halfBodies = foreach ($halfFile in $halfFiles) {
 $halfText = Get-Content -LiteralPath (Join-Path $halfAssetsRoot $halfFile) -Raw
 $halfUsings += [regex]::Matches($halfText, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
 [regex]::Replace($halfText, '(?m)^using [^;]+;\r?\n', '')
}
$halfHarness = @'
// Save validation runs against the real storage source; engine IO/JSON and
// unrelated item fields are adapters, so this does not test Unity JSON or disk IO.
namespace UnityEngine {
 public static class JsonUtility {
  public static void FromJsonOverwrite(string json,object value)=>throw new NotSupportedException();
  public static string ToJson(object value)=>throw new NotSupportedException();
 }
 public static class Debug { public static void LogException(Exception exception) {} }
}
namespace Game.Items {
 public enum EquipmentSlot { LeftHand=1,RightHand=2 }
 public enum PlacementSelectionKind { None=0,Block=1,Decoration=2 }
 public struct PlacementSelection { public PlacementSelectionKind Kind; public int Id; }
 public struct ItemInstance {
  public int DefinitionId;
  public float Durability,MaxDurability,DurabilityElapsed;
  public PlacementSelection PlacementSelection;
  public BlockPlacementMode PlacementMode;
 }
}
namespace Game.World.Saving {
 public static class WorldMetadataStorage { public const string RootPath="unused"; }
}
namespace Unity.Entities {
 public interface IComponentData {}
 public interface IBufferElementData {}
 [AttributeUsage(AttributeTargets.Struct)] public class InternalBufferCapacityAttribute : Attribute { public InternalBufferCapacityAttribute(int n) {} }
 public struct DynamicBuffer<T> : IEnumerable<T> {
  List<T> data;
  public DynamicBuffer(List<T> data) { this.data=data; }
  public bool IsCreated => data!=null;
  public int Length => data?.Count ?? 0;
  public T this[int i] { get=>data[i]; set=>data[i]=value; }
  public void Add(T item)=>data.Add(item);
  public void RemoveAtSwapBack(int index) {data[index]=data[data.Count-1];data.RemoveAt(data.Count-1);}
  public IEnumerator<T> GetEnumerator()=>data.GetEnumerator();
  System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
 }
}
namespace Unity.Collections {
 public struct NativeParallelMultiHashMap<K,V> {
  class Storage {public List<(K key,V value)> Entries=new List<(K,V)>();public int Capacity;}
  Storage data;
  public struct Iterator {internal K Key;internal int Index;}
  public NativeParallelMultiHashMap(int capacity,Allocator allocator) {data=new Storage {Capacity=capacity};}
  public int Capacity {get=>data.Capacity;set=>data.Capacity=value;}
  public int Count()=>data.Entries.Count;
  public void Add(K key,V value)=>data.Entries.Add((key,value));
  public bool TryGetFirstValue(K key,out V value,out Iterator iterator) {
   iterator=new Iterator {Key=key,Index=-1};return TryGetNextValue(out value,ref iterator);
  }
  public bool TryGetNextValue(out V value,ref Iterator iterator) {
   while(++iterator.Index<data.Entries.Count) {
    var entry=data.Entries[iterator.Index];
    if(EqualityComparer<K>.Default.Equals(entry.key,iterator.Key)) {value=entry.value;return true;}
   }
   value=default;return false;
  }
  public void SetValue(V value,Iterator iterator)=>data.Entries[iterator.Index]=(iterator.Key,value);
 }
 public enum Allocator {Temp}
 public struct NativeParallelHashMap<K,V> {
  Dictionary<K,V> data;
  public NativeParallelHashMap(int capacity,Allocator a) {data=new Dictionary<K,V>();}
  public bool ContainsKey(K key)=>data.ContainsKey(key);
  public bool TryGetValue(K key,out V value)=>data.TryGetValue(key,out value);
  public void Add(K key,V value)=>data.Add(key,value);
  public void Clear()=>data.Clear();
  public bool IsCreated=>data!=null;
  public void Dispose(){}
 }
}
namespace Game.World.Chunks {
 public struct ChunkBlockAccessor {
  public static long ReadCount;
  Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData> data;
  Dictionary<Unity.Mathematics.int3,Game.World.Blocks.CellContents> contents;
  public ChunkBlockAccessor(Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData> data,
   Dictionary<Unity.Mathematics.int3,Game.World.Blocks.CellContents> contents=null) {this.data=data;this.contents=contents;}
  public bool HasSplitCells(Unity.Mathematics.int2 coordinate) => contents!=null && contents.Keys.Any(p=>
   (int)Math.Floor(p.x/16.0)==coordinate.x && (int)Math.Floor(p.z/16.0)==coordinate.y);
  public bool TryGetRawBlock(Unity.Mathematics.int2 coordinate,int x,int y,int z,out Game.World.Blocks.BlockData value) {
   ReadCount++;
   data.TryGetValue(new Unity.Mathematics.int3(coordinate.x*16+x,y,coordinate.y*16+z),out value);
   return y>=0 && y<256;
  }
  public Game.World.Blocks.CellContents GetContentsOrAir(Unity.Mathematics.int2 coordinate,int x,int y,int z) {
   var key=new Unity.Mathematics.int3(coordinate.x*16+x,y,coordinate.y*16+z);
   ReadCount++;
   if(contents!=null && contents.TryGetValue(key,out var value)) return value;
   data.TryGetValue(key,out var raw);return Game.World.Blocks.CellContents.FromLegacy(raw);
  }
  public Game.World.Blocks.BlockData GetBlockOrAir(Unity.Mathematics.int2 coordinate,int x,int y,int z) {
   ReadCount++;
   var key=new Unity.Mathematics.int3(coordinate.x*16+x,y,coordinate.y*16+z);
   if(contents!=null && contents.TryGetValue(key,out var value)) return value.GeometryView();
   data.TryGetValue(key,out var result);return result;
  }
 }
}
public static class HalfProjectionChecks {
 static void Check(bool ok,string label) {if(!ok)throw new Exception(label);}
 static (List<Game.World.Rendering.ProjectedCellData> solid,List<Game.World.Rendering.ProjectedCellData> water) Build(
  Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData> map,Game.World.Rendering.ViewDirection view,bool clip=false,
  Dictionary<Unity.Mathematics.int3,Game.World.Blocks.CellContents> contents=null,float playerHeight=7f) {
  map=new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>(map);
  if(contents!=null) foreach(var pair in contents) map[pair.Key]=new Game.World.Blocks.BlockData {Shape=Game.World.Blocks.SplitCellUtility.ReferenceShape};
  var source=new List<Game.World.Blocks.BlockData>(new Game.World.Blocks.BlockData[16*256*16]);
  foreach(var entry in map) if(Game.World.Chunks.ChunkUtility.IsInside(entry.Key.x,entry.Key.y,entry.Key.z)) source[Game.World.Chunks.ChunkUtility.ToIndex(entry.Key)]=entry.Value;
  var solid=new List<Game.World.Rendering.ProjectedCellData>();var water=new List<Game.World.Rendering.ProjectedWaterCellData>();
  var opaque=new Game.World.Rendering.ProjectionOccupancy(1024,Unity.Collections.Allocator.Temp);
  var translucent=new Game.World.Rendering.ProjectionOccupancy(1024,Unity.Collections.Allocator.Temp);
  var clips=new Unity.Entities.DynamicBuffer<Game.World.Rendering.ProjectedClipCell>(new List<Game.World.Rendering.ProjectedClipCell>());
  Game.World.Rendering.ChunkProjectionBuilder.Build(new Unity.Entities.DynamicBuffer<Game.World.Blocks.BlockData>(source),
   Unity.Mathematics.int2.zero,new Game.World.Chunks.ChunkBlockAccessor(map,contents),
   new Game.World.Rendering.ProjectionWriter(opaque,new Unity.Entities.DynamicBuffer<Game.World.Rendering.ProjectedCellData>(solid),clips),
   new Game.World.Rendering.WaterProjectionWriter(opaque,translucent,new Unity.Entities.DynamicBuffer<Game.World.Rendering.ProjectedWaterCellData>(water)),
   view,clip,new Unity.Mathematics.float3(7,playerHeight,7));
  return (solid,water.Select(x=>x.Value).ToList());
 }
 public static string Run() {
  CheckRockyShore();
  CheckTerrainMinimap();
  CheckSplitStorage();
  CheckSaveValidation();
  CheckPlacement();
  CheckRemoval();
  CheckPlayerCollision();
  CheckWorldItemCollision();
  CheckSplitProjection();
  var p=new Unity.Mathematics.int3(7,7,7);
  var full=new Game.World.Blocks.BlockData(Game.World.Blocks.BlockId.Stone,255);
  var half=new Game.World.Blocks.BlockData(Game.World.Blocks.BlockId.Stone,255,Game.World.Blocks.BlockShape.LowerHalf);
  Check(!Game.World.Lighting.BlockLightGeometry.IsFull(half),"Half upper gap rejected as full voxel");
  Check(Game.World.Lighting.BlockLightGeometry.IsFull(full),"Full voxel lost light blocking");
  Check(!Game.World.Lighting.BlockLightGeometry.Intersects(half,Unity.Mathematics.int3.zero,
   new Unity.Mathematics.float3(-1f,.75f,.5f),new Unity.Mathematics.float3(1,0,0),3f),"Light blocked above half");
  Check(Game.World.Lighting.BlockLightGeometry.Intersects(half,Unity.Mathematics.int3.zero,
   new Unity.Mathematics.float3(-1f,.25f,.5f),new Unity.Mathematics.float3(1,0,0),3f),"Light leaked through lower half");
  Check(Game.World.Lighting.BlockLightGeometry.Intersects(half,Unity.Mathematics.int3.zero,
   new Unity.Mathematics.float3(.5f,2f,.5f),new Unity.Mathematics.float3(0,-1,0),3f),"Downward light leaked through half");
  foreach(Game.World.Rendering.ViewDirection view in Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))) {
   var floorPosition=new Unity.Mathematics.int3(7,7,7)-Game.World.Rendering.ViewDirectionUtility.GetAwayFromCameraOffset(view)*2;
   foreach(var floorMaterial in new[]{BlockId.Sand,BlockId.OceanWater}) {
    var floorMap=new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{floorPosition,new Game.World.Blocks.BlockData(floorMaterial,255,Game.World.Blocks.BlockShape.LowerHalf)}};
    var visible=Build(floorMap,view,playerHeight:7.5f);
    var clippedFloor=Build(floorMap,view,true,playerHeight:7.5f);
    var originalFaces=floorMaterial==BlockId.Sand?visible.solid:visible.water;
    var remainingFaces=floorMaterial==BlockId.Sand?clippedFloor.solid:clippedFloor.water;
    Check(remainingFaces.Count==originalFaces.Count && remainingFaces.Any(c=>c.FaceType==ProjectedFaceType.Top && !c.IsCutTop),
     $"Half floor at feet was clipped: {view}, {floorMaterial}");
   }
   var wallMap=new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{floorPosition,new Game.World.Blocks.BlockData(BlockId.Stone,255)}};
   var cutWall=Build(wallMap,view,true,playerHeight:7.5f).solid;
   Check(cutWall.Any(c=>c.FaceType==ProjectedFaceType.SideLower && !c.IsCutSide) &&
    !cutWall.Any(c=>c.FaceType==ProjectedFaceType.SideUpper && !c.IsCutSide) && cutWall.Any(c=>c.IsCutTop),
    $"Full wall did not clip at half height: {view}");
   var clipPosition=new Unity.Mathematics.int3(7,8,7)-Game.World.Rendering.ViewDirectionUtility.GetAwayFromCameraOffset(view)*2;
   foreach(var clipShape in new[]{Game.World.Blocks.BlockShape.Full,Game.World.Blocks.BlockShape.LowerHalf}) {
    var waterMap=new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{clipPosition,new Game.World.Blocks.BlockData(Game.World.Blocks.BlockId.OceanWater,255,clipShape)}};
    var visibleWater=Build(waterMap,view).water;
    var cutWater=Build(waterMap,view,true).water;
    Check(visibleWater.Count>0 && cutWater.Count==0,$"Water ignored near clipping: {view}, {clipShape}");
   }
   Game.World.Chunks.ChunkBlockAccessor.ReadCount=0;
   var a=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,full}},view).solid;
   long fullReads=Game.World.Chunks.ChunkBlockAccessor.ReadCount;
   Game.World.Chunks.ChunkBlockAccessor.ReadCount=0;
   var b=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,half}},view).solid;
   long halfReads=Game.World.Chunks.ChunkBlockAccessor.ReadCount;
   Check(halfReads<=fullReads+1024,$"One half block triggered chunk-wide extra lookups: {view}/{fullReads}/{halfReads}");
   Console.WriteLine($"Projection lookup check {view}: full={fullReads}, one half={halfReads}");
   Check(a.Count==3,$"Full isolated count: {view}/{a.Count}");
   Check(b.Count==2,$"Half isolated count: {view}/{b.Count}");
   var topA=a.Single(x=>x.FaceType==Game.World.Rendering.ProjectedFaceType.Top);
   var topB=b.Single(x=>x.FaceType==Game.World.Rendering.ProjectedFaceType.Top);
   Check(topB.ProjectionY==topA.ProjectionY+1,$"Half Top height: {view}");
   Check(b.All(x=>x.IsLowerHalf && x.FaceType!=Game.World.Rendering.ProjectedFaceType.SideLower),$"Half geometry flags: {view}");
   Check(b.Single(x=>x.FaceType==Game.World.Rendering.ProjectedFaceType.SideUpper).ProjectionY==
     a.Single(x=>x.FaceType==Game.World.Rendering.ProjectedFaceType.SideLower).ProjectionY,$"Half Side texture slot: {view}");
   var away=Game.World.Rendering.ViewDirectionUtility.GetAwayFromCameraOffset(view);
   var topNeighbours=new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,half},{p+away,full}};
   byte halfMask=Game.World.Rendering.TopNeighborMaskUtility.Build(Unity.Mathematics.int2.zero,p,
    new Game.World.Chunks.ChunkBlockAccessor(topNeighbours),view);
   Check((halfMask & Game.World.Rendering.TopNeighborMaskUtility.NorthBit)!=0,$"Full wall must close half Top rim: {view}");
   topNeighbours[p]=full; topNeighbours[p+away]=half;
   byte fullMask=Game.World.Rendering.TopNeighborMaskUtility.Build(Unity.Mathematics.int2.zero,p,
    new Game.World.Chunks.ChunkBlockAccessor(topNeighbours),view);
   Check((fullMask & Game.World.Rendering.TopNeighborMaskUtility.NorthBit)==0,$"Lower neighbour must not close full Top rim: {view}");
   var mixed=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,half},{p+away,full}},view).solid;
   ushort front=(ushort)Game.World.Chunks.ChunkUtility.ToIndex(p);
   Check(mixed.Any(x=>!x.IsLowerHalf && x.FaceType==Game.World.Rendering.ProjectedFaceType.SideUpper && x.SourceAirIndex==front),$"Full upper face behind half: {view}");
   var raised=mixed.Single(x=>!x.IsLowerHalf && x.FaceType==Game.World.Rendering.ProjectedFaceType.SideUpper && x.SourceAirIndex==front);
   Check(raised.FootBlockId==half.BlockId && (raised.BlockData & Game.World.Rendering.ProjectedCellPacking.RaisedFootFlag)!=0,
    $"Half Top overlay missing on wall behind: {view}");
   Check(a.All(x=>(x.BlockData & Game.World.Rendering.ProjectedCellPacking.RaisedFootFlag)==0),$"Raised overlay leaked onto normal wall: {view}");
   Check(!mixed.Any(x=>!x.IsLowerHalf && x.FaceType==Game.World.Rendering.ProjectedFaceType.SideLower && x.SourceAirIndex==front),$"Full lower face leaked through half: {view}");
   var stacked=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,half},{p+new Unity.Mathematics.int3(0,1,0),full}},view).solid;
   Check(stacked.Any(x=>x.IsLowerHalf && x.FaceType==Game.World.Rendering.ProjectedFaceType.Top),$"Half Top under overhang: {view}");
   var water=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,new Game.World.Blocks.BlockData(Game.World.Blocks.BlockId.OceanWater,255,Game.World.Blocks.BlockShape.LowerHalf)}},view).water;
   Check(water.Count==2 && water.All(x=>x.IsLowerHalf && x.FaceType!=Game.World.Rendering.ProjectedFaceType.SideLower),$"Half water faces: {view}");
   var clipped=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{p,half},{p+away,full}},view,true).solid;
   Check(clipped.All(x=>!x.IsLowerHalf || x.FaceType!=Game.World.Rendering.ProjectedFaceType.SideLower),$"Clipped half has lower face: {view}");
   Check(a.All(x=>x.Durability==255 && x.MaxDurability==255),"Packing damaged durability");
  }
  var edge=Build(new Dictionary<Unity.Mathematics.int3,Game.World.Blocks.BlockData>{{new Unity.Mathematics.int3(16,7,7),half}},Game.World.Rendering.ViewDirection.Right).solid;
  Check(edge.Any(x=>x.IsLowerHalf && x.FaceType==Game.World.Rendering.ProjectedFaceType.SideUpper),"Neighbour-chunk half side missing");
  return "PASS: split storage layout, independent durability, collapse, swap-back references, change registry restore, legacy save validation v1-6 and invalid halves rejection; actual projection builder in headless buffer adapter, all 4 views, full/half faces, heights, mixed neighbours, overhang, water, clipping, chunk boundary and packing. Unity JSON, disk IO and GPU image not tested.";
 }
 static void CheckSplitStorage() {
  Check(System.Runtime.InteropServices.Marshal.SizeOf<Game.World.Blocks.BlockData>()==3,"Main cell grew");
  Check(System.Runtime.InteropServices.Marshal.SizeOf<Game.World.Blocks.SplitCellData>()==6,"Split cell is not six bytes");
  var cells=new Unity.Entities.DynamicBuffer<Game.World.Blocks.BlockData>(
   new List<Game.World.Blocks.BlockData>(new Game.World.Blocks.BlockData[65536]));
  var splits=new Unity.Entities.DynamicBuffer<Game.World.Blocks.SplitCellData>(new List<Game.World.Blocks.SplitCellData>());
  var stone=new Game.World.Blocks.BlockPart(Game.World.Blocks.BlockId.Stone,200);
  var water=new Game.World.Blocks.BlockPart(Game.World.Blocks.BlockId.OceanWater,123);
  var mixed=new Game.World.Blocks.CellContents(stone,water);
  Game.World.Blocks.SplitCellUtility.Set(cells,splits,10,mixed);
  Game.World.Blocks.SplitCellUtility.Set(cells,splits,65535,mixed);
  Check(splits.Length==2,"Split insert failed");
  var read=Game.World.Blocks.SplitCellUtility.Get(cells,splits,10);
  Check(read.Lower.Id==stone.Id && read.Lower.Durability==200 && read.Upper.Id==water.Id && read.Upper.Durability==123,"Half contents lost");
  Game.World.Blocks.SplitCellUtility.Set(cells,splits,10,new Game.World.Blocks.CellContents(stone,stone));
  Check(splits.Length==1 && !Game.World.Blocks.SplitCellUtility.IsSplit(cells[10]),"Uniform cell did not collapse");
  Check(Game.World.Blocks.SplitCellUtility.GetReferenceIndex(cells[65535])==0 && splits[0].CellIndex==65535,"Swap-back reference not repaired");
  var damaged=new Game.World.Blocks.BlockPart(stone.Id,100);
  Game.World.Blocks.SplitCellUtility.Set(cells,splits,10,new Game.World.Blocks.CellContents(stone,damaged));
  Check(splits.Length==2,"Different durability incorrectly collapsed");
  Game.World.Blocks.SplitCellUtility.SetLegacy(cells,splits,65535,new Game.World.Blocks.BlockData(stone.Id,77,Game.World.Blocks.BlockShape.LowerHalf));
  Check(splits.Length==1 && Game.World.Blocks.SplitCellUtility.GetReferenceIndex(cells[10])==0,"Legacy overwrite lost moved reference");
  read=Game.World.Blocks.SplitCellUtility.Get(cells,splits,65535);
  Check(read.Lower.Id==stone.Id && read.Lower.Durability==77 && read.Upper.Id==Game.World.Blocks.BlockId.Air,"Legacy half conversion failed");
  var air=new Game.World.Blocks.BlockPart(Game.World.Blocks.BlockId.Air,99);
  Game.World.Blocks.SplitCellUtility.Set(cells,splits,10,new Game.World.Blocks.CellContents(air,air));
  Check(splits.Length==0 && cells[10].BlockId==Game.World.Blocks.BlockId.Air && cells[10].Durability==0,"Air did not normalize/collapse");
  // A buffer can address all 65,536 cells, including split index 65,535.
  for(int i=0;i<65536;i++) Game.World.Blocks.SplitCellUtility.Set(cells,splits,i,mixed);
  Check(Game.World.Blocks.SplitCellUtility.GetReferenceIndex(cells[65535])==65535,"16-bit reference truncated");
  for(int i=0;i<65536;i++) Game.World.Blocks.SplitCellUtility.Set(cells,splits,i,new Game.World.Blocks.CellContents(stone,stone));
  Check(splits.Length==0,"Full-buffer collapse left entries behind");
  var changes=new Game.World.Saving.WorldBlockChanges {
   Values=new Unity.Collections.NativeParallelMultiHashMap<Unity.Mathematics.int2,Game.World.Saving.ChangedBlock>(4,Unity.Collections.Allocator.Temp)
  };
  changes.Record(Unity.Mathematics.int2.zero,5,mixed);
  changes.Apply(Unity.Mathematics.int2.zero,cells,splits);
  read=Game.World.Blocks.SplitCellUtility.Get(cells,splits,5);
  Check(read.Lower.Id==stone.Id && read.Upper.Id==water.Id && read.Upper.Durability==123,"Registry did not restore split contents");
  changes.Record(Unity.Mathematics.int2.zero,5,new Game.World.Blocks.CellContents(stone,stone));
  changes.Apply(Unity.Mathematics.int2.zero,cells,splits);
  Check(changes.Values.Count()==1 && splits.Length==0 && cells[5].BlockId==stone.Id,"Registry overwrite left stale split data");
 }
 static void CheckTerrainMinimap() {
  var blocks=new Unity.Entities.DynamicBuffer<BlockData>(new List<BlockData>(new BlockData[16*256*16]));
  var splits=new Unity.Entities.DynamicBuffer<SplitCellData>(new List<SplitCellData>());
  var surface=new Unity.Entities.DynamicBuffer<ChunkSurfaceSample>(new List<ChunkSurfaceSample>(new ChunkSurfaceSample[256]));
  var sand=new BlockPart(BlockId.Sand,255);var water=new BlockPart(BlockId.OceanWater,255);var air=default(BlockPart);
  foreach(int x in new[]{0,7,15}) foreach(int z in new[]{0,7,15}) {
   int column=z*16+x,index=Game.World.Chunks.ChunkUtility.ToIndex(x,7,z);
   SplitCellUtility.Set(blocks,splits,index,new CellContents(sand,water));
   Check(TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z),"Initial surface not marked changed");
   Check(surface[column].Material==BlockId.OceanWater && surface[column].HeightHalf==16,"Surface must see water above sand");
   SplitCellUtility.Set(blocks,splits,index,new CellContents(sand,air));
   Check(TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,7) &&
    surface[column].Material==BlockId.Sand && surface[column].HeightHalf==15,"Water removal must expose half sand");
   blocks[Game.World.Chunks.ChunkUtility.ToIndex(x,2,z)]=new BlockData(BlockId.Stone,255);
   Check(!TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,7),"Underground change repainted unchanged surface");
   blocks[Game.World.Chunks.ChunkUtility.ToIndex(x,9,z)]=new BlockData(BlockId.MeadowGrass,255);
   Check(TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,9) && surface[column].HeightHalf==20 &&
    surface[column].Material==BlockId.MeadowGrass,"Placement above surface not reflected");
   blocks[Game.World.Chunks.ChunkUtility.ToIndex(x,9,z)]=default;
   TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,9);
   Check(surface[column].Material==BlockId.Sand && surface[column].HeightHalf==15,"Removal lost lower half floor");
   SplitCellUtility.Set(blocks,splits,index,new CellContents(air,water));
   TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,7);
   Check(surface[column].Material==BlockId.OceanWater && surface[column].HeightHalf==16,"Upper-only water lost surface");
   SplitCellUtility.Set(blocks,splits,index,default);
   TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,7);
   Check(surface[column].Material==BlockId.Stone && surface[column].HeightHalf==6,"Removing top must reveal next sky-visible block");
  }
  Check(System.Runtime.InteropServices.Marshal.SizeOf<ChunkSurfaceSample>()<=4,"Surface cache exceeds four bytes per column");
  foreach(int centre in new[]{-33,-17,-1,0,15,16,33}) {
   var used=new HashSet<int>();
   for(int z=centre-8;z<centre+8;z++) for(int x=centre-8;x<centre+8;x++) {
    int px=TerrainMinimapUtility.RingTileOffset(x),pz=TerrainMinimapUtility.RingTileOffset(z);
    Check(px>=0 && px<=240 && pz>=0 && pz<=240 && used.Add(px+pz*256),"Ring cache overlaps or leaves texture at negative chunk edges");
    var uv=TerrainMinimapUtility.RingUv(new float2(x*16+.5f,z*16+.5f));
    Check(math.distance(uv*256,new float2(px+.5f,pz+.5f))<.001f,"Scrolling UV does not match world column texel centre");
   }
  }
  Console.WriteLine("Terrain minimap PASS: sky-visible material, water over half sand, upper-only water, edits, hidden changes and 4-byte columns");
 }
 static void CheckRockyShore() {
  var settings=new Game.World.Generation.Biomes.RockyShore.RockyShoreSettingsComponent {
   CliffWidth=20,CliffWidthVariation=.35f,CliffWidthNoiseScale=.006f,
   CliffMinHeight=10,CliffMaxHeight=24,CliffSharpness=3,CliffNoiseScale=.02f,
   GrassDepth=3,RampNoiseScale=.008f,RampThreshold=.65f,RampStrength=.8f
  };
  var terrain=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(settings);
  var world=new Game.World.Generation.WorldGenerationSettingsComponent {SeaLevelHeight=64};
  var beach=terrain.Sample(0,0,63.5f,0,1,123,world);
  Check(beach.Height==64 && beach.HalfSurface && terrain.GetBlock(63,beach.Zone,beach.Height)==BlockId.Sand,
   "Beach lost fractional sand surface");
  var whole=terrain.Sample(0,0,64f,0,1,123,world);
  Check(whole.Height==64 && !whole.HalfSurface,"Integer beach unexpectedly became half");
  int halves=0,grassHalves=0;
  foreach(uint seed in new uint[]{1,123,777})
   for(int x=-32;x<=32;x+=4) for(int z=-16;z<=16;z+=4) for(int d=1;d<=48;d+=3) {
    var sample=terrain.Sample(x,z,65.25f,d,1,seed,world);
    var again=terrain.Sample(x,z,65.25f,d,1,seed,world);
    Check(sample.Height==again.Height && sample.HalfSurface==again.HalfSurface && sample.Zone==again.Zone,
     "Rocky Shore generation is nondeterministic");
    Check(sample.Height>=1 && sample.Height<256,"Rocky Shore height outside chunk");
    if(!sample.HalfSurface) continue;
    halves++;
    var material=terrain.GetBlock(sample.Height-1,sample.Zone,sample.Height);
    if(sample.Zone==(byte)Game.World.Generation.Biomes.RockyShore.RockyShoreZone.GrassTop) {
     Check(material==BlockId.MeadowGrass,"Half cliff summit lost grass cap");grassHalves++;
    }
    Check(terrain.GetBlock(sample.Height,sample.Zone,sample.Height)==BlockId.Air,"Terrain exceeds half surface");
   }
  Check(halves>0 && grassHalves>0,"Rocky Shore produced no half slopes or grass summits");
  Console.WriteLine("Rocky Shore PASS: fractional beach, integer heights, half slopes, grass caps and deterministic seeds");
 }
 static void CheckPlacement() {
  var selection=new Game.World.Interaction.SelectedProjectedCell {
   IsValid=true,FaceType=ProjectedFaceType.Top,ChunkCoordinate=new int2(-1,2),
   SourceAirIndex=(ushort)Game.World.Chunks.ChunkUtility.ToIndex(7,8,7),
   BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Stone,255,BlockShape.LowerHalf),ProjectedFaceType.Top,255)
  };
  var before=new int3(-9,8,39);
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out var origin,out int start);
  Check(math.all(origin==new int3(-9,7,39)) && start==1,"Half Top cursor did not target its empty upper half");
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before+new int3(1,0,0),out origin,out start);
  Check(math.all(origin==before+new int3(1,0,0)) && start==0,"Custom position inherited cursor offset");
  selection.BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Stone,255),ProjectedFaceType.Top,255);
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out origin,out start);
  Check(math.all(origin==before) && start==0,"Full Top placement shifted down");
  selection.FaceType=ProjectedFaceType.SideUpper;selection.BlockData|=ProjectedCellPacking.UpperHalfFlag;
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out origin,out start);
  Check(math.all(origin==before) && start==1,"Upper-half side placement lost its half offset");
  selection.BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Stone,255),ProjectedFaceType.SideUpper,255);
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out origin,out start,true);
  Check(math.all(origin==before) && start==1,"Half mode must target the upper side half");
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out origin,out start);
  Check(start==0,"Full mode inherited half-mode side offset");
  selection.FaceType=ProjectedFaceType.SideLower;
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before,out origin,out start,true);
  Check(math.all(origin==before) && start==0,"Half mode must target the lower side half");
  selection.FaceType=ProjectedFaceType.SideUpper;
  Game.World.Interaction.ProjectedCellInteractionUtility.GetBlockPlacementStart(selection,before+new int3(1,0,0),out origin,out start,true);
  Check(start==0,"Custom position inherited half-mode side offset");
  var old=new BlockPart(BlockId.Stone,123);
  var placed=new BlockPart(BlockId.Sand,255);
  var air=new BlockPart(BlockId.Air,0);
  var empty=new CellContents(air,air);
  var half=new CellContents(old,air);
  Check(BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Full,1,half,empty,true,
   out var first,out var second,out bool spills),"Full placement above half failed");
  Check(spills && first.Lower.Id==BlockId.Stone && first.Lower.Durability==123 && first.Upper.Id==BlockId.Sand &&
   second.Lower.Id==BlockId.Sand && second.Upper.Id==BlockId.Air,"Full placement must span two consecutive halves");
  Check(BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Half,0,empty,empty,false,
   out first,out second,out spills) && !spills && first.Lower.Id==BlockId.Sand && first.Upper.Id==BlockId.Air,"Half on flat ground failed");
  Check(BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Full,0,empty,empty,false,
   out first,out second,out spills) && !spills && first.IsUniform,"Full on flat ground failed");
  Check(BlockPlacementUtility.TryFill(old,Game.Items.BlockPlacementMode.Half,1,half,empty,false,
   out first,out second,out spills) && first.IsUniform && !spills,"Matching halves should become uniform");
  var blocked=new CellContents(old,old);
  Check(!BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Full,1,half,blocked,true,
   out first,out second,out spills) && first.Upper.Id==BlockId.Air && second.Lower.Id==old.Id,"Blocked spill mutated first cell");
  Check(!BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Full,1,half,empty,false,
   out first,out second,out spills),"Placement crossed unavailable cell/world ceiling");
  var floating=new CellContents(air,old);
  Check(BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Full,1,half,floating,true,
   out first,out second,out spills) && second.Upper.Durability==123,"Untouched upper half was replaced");
  var wet=new CellContents(old,new BlockPart(BlockId.OceanWater,255));
  Check(BlockPlacementUtility.TryFill(placed,Game.Items.BlockPlacementMode.Half,1,wet,empty,true,
   out first,out second,out spills) && first.Lower.Durability==123 && first.Upper.Id==BlockId.Sand,"Placing into water lost lower half");
  Check(!BlockPlacementUtility.TryFill(air,Game.Items.BlockPlacementMode.Half,0,empty,empty,true,
   out first,out second,out spills),"Air treated as placeable material");
  Check(!BlockPlacementUtility.TryFill(placed,(Game.Items.BlockPlacementMode)2,0,empty,empty,true,
   out first,out second,out spills),"Unknown placement mode accepted");
  Console.WriteLine("Placement PASS: full/half, full above half, atomic rejection, ceiling, water, preserved durability");
 }
 static void CheckRemoval() {
  var sand=new BlockPart(BlockId.Sand,91);
  var stone=new BlockPart(BlockId.Stone,132);
  var contents=new CellContents(stone,sand);
  Check(contents.PlacementMode==Game.Items.BlockPlacementMode.Half,"Pick mixed material must select half shape");
  Check(new CellContents(stone,stone).PlacementMode==Game.Items.BlockPlacementMode.Full,"Pick full block must select full shape");
  Check(new CellContents(stone,new BlockPart(BlockId.Stone,30)).PlacementMode==Game.Items.BlockPlacementMode.Full,
   "Pick full material with different durability must not select half shape");
  Check(new CellContents(stone,new BlockPart(BlockId.Air,0)).PlacementMode==Game.Items.BlockPlacementMode.Half,"Pick lower half shape");
  Check(new CellContents(new BlockPart(BlockId.Air,0),sand).PlacementMode==Game.Items.BlockPlacementMode.Half,"Pick upper half shape");
  var selected=new Game.World.Interaction.SelectedProjectedCell {
   IsValid=true,FaceType=ProjectedFaceType.Top,
   BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Sand,91),ProjectedFaceType.Top,255)
  };
  Check(Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,contents,out var result) &&
   result.Upper.Id==BlockId.Air && result.Lower.Id==BlockId.Stone && result.Lower.Durability==132,"Removing upper Top destroyed lower part");
  selected.FaceType=ProjectedFaceType.SideUpper;selected.BlockData|=ProjectedCellPacking.UpperHalfFlag;
  Check(Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,contents,out result) &&
   result.Lower.Durability==132 && result.Upper.Id==BlockId.Air,"Removing upper Side destroyed lower part");
  selected.BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Stone,132,BlockShape.LowerHalf),ProjectedFaceType.SideUpper,255);
  Check(Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,contents,out result) &&
   result.Lower.Id==BlockId.Air && result.Upper.Id==BlockId.Sand && result.Upper.Durability==91,"Removing lower Side destroyed upper part");
  selected.FaceType=ProjectedFaceType.Top;
  Check(Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,new CellContents(stone,stone),out result) &&
   result.IsUniform && result.Lower.Id==BlockId.Air,"Ordinary full block no longer removes as a whole");
  selected.BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.OceanWater,255),ProjectedFaceType.Top,255);
  Check(Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,
   new CellContents(stone,new BlockPart(BlockId.OceanWater,255)),out result) && result.Lower.Durability==132 && result.Upper.Id==BlockId.Air,
   "Removing upper water destroyed solid underneath");
  selected.BlockData=ProjectedCellPacking.PackBlock(new BlockData(BlockId.Snow,255),ProjectedFaceType.Top,255);
  Check(!Game.World.Interaction.ProjectedCellInteractionUtility.TryRemoveSelectedPart(selected,contents,out result),"Stale selected material deleted another part");
  Console.WriteLine("Removal PASS: upper/lower Top and Side, full blocks, water, durability and stale selections");
 }
 static void CheckPlayerCollision() {
  var shape=new Game.Player.PlayerCollisionShape {HalfExtentsXZ=new float2(.2f,.2f),Height=1.8f};
  var stone=new BlockPart(BlockId.Stone,200);
  var air=new BlockPart(BlockId.Air,0);
  foreach(var direction in new[]{new int3(1,0,0),new int3(-1,0,0),new int3(0,0,1),new int3(0,0,-1)}) {
   // Both positive and negative chunk edges exercise coordinate normalization.
   foreach(int offset in new[]{0,15,-1}) {
    var map=new Dictionary<int3,BlockData>();
    for(int z=offset-3;z<=offset+3;z++) for(int x=offset-3;x<=offset+3;x++) map[new int3(x,0,z)]=new BlockData(BlockId.Stone,200);
    var target=new int3(offset,1,offset)+direction;
    var contents=new Dictionary<int3,CellContents>{{target,new CellContents(stone,air)}};
    map[target]=new BlockData {Shape=SplitCellUtility.ReferenceShape};
    var accessor=new Game.World.Chunks.ChunkBlockAccessor(map,contents);
    var start=new float3(offset+.5f,1f,offset+.5f);
    var displacement=new float2(direction.x*.9f,direction.z*.9f);
    var end=Game.Player.PlayerCollisionUtility.MoveHorizontally(start,displacement,shape,accessor,true);
    Check(math.abs(end.y-1.5f)<.0001f && math.distance(end.xz,start.xz+displacement)<.0001f,"Failed half step at chunk edge");
    Check(Game.Player.PlayerCollisionUtility.IsGrounded(end,shape,accessor),"Half step has no support");
    Check(!Game.Player.PlayerCollisionUtility.IsPositionBlocked(end,shape,accessor),"Player embedded in half step");
    Check(Game.Player.PlayerCollisionUtility.IsPositionBlocked(end-new float3(0,.01f,0),shape,accessor),"Half support collision missing");
    var jumping=Game.Player.PlayerCollisionUtility.MoveHorizontally(start,displacement,shape,accessor,false);
    Check(jumping.y==1f && math.distance(jumping.xz,start.xz+displacement)>.05f,"Step ignored airborne/jump gate");
    contents[target]=new CellContents(stone,stone);
    var wall=Game.Player.PlayerCollisionUtility.MoveHorizontally(start,displacement,shape,accessor,true);
    Check(wall.y==1f && math.distance(wall.xz,start.xz+displacement)>.05f,"Climbed full-height wall");
    contents[target]=new CellContents(stone,air);
    map[new int3(offset,3,offset)]=new BlockData(BlockId.Stone,200);
    var ceiling=Game.Player.PlayerCollisionUtility.MoveHorizontally(start,displacement,shape,accessor,true);
    Check(ceiling.y==1f,"Step passed through low ceiling");
    map.Remove(new int3(offset,3,offset));
    var airborne=Game.Player.PlayerCollisionUtility.MoveHorizontally(start+new float3(0,.2f,0),displacement,shape,accessor,true);
    Check(math.abs(airborne.y-1.2f)<.0001f,"Auto step while airborne");
    contents[target]=new CellContents(air,stone);
    var small=new Game.Player.PlayerCollisionShape {HalfExtentsXZ=new float2(.2f),Height=.3f};
    var below=(float3)target+new float3(.5f,.05f,.5f);
    Check(!Game.Player.PlayerCollisionUtility.IsPositionBlocked(below,small,accessor),"Upper-only half filled empty lower volume");
    Check(Game.Player.PlayerCollisionUtility.IsPositionBlocked(below+new float3(0,.3f,0),small,accessor),"Upper half has no collision");
   }
  }
  // Full block after a half step is another half-height rise, not a one-block jump.
  var stairs=new Dictionary<int3,BlockData>();
  for(int x=-1;x<5;x++) stairs[new int3(x,0,0)]=new BlockData(BlockId.Stone,200);
  stairs[new int3(1,1,0)]=new BlockData(BlockId.Stone,200,BlockShape.LowerHalf);
  stairs[new int3(2,1,0)]=new BlockData(BlockId.Stone,200);
  var stairAccessor=new Game.World.Chunks.ChunkBlockAccessor(stairs);
  var climbed=Game.Player.PlayerCollisionUtility.MoveHorizontally(new float3(.5f,1f,.5f),new float2(2f,0),shape,stairAccessor,true);
  Check(math.abs(climbed.y-2f)<.0001f && math.abs(climbed.x-2.5f)<.0001f,"Consecutive half-height stairs failed");
  var largeStep=Game.Player.PlayerCollisionUtility.MoveHorizontally(new float3(.5f,1f,.5f),new float2(3.5f,0),shape,
   new Game.World.Chunks.ChunkBlockAccessor(new Dictionary<int3,BlockData>{{new int3(1,1,0),new BlockData(BlockId.Stone,255)}}),false);
  Check(largeStep.x<1f,"Large-frame horizontal move tunneled through a full block");
  Console.WriteLine("Player collision PASS: half volumes, all cardinal directions, chunk edges, support, headroom, airborne gate, stairs and tunneling");
 }
 static void CheckWorldItemCollision() {
  var stone=new BlockPart(BlockId.Stone,200);
  var air=new BlockPart(BlockId.Air,0);
  foreach(int x in new[]{0,16,-1}) {
   var p=new int3(x,2,0);
   var map=new Dictionary<int3,BlockData>{{p,new BlockData {Shape=SplitCellUtility.ReferenceShape}}};
   var contents=new Dictionary<int3,CellContents>{{p,new CellContents(stone,air)}};
   var accessor=new Game.World.Chunks.ChunkBlockAccessor(map,contents);
   var start=new float3(x+.5f,4f,.5f);
   var end=new float3(x+.5f,1f,.5f);
   const float radius=.04f;
   Check(WorldItemCollisionUtility.Sweep(accessor,start,end,radius,out float fraction,out var normal) && fraction<1 && normal.y>.5f,"Item did not hit lower half");
   var landed=math.lerp(start,end,fraction)+normal*.0002f;
   Check(math.abs(landed.y-radius-2.5f)<.001f,"Item landed at full-block height over lower half");
   Check(WorldItemCollisionUtility.ProbeSupport(accessor,landed,radius,out bool supported) && supported,"Half-block support missing");
   Check(WorldItemCollisionUtility.ProbeSupport(accessor,new float3(x+.5f,3f+radius,.5f),radius,out supported) && !supported,
    "Old hovering item remained grounded above a half block");
   contents[p]=new CellContents(air,stone);
   Check(WorldItemCollisionUtility.Sweep(accessor,start,end,radius,out fraction,out normal) &&
    math.abs(math.lerp(start,end,fraction).y-radius-3f)<.001f,"Upper-half top is wrong");
   var lowStart=new float3(x-.5f,2.2f,.5f);
   var lowEnd=new float3(x+1.5f,2.2f,.5f);
   Check(WorldItemCollisionUtility.Sweep(accessor,lowStart,lowEnd,radius,out fraction,out normal) && fraction==1f,
    "Item collided with empty space below upper half");
   contents[p]=new CellContents(stone,air);
   Check(WorldItemCollisionUtility.Sweep(accessor,lowStart,lowEnd,radius,out fraction,out normal) && fraction<1f && math.abs(normal.x)>.5f,
    "Flying item passed through lower half side");
   contents[p]=new CellContents(stone,new BlockPart(BlockId.Sand,90));
   Check(WorldItemCollisionUtility.Sweep(accessor,start,end,radius,out fraction,out normal) &&
    math.abs(math.lerp(start,end,fraction).y-radius-3f)<.001f,"Mixed full solid collision changed");
   contents[p]=new CellContents(air,air);
   Check(WorldItemCollisionUtility.ProbeSupport(accessor,landed,radius,out supported) && !supported,"Removed half still supports item");
  }
  Console.WriteLine("World item collision PASS: lower/upper half landing, support revalidation, flight collisions and chunk boundaries");
 }
 static void CheckSplitProjection() {
  var p=new int3(7,7,7);
  var stone=new BlockPart(BlockId.Stone,201);
  var sand=new BlockPart(BlockId.Sand,101);
  var fluid=new BlockPart(BlockId.OceanWater,255);
  var air=new BlockPart(BlockId.Air,0);
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) {
   var map=new Dictionary<int3,BlockData>();
   var contents=new Dictionary<int3,CellContents>{{p,new CellContents(stone,sand)}};
   var mixed=Build(map,view,contents:contents);
   Check(mixed.solid.Count==3 && mixed.water.Count==0,$"Mixed solid face count {view}/{mixed.solid.Count}");
   Check(mixed.solid.Single(x=>x.FaceType==ProjectedFaceType.Top).BlockId==BlockId.Sand,$"Upper Top material {view}");
   var lower=mixed.solid.Single(x=>x.IsLowerHalf);
   Check(lower.BlockId==BlockId.Stone && lower.Durability==201,$"Lower material/durability {view}");
   Check(mixed.solid.Any(x=>x.BlockId==BlockId.Sand && x.Durability==101 &&
    (x.BlockData & ProjectedCellPacking.UpperHalfFlag)!=0),$"Upper material/durability/half cursor {view}");
   contents[p]=new CellContents(stone,fluid);
   var flooded=Build(map,view,contents:contents);
   Check(flooded.solid.Count==2 && flooded.water.Count==2,$"Flooded face count {view}/{flooded.solid.Count}/{flooded.water.Count}");
   var floor=flooded.solid.Single(x=>x.FaceType==ProjectedFaceType.Top);
   var surface=flooded.water.Single(x=>x.FaceType==ProjectedFaceType.Top);
   Check(floor.ProjectionY==surface.ProjectionY+1,$"Water surface must be above stone {view}");
   Check(flooded.water.All(x=>x.BlockId==BlockId.OceanWater && !x.IsLowerHalf),$"Upper water rendered in lower half {view}");
   var waterSide=flooded.water.Single(x=>x.FaceType!=ProjectedFaceType.Top);
   Check(waterSide.Position==floor.Position,$"Water side must overlay its submerged floor {view}");
   Check(flooded.water.All(x=>x.OpticalDepth==1),$"Upper-half water depth {view}");
   // Unrelated opaque occupancy must not be bypassed by the water underlay exception.
   var occupied=new ProjectionOccupancy(8,Unity.Collections.Allocator.Temp);
   var watery=new ProjectionOccupancy(8,Unity.Collections.Allocator.Temp);
   occupied.TryOccupy(1,1,ProjectedFaceType.Top,source:12,lowerTop:true);
   var waterCells=new List<ProjectedWaterCellData>();
   var waterWriter=new WaterProjectionWriter(occupied,watery,new Unity.Entities.DynamicBuffer<ProjectedWaterCellData>(waterCells));
   Check(!waterWriter.TryAddSideUpper(new BlockData(BlockId.OceanWater,255),1,0,1,1,true,opaqueUnderlaySource:13),"Water ignored another cell's opaque Top");
   Check(waterWriter.TryAddSideUpper(new BlockData(BlockId.OceanWater,255),1,0,1,1,true,opaqueUnderlaySource:12),"Water failed to overlay its own lower Top");
   contents[p]=new CellContents(stone,air);
   Game.World.Chunks.ChunkBlockAccessor.ReadCount=0;
   var splitHalf=Build(map,view,contents:contents);
   long splitReads=Game.World.Chunks.ChunkBlockAccessor.ReadCount;
   Game.World.Chunks.ChunkBlockAccessor.ReadCount=0;
   var legacyHalf=Build(new Dictionary<int3,BlockData>{{p,new BlockData(BlockId.Stone,201,BlockShape.LowerHalf)}},view);
   long legacyReads=Game.World.Chunks.ChunkBlockAccessor.ReadCount;
   Check(splitReads<=legacyReads+8192,$"Sparse split introduced chunk-wide neighbour work {view}/{legacyReads}/{splitReads}");
   Check(splitHalf.solid.Count==legacyHalf.solid.Count && splitHalf.solid.All(a=>legacyHalf.solid.Any(b=>a.BlockData==b.BlockData && a.Position==b.Position && a.FaceData==b.FaceData)),
    $"Split lower half differs from legacy {view}");
   contents[p]=new CellContents(air,sand);
   var floating=Build(map,view,contents:contents);
   Check(floating.solid.Count==2 && floating.solid.All(x=>!x.IsLowerHalf),$"Upper-only solid faces {view}");
   contents[p]=new CellContents(fluid,stone);
   var inverse=Build(map,view,contents:contents);
   Check(inverse.solid.Count==2 && inverse.water.Count==1,$"Water below solid {view}/{inverse.solid.Count}/{inverse.water.Count}");
   contents[p]=new CellContents(stone,new BlockPart(BlockId.Stone,30));
   var durability=Build(map,view,contents:contents);
   Check(durability.solid.Any(x=>x.FaceType==ProjectedFaceType.SideLower && x.Durability==201) &&
    durability.solid.Any(x=>x.FaceType==ProjectedFaceType.SideUpper && x.Durability==30),$"Same material with independent durability must keep full texture {view}");
   var away=ViewDirectionUtility.GetAwayFromCameraOffset(view);
   var waterMap=new Dictionary<int3,BlockData>{{p,new BlockData(BlockId.OceanWater,255)}};
   var shore=new Dictionary<int3,CellContents>{{p+away,new CellContents(sand,fluid)}};
   ushort waterSource=(ushort)Game.World.Chunks.ChunkUtility.ToIndex(p+new int3(0,1,0));
   var touchingWater=Build(waterMap,view,contents:shore);
   Check(!touchingWater.water.Single(c=>c.FaceType==ProjectedFaceType.Top && c.SourceAirIndex==waterSource).WaterTopInset,
    $"Water Top inset against water above a solid lower half {view}");
   shore[p+away]=new CellContents(sand,air);
   var touchingAir=Build(waterMap,view,contents:shore);
   Check(!touchingAir.water.Single(c=>c.FaceType==ProjectedFaceType.Top && c.SourceAirIndex==waterSource).WaterTopInset,
    $"Water Top inset against empty upper half {view}");
   shore[p+away]=new CellContents(air,stone);
   var touchingSolid=Build(waterMap,view,contents:shore);
   Check(touchingSolid.water.Single(c=>c.FaceType==ProjectedFaceType.Top && c.SourceAirIndex==waterSource).WaterTopInset,
    $"Water Top must still inset against solid upper half {view}");
   contents[p]=new CellContents(stone,air);
   map[p+away]=new BlockData(BlockId.Sand,255);
   var backed=Build(map,view,contents:contents);
   Check(backed.solid.Any(x=>x.BlockId==BlockId.Sand && x.FootBlockId==BlockId.Stone &&
    (x.BlockData & ProjectedCellPacking.RaisedFootFlag)!=0),$"Split Top overlay on full wall {view}");
   var edgePosition=new int3(away.x>0 ? 16 : away.x<0 ? -1 : 7,7,
    away.z>0 ? 16 : away.z<0 ? -1 : 7);
   var edgeContents=new Dictionary<int3,CellContents>{{edgePosition,new CellContents(stone,sand)}};
   var edge=Build(new Dictionary<int3,BlockData>(),view,contents:edgeContents);
   Check(edge.solid.Count==2 && edge.solid.Any(x=>x.BlockId==BlockId.Stone) && edge.solid.Any(x=>x.BlockId==BlockId.Sand),
    $"Split materials across chunk boundary {view}/{edge.solid.Count}");
   Console.WriteLine($"Split projection PASS {view}: two solids, stone/water, water/stone, upper Air, lower Air, overlays and water depth");
  }
 }
 static void CheckSaveValidation() {
  var validate=typeof(Game.World.Saving.WorldStateStorage).GetMethod("Validate",
   System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
  var block=new Game.World.Saving.BlockChangeData {Index=65535,BlockId=2,Durability=200};
  var state=new Game.World.Saving.WorldStateData {
   Version=6,WorldId="test",Seed=7,ChunkSizeX=16,ChunkSizeY=256,ChunkSizeZ=16,
   Equipment=Array.Empty<Game.World.Saving.EquipmentSlotData>(),
   Chunks=new[]{new Game.World.Saving.ChunkChangesData {Blocks=new[]{block}}}
  };
  Action<bool,string> check=(expected,label)=>{
   bool valid=true;
   try {validate.Invoke(null,new object[]{state,"test",(uint)7});}
   catch(System.Reflection.TargetInvocationException e) when(e.InnerException is System.IO.InvalidDataException) {valid=false;}
   Check(valid==expected,label);
  };
  for(int version=2;version<=7;version++) {state.Version=version;check(true,$"Legacy save rejected: v{version}");}
  block.Halves=new Game.World.Saving.CellHalvesData();
  for(int version=2;version<=7;version++) {state.Version=version;check(true,$"Unity inline empty Halves broke v{version}");}
  Check(!block.HasSplitContents,"Empty inline Halves treated as a real split; would erase legacy blocks on restore");
  block.Halves=null;
  state.Version=1;state.Chunks=Array.Empty<Game.World.Saving.ChunkChangesData>();check(true,"Legacy v1 save rejected");
  state.Chunks=new[]{new Game.World.Saving.ChunkChangesData {Blocks=new[]{block}}};
  block.Shape=1;state.Version=5;check(true,"Legacy half rejected");
  state.Version=4;check(false,"Pre-shape save accepted half");
  block.Shape=0;state.Version=6;
  block.Halves=new Game.World.Saving.CellHalvesData {LowerId=2,LowerDurability=200,UpperId=4,UpperDurability=123};
  check(true,"Mixed materials rejected");
  Check(block.HasSplitContents,"Older split save without discriminator lost contents");
  state.Version=5;check(false,"Old save accepted new halves");state.Version=6;
  block.Halves.UpperId=254;check(false,"Undefined material accepted");
  block.Halves.UpperId=0;block.Halves.UpperDurability=12;check(false,"Air durability accepted");
  block.Halves.UpperDurability=0;check(true,"Stone/Air rejected");
  block.Halves.LowerDurability=256;check(false,"Durability overflow accepted");
  block.Halves.LowerDurability=200;block.Index=65536;check(false,"Cell index overflow accepted");
  block.Index=1;block.Halves=null;state.Version=7;
  state.Equipment=new[]{new Game.World.Saving.EquipmentSlotData {Slot=1,ItemId=3,PlacementMode=1}};
  check(true,"Saved half placement mode rejected");
  state.Version=6;check(false,"Old save accepted new placement mode");
  state.Version=7;state.Equipment[0].PlacementMode=2;check(false,"Unknown saved placement mode accepted");
 }
}
'@
$halfSource=(($halfUsings | Sort-Object -Unique) -join "`n")+"`n"+($halfBodies -join "`n")+"`n"+$halfHarness
Add-Type -TypeDefinition $halfSource -ReferencedAssemblies $halfReferences -ErrorAction Stop
[HalfProjectionChecks]::Run()
