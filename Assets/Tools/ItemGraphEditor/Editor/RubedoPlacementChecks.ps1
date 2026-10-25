$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read($path) { Get-Content (Join-Path $root $path) -Raw }
$version=((Read '../ProjectSettings/ProjectVersion.txt') -split "`n" | Where-Object {$_ -match '^m_EditorVersion: '}) -replace '^m_EditorVersion: ',''
$math="C:/Program Files/Unity/Hub/Editor/$($version.Trim())/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll"
Add-Type -Path $math
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$math
$adapter=@'
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Items;
using Game.Items.Skills;
using Game.Player;
using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Interaction;
using Game.World.Rendering;
using Game.World.Trees;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
namespace Unity.Collections { public enum Allocator {Temp} public class NativeArray<T>:List<T>,IDisposable {public NativeArray(IEnumerable<T> data):base(data){} public void Dispose(){}} }
namespace Unity.Entities {
 public interface IComponentData{} public interface IBufferElementData{}
 public class InternalBufferCapacityAttribute:Attribute {public InternalBufferCapacityAttribute(int capacity){}}
 public struct Entity:IEquatable<Entity> {public int Id;public int Index=>Id;public static Entity Null=>default;public bool Equals(Entity b)=>Id==b.Id;public override bool Equals(object b)=>b is Entity e&&Equals(e);public override int GetHashCode()=>Id;public static bool operator==(Entity a,Entity b)=>a.Equals(b);public static bool operator!=(Entity a,Entity b)=>!a.Equals(b);}
 public class DynamicBuffer<T>:List<T> {public int Length=>Count;}
 public struct ComponentType {public Type Type;public static ComponentType ReadOnly<T>()=>new ComponentType{Type=typeof(T)};}
 public class World {public TreeResourcePreparationSystem Resources=new TreeResourcePreparationSystem();public T GetExistingSystemManaged<T>() where T:class=>Resources as T;}
 public class EntityManager {
  public World World=new World();internal Dictionary<(int,Type),object> Data=new Dictionary<(int,Type),object>();
  public bool Exists(Entity e)=>Data.Keys.Any(k=>k.Item1==e.Id);public bool HasComponent<T>(Entity e)=>Data.ContainsKey((e.Id,typeof(T)));
  public void DestroyEntity(Entity e){foreach(var key in Data.Keys.Where(k=>k.Item1==e.Id).ToArray())Data.Remove(key);}
  public T GetComponentData<T>(Entity e)=>(T)Data[(e.Id,typeof(T))];public void Set<T>(Entity e,T value)=>Data[(e.Id,typeof(T))]=value;
  public bool HasBuffer<T>(Entity e)=>Data.ContainsKey((e.Id,typeof(T)));public DynamicBuffer<T> GetBuffer<T>(Entity e,bool readOnly=false)=>(DynamicBuffer<T>)Data[(e.Id,typeof(T))];
  public DynamicBuffer<T> AddBuffer<T>(Entity e){var b=new DynamicBuffer<T>();Data[(e.Id,typeof(T))]=b;return b;}
  public EntityQuery CreateEntityQuery(params ComponentType[] types)=>new EntityQuery{Entities=Data.Keys.Select(k=>k.Item1).Distinct().Where(id=>types.All(t=>Data.ContainsKey((id,t.Type)))).Select(id=>new Entity{Id=id}).ToArray()};
 }
 public class EntityQuery:IDisposable {public Entity[] Entities;public int CalculateEntityCount()=>Entities.Length;public Entity GetSingletonEntity()=>Entities.Single();public NativeArray<Entity> ToEntityArray(Allocator a)=>new NativeArray<Entity>(Entities);public void Dispose(){}}
}
namespace Game.World.Blocks {
 public enum BlockId {Air,Stone} public struct BlockPart {public BlockId Id;}
 public struct BlockData {public BlockId Lower,Upper;} public struct SplitCellData{}
 public struct CellContents {public BlockPart Lower,Upper;}
 public static class BlockUtility {public static bool IsAir(BlockId id)=>id==BlockId.Air;}
 public static class SplitCellUtility {public static CellContents Get(DynamicBuffer<BlockData> blocks,DynamicBuffer<SplitCellData> split,int index)=>new CellContents{Lower=new BlockPart{Id=blocks[index].Lower},Upper=new BlockPart{Id=blocks[index].Upper}};}
}
namespace Game.World.Chunks {
 public struct ChunkComponent {public int2 Coordinate;} public struct ChunkGenerated{}
 public static class ChunkSettings {public const int SizeY=256;}
 public static class ChunkUtility {public static int ToIndex(int x,int y,int z)=>x+16*(z+16*y);}
}
namespace Game.World.Rendering {public enum ProjectedFaceType {Top,SideUpper,SideLower}}
namespace Game.World.Interaction {
 public struct SelectedProjectedCell {public bool IsValid,IsCutSurface,Lower;public int3 Before;public ProjectedFaceType FaceType;}
 public static class ProjectedCellInteractionUtility {
  public static bool TryGetPlacementPosition(SelectedProjectedCell s,out int3 p){p=s.Before;return s.IsValid&&!s.IsCutSurface;}
  public static float3 GetTopSurfacePosition(SelectedProjectedCell s,int3 p)=>new float3(p.x+.5f,p.y-(s.Lower?.5f:0),p.z+.5f);
 }
}
namespace Game.Player {public struct PlayerWorldPosition {public float3 Value;} public struct PlayerCollisionShape {public float2 HalfExtentsXZ;public float Height;}}
namespace Game.Items {
 public enum BlockPlacementMode {Full,Half}
 public class ItemDefinition {public int Id;public float MaxDurability=100;public object WorldSprite=new object();}
 public struct ItemDefinitionReference {public int Id;public ItemDefinition Definition;public static bool TryGet(DynamicBuffer<ItemDefinitionReference> refs,int id,out ItemDefinition result){foreach(var r in refs)if(r.Id==id){result=r.Definition;return result!=null;}result=null;return false;}}
 public struct WorldItem {public ItemInstance Instance;public float3 Position,Velocity;public float Gravity,CollisionRadius;public bool Grounded;}
 public struct WorldItemThrowRequest {public WorldItem Item;}
}
namespace Game.World.Trees {
 public class PineSettings {public float TrunkRadius=.2f,Height=4;}
 public class PineTreeTest {public static PineTreeTest Active=new PineTreeTest();public PineSettings Shape=new PineSettings();public float Scale=1;public int QuarterTurn;}
 public class TreeResourcePreparationSystem {public bool Ready=true;public int BaseSeed=123;}
}
namespace UnityEngine {public class CreateAssetMenuAttribute:Attribute {public string menuName;}}
namespace Game.Items {public enum EquipmentSlot {Hand}}
namespace Game.Items.Skills {
 public abstract class ItemSkillAction {public abstract void Execute(EntityManager manager,Entity player,EquipmentSlot slot,int item);}
 public enum CursorWorldPosition {Before,Behind}
 public static class CursorWorldUtility {public static bool TryGetPosition(EntityManager m,Entity p,CursorWorldPosition source,out int3 before){before=m.GetComponentData<SelectedProjectedCell>(p).Before;return true;}}
}
'@
$production=''
foreach ($path in 'Scripts/Items/ItemInstance.cs','Scripts/World/Trees/ChunkTree.cs','Scripts/Items/Skills/Configs/RubedoPlacementUtility.cs','Scripts/Items/Skills/Configs/DestroyWorldObjectAction.cs') {
 $production+=(Read $path) -replace '(?m)^using [^;]+;\r?\n',''
}
$save=Read 'Scripts/World/Saving/WorldStateStorage.cs'
$validation=[regex]::Match($save,'(?s)private static bool ValidPlacementSelection\(.*?\n        \}').Value.Replace('private static','public static')
if (!$validation) {throw 'Save selection validation missing'}
$production+='public static class SaveSelectionCheck {'+$validation+'}'
$tests=@'
public static class RubedoChecks {
 static readonly Entity Player=new Entity{Id=1},Chunk=new Entity{Id=2},Library=new Entity{Id=3};
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static EntityManager Setup(bool half=false,int x=0,int z=0){
  var m=new EntityManager();m.Set(Player,new SelectedProjectedCell{IsValid=true,FaceType=ProjectedFaceType.Top,Lower=half,Before=new int3(x,4,z)});
  m.Set(Player,new PlayerWorldPosition{Value=new float3(20,4,20)});m.Set(Player,new PlayerCollisionShape{HalfExtentsXZ=new float2(.3f),Height=1.8f});m.AddBuffer<WorldItemThrowRequest>(Player);
  var c=(int2)math.floor(new float2(x,z)/16);m.Set(Chunk,new ChunkComponent{Coordinate=c});m.Set(Chunk,new ChunkGenerated());
  var blocks=m.AddBuffer<BlockData>(Chunk);for(int i=0;i<65536;i++)blocks.Add(default);
  blocks[ChunkUtility.ToIndex(x-c.x*16,3,z-c.y*16)]=new BlockData{Lower=BlockId.Stone,Upper=half?BlockId.Air:BlockId.Stone};m.AddBuffer<SplitCellData>(Chunk);
  var refs=m.AddBuffer<ItemDefinitionReference>(Library);foreach(int id in new[]{1,2,3,4,6,7})refs.Add(new ItemDefinitionReference{Id=id,Definition=new ItemDefinition{Id=id,MaxDurability=id*10}});
  return m;
 }
 public static void Run(){
  foreach(bool half in new[]{false,true}) {
   var m=Setup(half,-1,-1);var before=new int3(-1,4,-1);float y=half?3.5f:4;
   var first=new Entity{Id=10};var other=new Entity{Id=11};
   var worldItem=new WorldItem {Instance=new ItemInstance{DefinitionId=6,Durability=100},Grounded=true,CollisionRadius=.08f,Position=new float3(-.5f,y+.08f,-.5f)};
   m.Set(first,worldItem);m.Set(other,worldItem);
   Entity owner;int instanceId;float3 surface;
   Check(WorldObjectTargetUtility.TryFind(m,Player,before,false,out owner,out instanceId,out surface)&&owner==first&&instanceId==0,"Nigredo did not resolve stable item entity");
   var removeItem=new DestroyWorldObjectAction{Category=WorldObjectCategory.Item};
   Check(!removeItem.CanDestroy(m,owner,instanceId,surface+new float3(0,1,0)),"Stale item position accepted");
   Check(removeItem.ExecuteTarget(m,owner,instanceId,surface)&&!m.Exists(first)&&m.Exists(other),"Nigredo removed wrong/multiple items");
   Check(m.GetBuffer<BlockData>(Chunk)[ChunkUtility.ToIndex(15,3,15)].Lower==BlockId.Stone,"Object removal destroyed support block");
   Check(!WorldObjectTargetUtility.TryFind(m,Player,before,true,out owner,out instanceId,out surface),"Item search fell back into a structure");
   var pineChoice=new ItemPlacementSelection{Kind=PlacementSelectionKind.Structure,Id=(int)StructureDefinitionId.Pine};
   Check(RubedoPlacementUtility.Spawn(m,Player,pineChoice,before),"Structure test spawn failed");
   Check(WorldObjectTargetUtility.TryFind(m,Player,before,true,out owner,out instanceId,out surface)&&owner==Chunk&&instanceId==1024,"Nigredo lost chunk/structure instance ID");
   var removeTree=new DestroyWorldObjectAction{Category=WorldObjectCategory.Structure};
   Check(!removeTree.CanDestroy(m,owner,instanceId+1,surface),"Wrong structure instance accepted");
   Check(removeTree.ExecuteTarget(m,owner,instanceId,surface)&&m.GetBuffer<ChunkTree>(Chunk).Count==0&&m.Exists(other),"Nigredo failed to remove only resolved structure");
   var cursor=m.GetComponentData<SelectedProjectedCell>(Player);cursor.IsCutSurface=true;m.Set(Player,cursor);
   Check(!WorldObjectTargetUtility.TryFind(m,Player,before,false,out owner,out instanceId,out surface),"Cut cap accepted as Before object target");
  }
  foreach(int kind in new[]{1,2,3,4})Check(SaveSelectionCheck.ValidPlacementSelection(kind,1,9),"Current selection rejected by save validation");
  foreach(int version in new[]{1,7,8}) {
   Check(SaveSelectionCheck.ValidPlacementSelection(1,1,version),"Legacy block selection rejected");
   Check(!SaveSelectionCheck.ValidPlacementSelection(3,1,version),"New enum accepted in old save schema");
  }
  Check(!SaveSelectionCheck.ValidPlacementSelection(256,1,9)&&!SaveSelectionCheck.ValidPlacementSelection(-1,1,9)&&
    !SaveSelectionCheck.ValidPlacementSelection(3,0,9)&&!SaveSelectionCheck.ValidPlacementSelection(0,1,9)&&
    SaveSelectionCheck.ValidPlacementSelection(0,0,9),"Invalid selection ID/kind validation");
  foreach(bool half in new[]{false,true})foreach(int x in new[]{-17,-1,0,16})foreach(int id in new[]{1,2,3,4,6,7}){
   var m=Setup(half,x,-1);var selection=new ItemPlacementSelection{Kind=PlacementSelectionKind.Item,Id=id};var before=new int3(x,4,-1);
   Check(RubedoPlacementUtility.CanSpawn(m,Player,selection,before),"Registered item refused");Check(RubedoPlacementUtility.Spawn(m,Player,selection,before),"Item spawn failed");
   var item=m.GetBuffer<WorldItemThrowRequest>(Player).Single().Item;
   Check(item.Instance.DefinitionId==id&&item.Instance.Durability==id*10&&item.Instance.MaxDurability==id*10,"Wrong definition/durability");
   Check(math.abs(item.Position.y-(4-(half?.5f:0)+.08f))<.0001f&&item.Grounded,"Wrong half-height/contact radius");
   Check(!m.HasBuffer<ChunkTree>(Chunk),"Item mutated structures");
  }
  var pine=new ItemPlacementSelection{Kind=PlacementSelectionKind.Structure,Id=(int)StructureDefinitionId.Pine};
  var manager=Setup(true,-1,-1);var at=new int3(-1,4,-1);
  Check(RubedoPlacementUtility.Spawn(manager,Player,pine,at),"Pine on half-block failed");
  var tree=manager.GetBuffer<ChunkTree>(Chunk).Single();Check(tree.Id==1024&&tree.DefinitionId==StructureDefinitionId.Pine&&tree.PlacedByPlayer&&tree.Position.y==3.5f,"Type/instance IDs or manual provenance lost");
  Check(!RubedoPlacementUtility.CanSpawn(manager,Player,pine,at),"Overlapping trunks accepted");
  manager=Setup();manager.Set(Player,new PlayerWorldPosition{Value=new float3(.5f,4,.5f)});Check(!RubedoPlacementUtility.CanSpawn(manager,Player,pine,new int3(0,4,0)),"Tree spawned through player");
  manager=Setup();manager.GetBuffer<BlockData>(Chunk)[ChunkUtility.ToIndex(0,5,0)]=new BlockData{Lower=BlockId.Stone,Upper=BlockId.Stone};Check(!RubedoPlacementUtility.CanSpawn(manager,Player,pine,new int3(0,4,0)),"Blocked trunk accepted");
  foreach(var face in new[]{ProjectedFaceType.Top,ProjectedFaceType.SideUpper}){
   manager=Setup();var s=manager.GetComponentData<SelectedProjectedCell>(Player);s.IsCutSurface=face==ProjectedFaceType.Top;s.FaceType=face;manager.Set(Player,s);
   Check(!RubedoPlacementUtility.CanSpawn(manager,Player,pine,new int3(0,4,0)),"Cut/Side allowed a structure");
  }
  manager=Setup();Check(!RubedoPlacementUtility.CanSpawn(manager,Player,new ItemPlacementSelection{Kind=PlacementSelectionKind.Item,Id=999},new int3(0,4,0)),"Unknown item accepted");
  Check(!RubedoPlacementUtility.IsSpawnSelection(new ItemPlacementSelection{Kind=PlacementSelectionKind.Block,Id=1}),"Block routed into object spawn");
 }
}
'@
Add-Type -TypeDefinition ($adapter+$production+$tests) -ReferencedAssemblies $refs
[RubedoChecks]::Run()
$hud=Read 'Scripts/UI/EquipmentHud.cs'
if ($hud -notmatch 'enum RubedoTab \{ Blocks, Structures, Items \}' -or $hud -notmatch 'GetBuffer<ItemDefinitionReference>' -or $hud -notmatch 'simulation.Enabled = false' -or $hud -notmatch 'pickerSimulation.Enabled = pickerSimulationWasEnabled') {throw 'Picker tabs, live catalog or pause restoration missing'}
$trees=Read 'Scripts/World/Trees/PineTreeTestSystem.cs'
if ($trees -notmatch 'trees\[i\].Id > 0 && !trees\[i\].PlacedByPlayer' -or $trees -notmatch 'manual.Seed = resources.BaseSeed') {throw 'Procedural regeneration must preserve manual trees and refresh their cached seed'}
$action=Read 'Scripts/Items/Skills/Configs/SetWorldBlockAction.cs'
if ($action -notmatch 'RubedoPlacementUtility.CanSpawn' -or $action -notmatch 'return RubedoPlacementUtility.Spawn' -or
 $action.IndexOf('return RubedoPlacementUtility.Spawn') -gt $action.IndexOf('BlockId block =')) {throw 'Spawn dispatch must precede block-ID conversion and block mutations'}
$database=Read 'Configs/ItemDatabase.asset'
foreach ($asset in 'Configs/Items/Torch/Torch.asset','Configs/Items/IgnitedTorch/IgnitedTorch.asset','Resources/Flint.asset','Resources/FlintShard.asset','Resources/Pyrite.asset','Configs/Items/WorldShaper/WorldShaper.asset') {
 $guid=[regex]::Match((Read ($asset+'.meta')),'guid: ([a-f0-9]+)').Groups[1].Value
 if (!$guid -or $database -notmatch $guid) {throw "Rubedo item missing from the registered database: $asset"}
}
Write-Output 'Rubedo: six item definitions, full/half surfaces, negative chunks, durability, Pine type/instance IDs, trunk/player collisions, invalid IDs, cut/Side rejection, tabs and regeneration contracts PASS. ECS/graphics adapted; Unity UI not tested.'
