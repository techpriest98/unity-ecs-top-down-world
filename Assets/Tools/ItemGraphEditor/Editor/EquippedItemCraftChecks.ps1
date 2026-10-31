[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$actionSource = Get-Content (Join-Path $root 'Scripts/Items/Skills/Configs/ReplaceEquippedItemAction.cs') -Raw
$equipmentSource = Get-Content (Join-Path $root 'Scripts/Player/Items/Utilities/PlayerEquipmentUtility.cs') -Raw
$actionSource = $actionSource.Substring($actionSource.IndexOf('namespace Game.Items.Skills'))
$equipmentSource = $equipmentSource.Substring($equipmentSource.IndexOf('namespace Game.Player'))
$adapters = @'
#pragma warning disable 0649 // Unity populates serialized fields; the adapter uses reflection.
using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Items;
using Game.Player;
using Unity.Entities;
using UnityEngine;
namespace UnityEngine {
 public class ScriptableObject {}
 public class CreateAssetMenuAttribute : Attribute {public string fileName,menuName;}
 public class SerializeField : Attribute {}
 public class TooltipAttribute : Attribute {public TooltipAttribute(string text){}}
}
namespace Unity.Entities {
 public struct Entity {public int Id;}
 public class DynamicBuffer<T> : List<T> {public int Length=>Count;}
 public class ComponentType {public static ComponentType ReadOnly<T>()=>new ComponentType();}
 public class Query : IDisposable {
  public EntityManager Manager;public int CalculateEntityCount()=>1;public Entity GetSingletonEntity()=>new Entity{Id=3};
  public T GetSingleton<T>()=>(T)(object)Manager.Library;public void Dispose(){}
 }
 public class EntityManager {
  public DynamicBuffer<PlayerEquipmentSlot> Slots=new DynamicBuffer<PlayerEquipmentSlot>();
  public DynamicBuffer<ItemDefinitionReference> Definitions=new DynamicBuffer<ItemDefinitionReference>();
  public ItemLibrary Library;
  public bool Exists(Entity player)=>player.Id==1;
  public bool HasBuffer<T>(Entity player)=>Exists(player);
  public DynamicBuffer<T> GetBuffer<T>(Entity entity,bool readOnly=false) {
   if(typeof(T)==typeof(PlayerEquipmentSlot))return (DynamicBuffer<T>)(object)Slots;
   if(typeof(T)==typeof(ItemDefinitionReference))return (DynamicBuffer<T>)(object)Definitions;
   throw new Exception("Unexpected buffer "+typeof(T));
  }
  public Query CreateEntityQuery(params ComponentType[] types)=>new Query{Manager=this};
 }
}
namespace Game.Items {
 [Flags] public enum EquipmentSlot {None=0,LeftHand=1,RightHand=2}
 public class ItemDefinition {public int Id;public EquipmentSlot AllowedSlots=(EquipmentSlot)3;}
 public struct ItemInstance {public int DefinitionId,CustomState;public float Durability,MaxDurability,DurabilityElapsed;}
 public struct ItemData {public int Id;public EquipmentSlot AllowedSlots;public float MaxDurability;}
 public struct ItemLibraryBlob {public ItemData[] Items;}
 public class Blob {public bool IsCreated=true;public ItemLibraryBlob Value;}
 public struct ItemLibrary {public Blob Value;}
 public struct ItemDefinitionReference {
  public int Id;public ItemDefinition Definition;
  public static bool TryGet(DynamicBuffer<ItemDefinitionReference> definitions,int id,out ItemDefinition found) {
   foreach(var d in definitions)if(d.Id==id){found=d.Definition;return found!=null;}
   found=null;return false;
  }
 }
}
namespace Game.Player {public struct PlayerEquipmentSlot {public EquipmentSlot Slot;public ItemInstance Instance;}}
namespace Game.Items.Skills {
 public abstract class ItemSkillAction : ScriptableObject {
  public virtual bool IsAvailable(EntityManager m,Entity player,EquipmentSlot source,int item)=>true;
  public abstract void Execute(EntityManager m,Entity player,EquipmentSlot source,int item);
 }
}
ACTION_SOURCE
EQUIPMENT_SOURCE
public static class CraftChecks {
 static Entity Player=new Entity{Id=1};
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static ItemDefinition Branch=new ItemDefinition{Id=8},Stick=new ItemDefinition{Id=11},Resin=new ItemDefinition{Id=10},Torch=new ItemDefinition{Id=1};
 static EntityManager Manager(EquipmentSlot source) {
  var m=new EntityManager();
  foreach(int id in new[]{1,6,8,9,10,11,12,13,14})m.Definitions.Add(new ItemDefinitionReference{Id=id,Definition=id==8?Branch:id==11?Stick:id==10?Resin:id==1?Torch:new ItemDefinition{Id=id}});
  var items=new List<ItemData>();foreach(var d in m.Definitions)items.Add(new ItemData{Id=d.Id,AllowedSlots=d.Definition.AllowedSlots,MaxDurability=100});
  m.Library=new ItemLibrary{Value=new Blob{Value=new ItemLibraryBlob{Items=items.ToArray()}}};
  foreach(var slot in new[]{EquipmentSlot.LeftHand,EquipmentSlot.RightHand})m.Slots.Add(new PlayerEquipmentSlot{Slot=slot,Instance=new ItemInstance{DefinitionId=slot==source?6:8,Durability=slot==source?32:47,MaxDurability=100,DurabilityElapsed=1.25f,CustomState=slot==source?123:456}});
  return m;
 }
 static Game.Items.Skills.ReplaceEquippedItemAction Action(bool other=true) {
  var a=new Game.Items.Skills.ReplaceEquippedItemAction{TargetHand=other?Game.Items.Skills.EquippedItemHand.OtherHand:Game.Items.Skills.EquippedItemHand.SourceHand,RequiredTargetItem=other?Branch:null};
  typeof(Game.Items.Skills.ReplaceEquippedItemAction).GetField("replacementItem",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,Stick);return a;
 }
 public static void Run() {
  foreach(var source in new[]{EquipmentSlot.LeftHand,EquipmentSlot.RightHand}) {
   var other=source==EquipmentSlot.LeftHand?EquipmentSlot.RightHand:EquipmentSlot.LeftHand;
   var m=Manager(source);var a=Action();
   Check(a.GetConfigurationError(m,source)==null&&a.IsAvailable(m,Player,source,6),"Valid two-hand recipe rejected");
   Check(a.ExecuteReplacement(m,Player,source,6),"Craft failed");
   var tool=PlayerEquipmentUtility.GetInstance(m.Slots,source);var result=PlayerEquipmentUtility.GetInstance(m.Slots,other);
   Check(tool.DefinitionId==6&&tool.Durability==32&&tool.CustomState==123,"Craft changed the active tool");
   Check(result.DefinitionId==11&&result.Durability==47&&result.DurabilityElapsed==1.25f&&result.CustomState==456,"Craft lost target hand or item resource/state");
   Check(!a.ExecuteReplacement(m,Player,source,6),"Already crafted stick accepted as another branch");
   Check(PlayerEquipmentUtility.TrySpendDurability(m.Slots,source,6,20),"Source durability payment rejected");
   Check(PlayerEquipmentUtility.GetInstance(m.Slots,source).Durability==12&&PlayerEquipmentUtility.GetItem(m.Slots,other)==11,"Cost changed crafted offhand or wrong tool");
   Check(!PlayerEquipmentUtility.TrySpendDurability(m.Slots,source,6,20)&&PlayerEquipmentUtility.GetInstance(m.Slots,source).Durability==12,"Insufficient durability changed tool");
   Check(!PlayerEquipmentUtility.TrySpendDurability(m.Slots,source,7,1),"Stale source was charged");
   Check(PlayerEquipmentUtility.TrySpendDurability(m.Slots,source,6,12)&&PlayerEquipmentUtility.GetItem(m.Slots,source)==0&&PlayerEquipmentUtility.GetItem(m.Slots,other)==11,"Exhausted tool not removed or crafted result lost");
   foreach(int wrongTarget in new[]{0,9,11}) {
    m=Manager(source);int i=source==EquipmentSlot.LeftHand?1:0;var entry=m.Slots[i];entry.Instance.DefinitionId=wrongTarget;m.Slots[i]=entry;
    Check(!a.IsAvailable(m,Player,source,6)&&!a.ExecuteReplacement(m,Player,source,6)&&PlayerEquipmentUtility.GetItem(m.Slots,other)==wrongTarget,"Wrong/empty offhand mutated");
   }
   m=Manager(source);Check(!a.ExecuteReplacement(m,Player,source,7)&&PlayerEquipmentUtility.GetItem(m.Slots,other)==8,"Stale source item crafted");
   m=Manager(source);a=Action(false);Check(a.ExecuteReplacement(m,Player,source,6)&&PlayerEquipmentUtility.GetItem(m.Slots,source)==11&&PlayerEquipmentUtility.GetItem(m.Slots,other)==8,"Legacy source-hand replacement regressed");
   m=Manager(source);a=Action(false);a.RequiredTargetItem=Stick;a.ConsumeOtherHandItem=Resin;
   typeof(Game.Items.Skills.ReplaceEquippedItemAction).GetField("replacementItem",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,Torch);
   PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,source,11);
   PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,other,10);
   Check(a.GetConfigurationError(m,source)==null&&a.ExecuteReplacement(m,Player,source,11),"Torch craft failed");
   Check(PlayerEquipmentUtility.GetItem(m.Slots,source)==1&&PlayerEquipmentUtility.GetItem(m.Slots,other)==0,"Torch craft must replace active stick and consume only offhand resin");
   Check(!a.ExecuteReplacement(m,Player,source,11),"Repeated torch craft accepted stale source");
   foreach(int wrong in new[]{0,8,9,11}) {
    PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,source,11);
    PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,other,wrong);
    Check(!a.ExecuteReplacement(m,Player,source,11)&&PlayerEquipmentUtility.GetItem(m.Slots,source)==11&&PlayerEquipmentUtility.GetItem(m.Slots,other)==wrong,"Wrong ingredient consumed or free torch crafted");
   }
  }
  foreach(var source in new[]{EquipmentSlot.LeftHand,EquipmentSlot.RightHand}) {
   var other=source==EquipmentSlot.LeftHand?EquipmentSlot.RightHand:EquipmentSlot.LeftHand;
   var m=Manager(source);var a=Action();
   ItemDefinition largeBranch=null,largeStick=null,largeTorch=null;
   foreach(var d in m.Definitions){if(d.Id==9)largeBranch=d.Definition;if(d.Id==12)largeStick=d.Definition;if(d.Id==13)largeTorch=d.Definition;}
   PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,other,9);
   a.RequiredTargetItem=largeBranch;
   typeof(Game.Items.Skills.ReplaceEquippedItemAction).GetField("replacementItem",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,largeStick);
   Check(a.GetConfigurationError(m,source)==null&&a.ExecuteReplacement(m,Player,source,6)&&PlayerEquipmentUtility.GetItem(m.Slots,other)==12&&PlayerEquipmentUtility.GetItem(m.Slots,source)==6,"Large branch crafting changed wrong hand");
   a=Action(false);a.RequiredTargetItem=largeStick;a.ConsumeOtherHandItem=Resin;
   typeof(Game.Items.Skills.ReplaceEquippedItemAction).GetField("replacementItem",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(a,largeTorch);
   PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,source,12);
   PlayerEquipmentUtility.TryEquip(m.Slots,ref m.Library.Value.Value,other,10);
   Check(a.ExecuteReplacement(m,Player,source,12)&&PlayerEquipmentUtility.GetItem(m.Slots,source)==13&&PlayerEquipmentUtility.GetItem(m.Slots,other)==0,"Large torch recipe failed or wrong ingredient consumed");
  }
  var invalid=Action();Check(invalid.GetConfigurationError(Manager(EquipmentSlot.LeftHand),EquipmentSlot.None)!=null,"Invalid source slot accepted for offhand action");
 }
}
'@
$code = $adapters.Replace('ACTION_SOURCE', $actionSource).Replace('EQUIPMENT_SOURCE', $equipmentSource)
Add-Type -TypeDefinition $code
[CraftChecks]::Run()
Write-Output 'PASS actual equipment replacement/crafting logic: both hands, exact target, tool unchanged, item state retained, wrong/empty/large-branch targets rejected, stale source rejected and legacy source-hand replacements preserved (ECS adapters).'
$recipe = Get-Content (Join-Path $root 'Configs/Items/Flint/Skills/CraftStickAction.asset') -Raw
if ($recipe -notmatch '(?m)^  TargetHand: 1$' -or
    $recipe -notmatch 'RequiredTargetItem: .*guid: 46d49d19198d4285b8f1dbe5cc628f5a' -or
    $recipe -notmatch 'replacementItem: .*guid: 3cd13e23cbaa4b0e83c11ca854e7e75f') { throw 'Craft recipe is not Branch -> Stick in the other hand.' }
