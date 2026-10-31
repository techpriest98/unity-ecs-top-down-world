param([string]$MathPath='C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -Path $MathPath
function Read([string]$path){Get-Content (Join-Path $root $path) -Raw}
$sources=@(
 'Scripts/Items/Skills/Configs/PlaceWorldItemAction.cs',
 'Scripts/Items/Skills/Configs/RefuelWorldItemAction.cs',
 'Scripts/Items/Skills/Configs/RefuelWorldItemCondition.cs',
 'Scripts/Items/Skills/Configs/WorldItemHasFuelCondition.cs',
 'Scripts/Items/ItemRuntimeUtility.cs',
 'Scripts/Items/WorldItemReplacementUtility.cs',
 'Scripts/Player/Items/Utilities/PlayerEquipmentUtility.cs'
)
$production=($sources | ForEach-Object {(Read $_) -replace '(?m)^using [^;]+;\r?\n',''}) -join "`n"
$catchup=[regex]::Match((Read 'Scripts/Items/WorldItemStreamingSystem.cs'),'(?s)public static void CatchUp\(.*?\n        \}').Value
$adapters=@'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using Game.Items;
using Game.Items.Skills;
using Game.Player;
using Game.World.Interaction;
namespace UnityEngine {
 public class ScriptableObject{}
 public class CreateAssetMenuAttribute:Attribute{public string menuName;}
 public class MinAttribute:Attribute{public MinAttribute(float n){}}
}
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Entities {
 public struct Entity:IEquatable<Entity>{public int Id;public static Entity Null=>default;public bool Equals(Entity x)=>Id==x.Id;public override bool Equals(object x)=>x is Entity e&&Equals(e);public override int GetHashCode()=>Id;}
 public class DynamicBuffer<T>:List<T>{public int Length=>Count;}
 public class ComponentType{public static ComponentType ReadOnly<T>()=>new ComponentType();}
 public class Entities:List<Entity>,IDisposable{public void Dispose(){}}
 public class Query:IDisposable{public EntityManager Manager;public Entities ToEntityArray(Unity.Collections.Allocator a){var e=new Entities();e.AddRange(Manager.Items.Keys);return e;}public void Dispose(){}}
 public class EntityManager{
  public Dictionary<Entity,WorldItem> Items=new Dictionary<Entity,WorldItem>();
  public DynamicBuffer<PlayerEquipmentSlot> Slots=new DynamicBuffer<PlayerEquipmentSlot>();
  public DynamicBuffer<WorldItemThrowRequest> Requests=new DynamicBuffer<WorldItemThrowRequest>();
  public SelectedProjectedCell Selected=new SelectedProjectedCell{Valid=true,Clear=true,Surface=new float3(-16.5f,63.5f,-32.5f)};
  public bool Exists(Entity e)=>e.Id==1||Items.ContainsKey(e);
  public bool HasBuffer<T>(Entity e)=>e.Id==1;
  public bool HasComponent<T>(Entity e)=>typeof(T)==typeof(WorldItem)?Items.ContainsKey(e):e.Id==1;
  public DynamicBuffer<T> GetBuffer<T>(Entity e,bool readOnly=false)=>(DynamicBuffer<T>)(typeof(T)==typeof(PlayerEquipmentSlot)?(object)Slots:Requests);
  public T GetComponentData<T>(Entity e)=>(T)(typeof(T)==typeof(WorldItem)?(object)Items[e]:Selected);
  public void SetComponentData<T>(Entity e,T value){Items[e]=(WorldItem)(object)value;}
  public Query CreateEntityQuery(params ComponentType[] types)=>new Query{Manager=this};
 }
}
namespace Game.Items {
 [Flags] public enum EquipmentSlot{None=0,LeftHand=1,RightHand=2}
 public enum PlacementSelectionKind{Item}
 public struct ItemPlacementSelection{public PlacementSelectionKind Kind;public int Id;}
 public struct ItemInstance{public int DefinitionId;public float Durability,MaxDurability,DurabilityElapsed;public int CustomState;}
 public class ItemDefinition{public int Id;}
 public struct ItemData{public int Id,DepletedReplacementId;public bool PreserveWhenDepleted;public float MaxDurability,DurabilityLossAmount,DurabilityLossInterval;public EquipmentSlot AllowedSlots;}
 public struct ItemLibraryBlob{public ItemData[] Items;}
 public struct WorldItem{public ItemInstance Instance;public float3 Position,Velocity;public float CollisionRadius,Gravity;public bool Grounded;}
 public struct WorldItemThrowRequest{public WorldItem Item;}
}
namespace Game.Player{public struct PlayerEquipmentSlot{public EquipmentSlot Slot;public ItemInstance Instance;}}
namespace Game.World.Interaction{
 public struct SelectedProjectedCell{public bool Valid,Clear;public float3 Surface;}
 public static class ProjectedCellInteractionUtility{
  public static bool TryGetPlacementPosition(SelectedProjectedCell s,out int3 cell){cell=(int3)math.floor(s.Surface)+new int3(0,1,0);return s.Valid;}
  public static float3 GetTopSurfacePosition(SelectedProjectedCell s,int3 c)=>s.Surface;
 }
}
namespace Game.Items.Skills{
 public abstract class ItemSkillAction:ScriptableObject{
  public virtual bool TryGetTarget(EntityManager m,Entity p,EquipmentSlot s,int item,out float3 position){position=default;return false;}
  public abstract void Execute(EntityManager m,Entity p,EquipmentSlot s,int item);
 }
 public abstract class ItemSkillCondition:ScriptableObject{
  public abstract bool Evaluate(EntityManager m,Entity p,EquipmentSlot s,int item);
  public virtual bool EvaluateTarget(EntityManager m,Entity p,EquipmentSlot s,int item,Entity target,float3 position)=>Evaluate(m,p,s,item);
 }
 public static class PickUpItemAction{public static float3 Surface(EntityManager m,Entity target){var w=m.Items[target];return w.Position-new float3(0,w.CollisionRadius,0);}}
 public static class RubedoPlacementUtility{public static bool CanSpawn(EntityManager m,Entity p,ItemPlacementSelection selection,int3 before)=>m.Selected.Valid&&m.Selected.Clear;}
}
public static class CatchUpCheck{
CATCHUP
}
'@
$tests=@'
public static class StandingChecks{
 static Entity Player=new Entity{Id=1},Target=new Entity{Id=2};
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static EntityManager Manager(EquipmentSlot source,int active=10,float durability=40,int worldId=13){
  var m=new EntityManager();
  foreach(var hand in new[]{EquipmentSlot.LeftHand,EquipmentSlot.RightHand})m.Slots.Add(new PlayerEquipmentSlot{Slot=hand,Instance=new ItemInstance{DefinitionId=hand==source?active:6,Durability=47,MaxDurability=100,CustomState=123}});
  m.Items[Target]=new WorldItem{Instance=new ItemInstance{DefinitionId=worldId,Durability=durability,MaxDurability=100,DurabilityElapsed=2,CustomState=456},Grounded=true,CollisionRadius=.08f,Position=m.Selected.Surface+new float3(0,.08f,0)};
  return m;
 }
 static RefuelWorldItemAction Fuel()=>new RefuelWorldItemAction{FuelItem=new ItemDefinition{Id=10},UnlitItem=new ItemDefinition{Id=13},LitItem=new ItemDefinition{Id=14},Amount=20};
 public static void Run(){
  foreach(var hand in new[]{EquipmentSlot.LeftHand,EquipmentSlot.RightHand}){
   var other=hand==EquipmentSlot.LeftHand?EquipmentSlot.RightHand:EquipmentSlot.LeftHand;
   foreach(int worldId in new[]{13,14})foreach(float durability in new[]{0f,40f,95f}){
    var m=Manager(hand,10,durability,worldId);var fuel=Fuel();var surface=m.Selected.Surface;
    var condition=new RefuelWorldItemCondition{Action=fuel};
    Check(condition.EvaluateTarget(m,Player,hand,10,Target,surface),"Valid refuel hidden");
    Check(fuel.ExecuteTarget(m,Player,hand,10,Target,surface),"Refuel failed");
    var result=m.Items[Target];
    Check(result.Instance.Durability==math.min(100,durability+20)&&result.Instance.DefinitionId==worldId&&result.Instance.CustomState==456&&result.Instance.DurabilityElapsed==2,"Refuel reset item/type/clock or failed cap");
    Check(PlayerEquipmentUtility.GetItem(m.Slots,hand)==0&&PlayerEquipmentUtility.GetItem(m.Slots,other)==6,"Refuel consumed wrong hand");
   }
   foreach(var fixture in new[]{Manager(hand,10,100),Manager(hand,11,40),Manager(hand,10,40,2)}){
    Check(!Fuel().ExecuteTarget(fixture,Player,hand,10,Target,fixture.Selected.Surface),"Invalid/full target accepted");
    Check(PlayerEquipmentUtility.GetItem(fixture.Slots,hand)!=0,"Failed refuel consumed fuel");
   }
   var full=Manager(hand,10,100);
   Check(new RefuelWorldItemCondition{Action=Fuel()}.EvaluateTarget(full,Player,hand,10,Target,full.Selected.Surface),"Full torch must stay visible/inspectable");
   Check(Fuel().CanInspect(full,Player,hand,10,Target,full.Selected.Surface)&&!Fuel().CanRefuel(full,Player,hand,10,Target,full.Selected.Surface),"Full check leaked into visibility or disappeared from Execute");
   Check(!Fuel().ExecuteTarget(full,Player,hand,10,Target,full.Selected.Surface)&&full.Items[Target].Instance.Durability==100&&PlayerEquipmentUtility.GetItem(full.Slots,hand)==10,"Full torch execution mutated fuel or resin");
   var stale=Manager(hand);Check(!Fuel().ExecuteTarget(stale,Player,hand,10,new Entity{Id=999},stale.Selected.Surface),"Stale target accepted");
   Check(!Fuel().ExecuteTarget(stale,Player,hand,10,Target,stale.Selected.Surface+new float3(0,1,0)),"Other floor accepted");
   var place=new PlaceWorldItemAction();
   foreach(float height in new[]{63.5f,64f}){
    var m=Manager(hand,13,40);m.Items.Clear();m.Selected.Surface=new float3(-16.5f,height,-32.5f);
    var instance=PlayerEquipmentUtility.GetInstance(m.Slots,hand);
    Check(place.ExecuteAt(m,Player,hand,13,m.Selected.Surface),"Top placement failed");
    Check(m.Requests.Count==1&&m.Requests[0].Item.Grounded&&math.all(m.Requests[0].Item.Position==m.Selected.Surface+new float3(0,.08f,0)),"Wrong placement support/grounded state");
    Check(m.Requests[0].Item.Instance.Durability==instance.Durability&&m.Requests[0].Item.Instance.CustomState==123&&PlayerEquipmentUtility.GetItem(m.Slots,hand)==0&&PlayerEquipmentUtility.GetItem(m.Slots,other)==6,"Placement lost instance or wrong hand consumed");
   }
   var occupied=Manager(hand,13);Check(!place.ExecuteAt(occupied,Player,hand,13,occupied.Selected.Surface)&&occupied.Requests.Count==0&&PlayerEquipmentUtility.GetItem(occupied.Slots,hand)==13,"Occupied placement mutated state");
   occupied.Items.Clear();occupied.Selected.Clear=false;
   Check(!place.ExecuteAt(occupied,Player,hand,13,occupied.Selected.Surface)&&PlayerEquipmentUtility.GetItem(occupied.Slots,hand)==13,"Blocked clearance consumed item");
  }
  var library=new ItemLibraryBlob{Items=new[]{new ItemData{Id=13,MaxDurability=100,PreserveWhenDepleted=true},new ItemData{Id=14,MaxDurability=100,PreserveWhenDepleted=true,DepletedReplacementId=13,DurabilityLossAmount=1,DurabilityLossInterval=4},new ItemData{Id=2,MaxDurability=100}}};
  var lit=new ItemInstance{DefinitionId=14,MaxDurability=100,Durability=47,CustomState=456,DurabilityElapsed=2};
  var ignited=WorldItemReplacementUtility.Replace(lit,14,100);
  Check(ignited.Durability==47&&ignited.CustomState==456,"Ignition reset fuel/state");
  var world=new WorldItem{Instance=lit};CatchUpCheck.CatchUp(ref world,library.Items[1],0,400);
  Check(world.Instance.Durability==0&&ItemRuntimeUtility.ResolveDepletion(ref world.Instance,ref library)&&world.Instance.DefinitionId==13&&world.Instance.Durability==0,"Unloaded torch vanished or auto-refilled at burnout");
  Check(ItemRuntimeUtility.ResolveDepletion(ref world.Instance,ref library),"Empty unlit pole removed");
  var normal=new ItemInstance{DefinitionId=2,Durability=0};Check(!ItemRuntimeUtility.ResolveDepletion(ref normal,ref library),"Ordinary depleted item retained");
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$MathPath
$code=($adapters.Replace('CATCHUP',$catchup)+$production+$tests).Replace('Game.','StationaryTorchAdapters.Game.').Replace('Unity.Entities','StationaryTorchAdapters.Unity.Entities').Replace('Unity.Collections','StationaryTorchAdapters.Unity.Collections').Replace('UnityEngine','StationaryTorchAdapters.UnityEngine')
Add-Type -TypeDefinition $code -ReferencedAssemblies $refs
[StandingChecks]::Run()
'PASS production standing torch actions with ECS adapters: both hands, +20/cap/full/empty/lit fuel, exact target/height, ingredient consumption, full/half Top placement, occupied/blocked guards, instance preservation, depletion and unloaded catch-up.'
$database=Read 'Configs/ItemDatabase.asset'
foreach($entry in @(@('LargeStick',12,'d011a100000000000000000000000001'),@('LargeTorch',13,'d011a100000000000000000000000002'),@('IgnitedLargeTorch',14,'d011a100000000000000000000000003'))) {
    $definition=Read ('Resources/'+$entry[0]+'.asset')
    if($definition -notmatch ('(?m)^  Id: '+$entry[1]+'$') -or !$database.Contains($entry[2])) {throw 'New item ID/registration missing.'}
    if($entry[1] -gt 12 -and ($definition -notmatch '(?m)^  IsStructure: 1$' -or $definition -notmatch '(?m)^  WorldSpriteUpright: 1$' -or $definition -notmatch '(?m)^  PreserveWhenDepleted: 1$')) {throw 'Pole torch must be an upright persistent structure.'}
}
if((Read 'Configs/Items/Flint/Skills/RefuelAction.asset') -notmatch '(?m)^  Amount: 20$') {throw 'Incorrect refuel amount.'}
foreach($skill in @('CraftLargeStick','IgniteLargeTorch')) {
    if((Read ('Configs/Items/Flint/Skills/'+$skill+'.asset')) -notmatch '(?m)^  DurabilityCost: 20$') {throw 'Large recipe/ignition must wear the shard.'}
}
Add-Type -AssemblyName System.Drawing
foreach($name in @('LargeStick','LargeTorch','IgnitedLargeTorch')) {
    foreach($world in @($false,$true)) {
        $suffix=if($world){'_world'}else{''}
        $height=if(!$world){32}elseif($name -eq 'LargeStick'){16}else{64}
        $bitmap=[System.Drawing.Bitmap]::FromFile((Join-Path $root ('Art/UI/Icons/'+$name+$suffix+'.png')))
        try {if($bitmap.Width -ne 32 -or $bitmap.Height -ne $height -or $bitmap.GetPixel(0,0).A -ne 0) {throw 'Wrong sprite dimensions/alpha.'}} finally {$bitmap.Dispose()}
    }
}
if((Read 'Scripts/World/Saving/WorldStateStorage.cs') -notmatch 'item.Instance.Durability < 0f' -or
   (Read 'Scripts/Items/WorldItemStreamingSystem.cs') -notmatch 'ResolveDepletion\(ref item.Instance' -or
   (Read 'Scripts/World/Saving/Systems/WorldStateSystem.cs') -notmatch 'ResolveDepletion\(ref restored.Instance') {throw 'Zero-fuel save/restore policy missing.'}
$unlitSprite=[System.Drawing.Bitmap]::FromFile((Join-Path $root 'Art/UI/Icons/LargeTorch_world.png'))
$litSprite=[System.Drawing.Bitmap]::FromFile((Join-Path $root 'Art/UI/Icons/IgnitedLargeTorch_world.png'))
try {
    for($y=32;$y -lt 64;$y++){for($x=0;$x -lt 32;$x++){
        if($unlitSprite.GetPixel($x,$y).A -ne $litSprite.GetPixel($x,$y).A){throw 'Torch shaft silhouette/registration changes when lit.'}
    }}
} finally {$unlitSprite.Dispose();$litSprite.Dispose()}
'PASS new item registration, structure policy, exact recipe costs/refuel amount, physical sprite dimensions/transparency, zero-fuel save/restore guards.'
