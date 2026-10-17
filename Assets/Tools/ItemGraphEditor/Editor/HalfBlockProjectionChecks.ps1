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
 'Scripts/World/Generation/Biomes/Ocean/OceanTerrain.cs',
 'Scripts/World/Generation/Biomes/Ocean/Components/OceanSettingsComponent.cs',
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
  public bool TryGetSurface(Unity.Mathematics.int2 coordinate,int x,int z,out Game.World.Blocks.ChunkSurfaceSample surface) {
   int worldX=coordinate.x*16+x,worldZ=coordinate.y*16+z;
   surface=default;
   foreach(var pair in data) {
    if(pair.Key.x!=worldX || pair.Key.z!=worldZ || (contents!=null && contents.ContainsKey(pair.Key))) continue;
    var parts=Game.World.Blocks.CellContents.FromLegacy(pair.Value);
    bool upper=parts.Upper.Id!=Game.World.Blocks.BlockId.Air;
    var material=upper?parts.Upper.Id:parts.Lower.Id;
    int height=pair.Key.y*2+(upper?2:1);
    if(material!=Game.World.Blocks.BlockId.Air && height>surface.HeightHalf)
     surface=new Game.World.Blocks.ChunkSurfaceSample {Material=material,HeightHalf=(ushort)height};
   }
   if(contents!=null) foreach(var pair in contents) {
    if(pair.Key.x!=worldX || pair.Key.z!=worldZ) continue;
    bool upper=pair.Value.Upper.Id!=Game.World.Blocks.BlockId.Air;
    var material=upper?pair.Value.Upper.Id:pair.Value.Lower.Id;
    int height=pair.Key.y*2+(upper?2:1);
    if(material!=Game.World.Blocks.BlockId.Air && height>surface.HeightHalf)
     surface=new Game.World.Blocks.ChunkSurfaceSample {Material=material,HeightHalf=(ushort)height};
   }
   return true;
  }
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
  CheckShoreFoam();
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
 static void CheckShoreFoam() {
   foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) foreach(int offset in new[]{0,15,-16})
   foreach(int corner in new[]{0,1,2,3}) {
    var water=new BlockData(BlockId.OceanWater,255);var sand=new BlockData(BlockId.Sand,255);
    var p=new int3(offset,7,7);
    var north=ViewDirectionUtility.GetAwayFromCameraOffset(view);var right=ViewDirectionUtility.Right(view);
    var east=new int3(right.x,0,right.y);var dirs=new[]{north,east,-north,-east};
    var a=p+dirs[corner];var b=p+dirs[(corner+1)&3];var d=a+dirs[(corner+1)&3];
    var map=new Dictionary<int3,BlockData>{{p,water},{a,water},{b,water},{d,sand}};
    var accessor=new Game.World.Chunks.ChunkBlockAccessor(map);
    byte expected=(byte)(1<<(4+corner));
    Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==expected,
     "Missing diagonal outside shore corner");
    // Verify the actual writer keeps all eight mask bits without changing depth/inset.
    var occupied=new ProjectionOccupancy(8,Unity.Collections.Allocator.Temp);
    var watery=new ProjectionOccupancy(8,Unity.Collections.Allocator.Temp);
    var cells=new List<ProjectedWaterCellData>();
    var writer=new WaterProjectionWriter(occupied,watery,new Unity.Entities.DynamicBuffer<ProjectedWaterCellData>(cells));
    Check(writer.TryAddTop(water,123,true,0,1,1,expected),"Corner writer rejected empty tile");
    Check(cells.Select(x=>x.Value).Any(c=>c.FaceType==ProjectedFaceType.Top && c.OpticalDepth==123 && c.WaterTopInset &&
     (((((uint)c.FaceData>>9)&127u)|(((c.BlockData>>15)&1u)<<7))&expected)!=0),"Outside corner packing lost a bit");
    map[p+new int3(0,2,0)]=sand;
    Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==0,
     "Outside corner foam appeared beneath a roof");
    map.Remove(p+new int3(0,2,0));map.Remove(a);
    Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==0,
     "Outside corner foam crossed an empty neighbour");
   }
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) foreach(int offset in new[]{0,15,-16}) {
   var p=new int3(offset,7,7);var n=p+ViewDirectionUtility.GetAwayFromCameraOffset(view);
   var water=new BlockData(BlockId.OceanWater,255);var sand=new BlockData(BlockId.Sand,255);
   var map=new Dictionary<int3,BlockData>{{p,water},{n,sand}};
   var accessor=new Game.World.Chunks.ChunkBlockAccessor(map);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==1,
    "Water shore mask lost world direction/chunk boundary");
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,false,false,view)==4,
    "Land mask did not point back to water");
   var inland=n+ViewDirectionUtility.GetAwayFromCameraOffset(view);map[inland]=sand;
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,inland,false,false,view)==0,
    "Foam propagated more than one block inland");
   map[n+new int3(0,1,0)]=new BlockData(BlockId.Stone,255,BlockShape.LowerHalf);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,false,false,view)==0,
    "Land under half-block roof received foam");
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==1,
    "Roof over shore incorrectly removed water-side foam");
   map[p+new int3(0,4,0)]=new BlockData(BlockId.Stone,255);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,false,true,view)==0,
    "Water beneath a high ceiling received wind foam");
   map.Remove(p+new int3(0,4,0));map.Remove(n+new int3(0,1,0));
   map[p]=new BlockData(BlockId.OceanWater,255,BlockShape.LowerHalf);
   map[n]=new BlockData(BlockId.Sand,255,BlockShape.LowerHalf);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,true,true,view)==1 &&
    Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,true,false,view)==4,
    "Half-height water/shore contact lost foam");
   map[n]=sand;
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,false,false,view)==0 &&
    Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,p,true,true,view)==1,
    "Foam climbed a shore half a block above water or lost water-side contact");
   map[p]=water;map[n]=new BlockData(BlockId.Sand,255,BlockShape.LowerHalf);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,true,false,view)==4,
    "Lower shore lost permitted foam");
   map.Remove(p);
   Check(Game.World.Rendering.ChunkProjectionBuilder.BuildShoreMask(int2.zero,accessor,n,true,false,view)==0,
    "Removing water did not clear shore mask");
  }
  var packing=Build(new Dictionary<int3,BlockData>{{new int3(7,7,7),new BlockData(BlockId.OceanWater,255)},
   {new int3(7,7,6),new BlockData(BlockId.Sand,255)}},ViewDirection.Front);
  Check(packing.water.Any(c=>c.FaceType==ProjectedFaceType.Top && ((c.FaceData>>9)&15)==1),"Water foam bits not packed");
  Check(packing.solid.Any(c=>c.FaceType==ProjectedFaceType.Top && ((c.FaceData>>8)&15)==4),"Land foam bits not packed");
  Console.WriteLine("Shore foam PASS: four views, chunk edges, half heights, water/land masks, one-cell run-up, ceilings and edits");
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
   blocks[Game.World.Chunks.ChunkUtility.ToIndex(x,9,z)]=new BlockData(BlockId.Turf,255);
   Check(TerrainMinimapUtility.UpdateColumn(blocks,splits,surface,x,z,9) && surface[column].HeightHalf==20 &&
    surface[column].Material==BlockId.Turf,"Placement above surface not reflected");
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
  var seabed=new Game.World.Generation.Biomes.Ocean.OceanTerrain(
   new Game.World.Generation.Biomes.Ocean.OceanSettingsComponent {SandDepth=3});
  var submerged=seabed.Sample(60.5f);
  Check(submerged.Height==61 && submerged.HalfSurface &&
   seabed.GetBlock(60,submerged.Zone,submerged.Height,63)==BlockId.Sand &&
   seabed.GetBlock(61,submerged.Zone,submerged.Height,63)==BlockId.OceanWater,
   "Underwater sand lost half-height geometry or water above it");
  var sandBlock=new BlockPart(BlockId.Sand,255);var waterBlock=new BlockPart(BlockId.OceanWater,255);
  var submergedContents=new Dictionary<int3,CellContents>{{new int3(7,60,7),new CellContents(sandBlock,waterBlock)}};
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) {
   var projected=Build(new Dictionary<int3,BlockData>(),view,contents:submergedContents);
   Check(projected.solid.Any(c=>c.BlockId==BlockId.Sand && c.FaceType==ProjectedFaceType.Top),
    "Submerged half sand top missing from projection");
   Check(projected.water.Any(c=>c.BlockId==BlockId.OceanWater),"Water over half sand missing");
  }
  Check(!seabed.Sample(60f).HalfSurface,"Integral seabed unexpectedly changed shape");
  Console.WriteLine("Ocean seabed PASS: fractional sand, water-filled upper half, four-view projection and integral heights");
  var settings=new Game.World.Generation.Biomes.RockyShore.RockyShoreSettingsComponent {
   CliffWidth=20,CliffWidthVariation=.35f,CliffWidthNoiseScale=.006f,
   CliffMinHeight=10,CliffMaxHeight=24,CliffSharpness=3,CliffNoiseScale=.02f,
   GrassDepth=3,RampNoiseScale=.008f,RampThreshold=.65f,RampStrength=.8f,
   MassifSize=32,MassifWarp=.2f,PlateauRelief=.75f
  };
  var terrain=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(settings);
  var world=new Game.World.Generation.WorldGenerationSettingsComponent {SeaLevelHeight=64};
  Check(world.WaterSurfaceHeight==63 && world.IsWaterColumn(62) &&
   !world.IsWaterColumn(63) && !world.IsWaterColumn(64),
   "Shore distance classified exposed sand at the water fill bound as ocean");
  foreach(float oldSurface in new[]{62.5f,63f,63.5f,64f}) {
   int rawHeight=(int)math.round(oldSurface);
   if(world.IsWaterColumn(rawHeight))continue;
   // These dry columns used to get a negative shore distance and bypass
   // shaping, leaving a row of raised half blocks along the new beach.
   var edge=terrain.Sample(0,0,oldSurface,1,1,123,world);
   float edgeHeight=edge.Height-(edge.HalfSurface?.5f:0);
   Check(edgeHeight==world.WaterSurfaceHeight,"Old shoreline ridge survived beach shaping");
  }
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
     Check(material==BlockId.Turf,"Half cliff summit lost grass cap");grassHalves++;
    }
    Check(terrain.GetBlock(sample.Height,sample.Zone,sample.Height)==BlockId.Air,"Terrain exceeds half surface");
   }
  Check(halves>0 && grassHalves>0,"Rocky Shore produced no half slopes or grass summits");
  var stepsSettings=settings;
  stepsSettings.CliffWidth=48;stepsSettings.CliffWidthVariation=0;
  stepsSettings.CliffMinHeight=8;stepsSettings.CliffMaxHeight=24;
  stepsSettings.BeachWidth=12;stepsSettings.BeachRise=1f;
  stepsSettings.TerraceCount=3;stepsSettings.RiserWidth=1;stepsSettings.PlateauRelief=0;stepsSettings.RampStrength=0;
  var steps=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(stepsSettings);
  foreach(uint seed in new uint[]{0,1,123,777}) {
   float previous=0;int flat=0,maxFlat=0,rises=0,falls=0;float low=999,high=0;
   for(int x=-512;x<=512;x++) {
    var s=steps.Sample(x,16,64,25,1,seed,world);
    var inland=steps.Sample(x,16,64,40,1,seed,world);
    float height=s.Height-(s.HalfSurface?.5f:0);
    Check(s.Height==inland.Height && s.HalfSurface==inland.HalfSurface,
     "Massif height still follows distance from water");
    if(x>-512) {
     if(height-previous>=2)rises++;
     if(previous-height>=2)falls++;
     if(height==previous)flat++;else flat=0;
     maxFlat=System.Math.Max(maxFlat,flat);
    }
    low=System.Math.Min(low,height);high=System.Math.Max(high,height);previous=height;
   }
   Check(rises>2 && falls>2 && maxFlat>=8 && high-low>=8,
    "Missing broad independent plateaus, sharp walls or non-monotonic height ordering");
   int far=steps.GetShoreSearchDistance();
   var outer=steps.Sample(-17,16,83.5f,far,1,seed,world);
   Check(outer.Height==84 && outer.HalfSurface,"Clamped shore field created an inland seam");
   var noInfluence=steps.Sample(-17,16,67.5f,25,0,seed,world);
   Check(noInfluence.Height==68 && noInfluence.HalfSurface,"Zero biome influence changed base terrain");
  }
  Check(steps.GetShoreSearchDistance()>=12*1.35f+48+48*.35f,"Shore search clips terrace profile");
  foreach(float influence in new[]{.5f,.75f,1f}) foreach(float macro in new[]{64f,68f,80f})
   for(int x=-32;x<=32;x+=8) for(int d=1;d<=7;d++) {
    var sand=steps.Sample(x,16,macro,d,influence,123,world);
    float surface=sand.Height-(sand.HalfSurface?.5f:0);
    Check(sand.Zone==(byte)Game.World.Generation.Biomes.RockyShore.RockyShoreZone.Beach &&
     surface>=world.SeaLevelHeight-1 && surface<=world.SeaLevelHeight-1+stepsSettings.BeachRise,
     "Beach exceeds two half-block levels above the actual generated water surface");
   }
  var untouchedBeach=steps.Sample(0,0,80,3,0,123,world);
  Check(untouchedBeach.Height==80,"Zero influence flattened the original beach height");
  Console.WriteLine("Rocky Shore massifs PASS: independent height at equal position, rising/falling neighbours, broad flat plateaus, beach and inland boundary");
  var shelfSettings=stepsSettings;
  shelfSettings.LowPlateauHeight=2;shelfSettings.LowPlateauChance=1;
  var shelves=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(shelfSettings);
  for(int x=-64;x<=64;x+=4) {
   var s=shelves.Sample(x,16,64,25,1,123,world);
   Check(s.Height-(s.HalfSurface?.5f:0)==world.WaterSurfaceHeight+2,
    "Low plateau height was not measured from the actual water surface");
  }
  shelfSettings.LowPlateauChance=.3f;
  shelves=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(shelfSettings);
  int lowShelves=0,tallMasses=0;
  for(int x=-512;x<=512;x++) {
   var s=shelves.Sample(x,16,64,25,1,123,world);
   float h=s.Height-(s.HalfSurface?.5f:0);
   if(h==world.WaterSurfaceHeight+2)lowShelves++;
   if(h>=world.SeaLevelHeight+shelfSettings.CliffMinHeight)tallMasses++;
  }
  Check(lowShelves>20 && tallMasses>20,"Low shelves replaced every taller mass or never appeared");
  Console.WriteLine("Rocky Shore low shelves PASS: two blocks above water and coexistence with taller massifs");
  var coverSettings=stepsSettings;
  coverSettings.LayeredGroundCover=true;coverSettings.RockEdgeWidth=2;coverSettings.HalfCoverWidth=2.5f;
  var coveredTerrain=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(coverSettings);
  int bareCover=0,halfCover=0,fullCover=0;
  for(int x=-128;x<=128;x+=2) for(int z=-64;z<=64;z+=2) {
   var s=coveredTerrain.Sample(x,z,64,25,1,123,world);
   var original=steps.Sample(x,z,64,25,1,123,world);
   Check(s.HasRockCover && s.RockSurfaceHalf==original.SurfaceHalf && s.SurfaceHalf==s.RockSurfaceHalf+s.CoverHalf,
    "Ground cover replaced rock instead of adding a layer above it");
   Check(s.CoverHalf<=2,"First-stage soil cover exceeded one block");
   if(s.CoverHalf==0)bareCover++;else if(s.CoverHalf==1)halfCover++;else fullCover++;
   Check(Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(s.RockSurfaceHalf-1,63,s)==BlockId.Stone,
    "Soil leaked below rock surface as a deep dirt wall");
   if(s.CoverHalf>0)Check(Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(s.SurfaceHalf-1,63,s)==BlockId.Turf,
    "Ground cover has no grass top");
   Check(Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(s.SurfaceHalf,63,s)==BlockId.Air,
    "Cover height disagrees with generated materials");
   int cx=(int)math.floor(x/16f),cz=(int)math.floor(z/16f);
   var repeated=coveredTerrain.Sample(cx*16+(x-cx*16),cz*16+(z-cz*16),64,25,1,123,world);
   Check(repeated.SurfaceHalf==s.SurfaceHalf && repeated.CoverHalf==s.CoverHalf,"Cover depends on chunk-local coordinates");
  }
  Check(bareCover>20 && halfCover>20 && fullCover>20,"Missing bare / half / full soil zones");
  foreach(float rockHeight in new[]{64f,64.5f}) foreach(byte cover in new byte[]{0,1,2}) {
   var s=new Game.World.Generation.Biomes.BiomeTerrainSample(Game.World.Generation.WorldBiome.RockyShore,rockHeight,4,cover);
   var cells=new Unity.Entities.DynamicBuffer<BlockData>(new List<BlockData>(new BlockData[Game.World.Chunks.ChunkSettings.BlockCount]));
   var split=new Unity.Entities.DynamicBuffer<SplitCellData>(new List<SplitCellData>());
   for(int y=63;y<=66;y++) {
    var a=new BlockPart(Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(y*2,63,s),255);
    var b=new BlockPart(Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(y*2+1,63,s),255);
    int index=Game.World.Chunks.ChunkUtility.ToIndex(1,y,1);
    SplitCellUtility.Set(cells,split,index,new CellContents(a,b));
    var contents=SplitCellUtility.Get(cells,split,index);
    Check(contents.Lower.Id==a.Id && contents.Upper.Id==b.Id,"Generated soil split failed round-trip");
   }
  }
  Console.WriteLine("Rocky Shore cover PASS: independent stone foundation, 0/.5/1-block layers, half-cell materials and world-space determinism");
  var rockSettings=stepsSettings;
  rockSettings.CoastalRockSpacing=16;rockSettings.CoastalRockChance=.4f;
  rockSettings.CoastalRockHeight=3;rockSettings.CoastalRockWaterReach=12;
  var outcrops=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(rockSettings);
  var oceanFloor=new Game.World.Generation.Biomes.BiomeTerrainSample(Game.World.Generation.WorldBiome.Ocean,61,1);
  var sandFloor=new Game.World.Generation.Biomes.BiomeTerrainSample(Game.World.Generation.WorldBiome.RockyShore,64,5);
  var grassFloor=new Game.World.Generation.Biomes.BiomeTerrainSample(Game.World.Generation.WorldBiome.RockyShore,68,4);
  foreach(uint seed in new uint[]{1,123}) {
   int waterRocks=0,sandRocks=0,halfRocks=0;
   for(int x=-64;x<=64;x++) for(int z=-64;z<=64;z++) {
    var s=outcrops.ApplyCoastalRocks(x,z,-4,seed,world,oceanFloor);
    var land=outcrops.ApplyCoastalRocks(x,z,4,seed,world,sandFloor);
    var again=outcrops.ApplyCoastalRocks(x,z,-4,seed,world,oceanFloor);
    Check(s.Height==again.Height && s.HalfSurface==again.HalfSurface && s.Zone==again.Zone,"Outcrop generation is not deterministic");
    if(s.Biome==Game.World.Generation.WorldBiome.RockyShore) {
     waterRocks++;if(s.HalfSurface)halfRocks++;
     Check(s.Zone==2 && s.SurfaceHalf>world.WaterSurfaceHeight*2 && s.SurfaceHalf<=(world.WaterSurfaceHeight+3.5f)*2,
      "Outcrop material or sea-relative height is wrong");
     Check(outcrops.GetBlock(s.Height-1,s.Zone,s.Height)==BlockId.Stone,"Outcrop has a sand or grass cap");
     bool insideRadius=false;int gx=(int)math.floor(x/16f),gz=(int)math.floor(z/16f);
     for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++) {
      uint h=math.hash(new uint3((uint)(gx+dx),(uint)(gz+dz),seed^0x524F434Bu));
      var center=(new float2(gx+dx,gz+dz)+.15f+new float2(h&65535u,h>>16)/65535f*.7f)*16;
      if(math.distance(center,new float2(x,z))<=3.001f)insideRadius=true;
     }
     Check(insideRadius,"Outcrop footprint exceeded three-block radius");
    }
    if(land.Zone==2)sandRocks++;
   }
   Check(waterRocks>30 && waterRocks<2000 && sandRocks>30 && halfRocks>0,"Missing sparse water/beach rocks or half-block caps");
   Check(outcrops.ApplyCoastalRocks(0,0,-20,seed,world,oceanFloor).SurfaceHalf==oceanFloor.SurfaceHalf,"Outcrop escaped the nearshore band");
   Check(outcrops.ApplyCoastalRocks(0,0,4,seed,world,grassFloor).SurfaceHalf==grassFloor.SurfaceHalf,"Outcrop replaced a grass plateau");
  }
  Console.WriteLine("Rocky Shore outcrops PASS: seeded sparse beach/water rocks, radius <=3, stone/half caps and placement exclusions");
  var coastSettings=stepsSettings;
  coastSettings.CoastVariation=1;coastSettings.CoastNoiseScale=.006f;
  var coast=new Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain(coastSettings);
  foreach(uint seed in new uint[]{0,1,123,777}) {
   int bays=0,rocks=0,longest=0,run=0,changes=0;bool previousRock=false;
   for(int x=-512;x<=512;x++) {
    var s=coast.Sample(x,-17,64,1,1,seed,world);
    var repeat=coast.Sample(x,-17,64,1,1,seed,world);
    Check(s.Height==repeat.Height && s.HalfSurface==repeat.HalfSurface && s.Zone==repeat.Zone,
     "Coast character is not deterministic");
    bool rocky=s.Zone==(byte)Game.World.Generation.Biomes.RockyShore.RockyShoreZone.Cliff;
    if(rocky) {
     rocks++;run++;longest=System.Math.Max(longest,run);
     Check(coast.GetBlock(s.Height-1,s.Zone,s.Height)==BlockId.Stone,"Headland retained sand cap");
    } else run=0;
    if(s.Zone==(byte)Game.World.Generation.Biomes.RockyShore.RockyShoreZone.Beach) {
     bays++;Check(s.Height-(s.HalfSurface?.5f:0)<=world.WaterSurfaceHeight+1,
      "Coast mask made a sandy bay too high");
    }
    if(x>-512 && rocky!=previousRock)changes++;previousRock=rocky;
   }
   Check(bays>50 && rocks>50 && longest>=16 && changes<64,"Missing broad sandy/rocky coastal stretches");
   bool wallAtWater=false;
   for(int x=-512;x<=512;x+=4) {
    var s=coast.Sample(x,-17,64,1,1,seed,world);
    if(s.Zone==(byte)Game.World.Generation.Biomes.RockyShore.RockyShoreZone.Cliff &&
     s.Height-(s.HalfSurface?.5f:0)>world.WaterSurfaceHeight+2)wallAtWater=true;
   }
   Check(wallAtWater,"Rocky coast removed sand without moving the cliff to the water");
   var far=coast.Sample(0,-17,83.5f,coast.GetShoreSearchDistance(),1,seed,world);
   Check(far.Height==84 && far.HalfSurface,"Coast mask broke inland blend boundary");
  }
  Console.WriteLine("Rocky Shore coast PASS: seeded broad bays/headlands, stone contact at water, gentle sand and inland continuity");
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