$condition = Get-Content (Join-Path $root 'Configs/Items/Flint/Skills/CraftStickOtherHand.asset') -Raw
if ($condition -notmatch 'RequiredItem: .*guid: 46d49d19198d4285b8f1dbe5cc628f5a') { throw 'Visibility recipe does not require Branch.' }
$producer = Get-Content (Join-Path $root 'Resources/FlintShard.asset') -Raw
if ($producer -notmatch 'guid: d777036d55bb42868a528092426d32e3') { throw 'Craft Stick is missing from Flint Shard.' }
Add-Type -AssemblyName System.Drawing
foreach ($spec in @(@('Stick.png',32,32), @('Stick_world.png',32,16))) {
    $bitmap = [System.Drawing.Bitmap]::FromFile((Join-Path $root ('Art/UI/Icons/' + $spec[0])))
    try {
        if ($bitmap.Width -ne $spec[1] -or $bitmap.Height -ne $spec[2] -or $bitmap.GetPixel(0,0).A -ne 0) { throw "Wrong sprite dimensions/transparency: $($spec[0])" }
    } finally { $bitmap.Dispose() }
}
Write-Output 'PASS serialized Branch -> Stick recipe, Flint Shard producer, and actual 32x32/32x16 transparent sprites.'
$torchRecipe = Get-Content (Join-Path $root 'Configs/Items/Flint/Skills/CraftTorchAction.asset') -Raw
if ($torchRecipe -notmatch '(?m)^  TargetHand: 0$' -or
    $torchRecipe -notmatch 'RequiredTargetItem: .*guid: 3cd13e23cbaa4b0e83c11ca854e7e75f' -or
    $torchRecipe -notmatch 'ConsumeOtherHandItem: .*guid: 6aa38210be4349d5b3f25f713a80595f' -or
    $torchRecipe -notmatch 'replacementItem: .*guid: 21863c02d6dcf45e395849a4c0b02231') { throw 'Torch crafting recipe is invalid.' }
$stickProducer = Get-Content (Join-Path $root 'Resources/Stick.asset') -Raw
if ($stickProducer -notmatch 'guid: 07d0b22b73a64cb590eb658f343d4211') { throw 'Craft Torch is missing from Stick.' }
$torchCondition = Get-Content (Join-Path $root 'Configs/Items/Flint/Skills/CraftTorchOtherHand.asset') -Raw
if ($torchCondition -notmatch 'RequiredItem: .*guid: 6aa38210be4349d5b3f25f713a80595f') { throw 'Craft Torch visibility must require resin.' }
Write-Output 'PASS Craft Torch: both active hands, resin consumed, wrong/empty ingredients unchanged, stale source rejected, serialized recipe and visibility.'
foreach ($skillPath in @('Configs/Items/Flint/Ignite.asset','Configs/Items/Flint/Skills/CutBark.asset','Configs/Items/Flint/Skills/CraftStick.asset')) {
    if ((Get-Content (Join-Path $root $skillPath) -Raw) -notmatch '(?m)^  DurabilityCost: 20$') { throw "Wrong Flint Shard cost: $skillPath" }
}
Write-Output 'PASS source tool durability: both hands, payment, insufficient/stale item rejection, zero durability removal, offhand result retained, three skill costs set to 20.'
