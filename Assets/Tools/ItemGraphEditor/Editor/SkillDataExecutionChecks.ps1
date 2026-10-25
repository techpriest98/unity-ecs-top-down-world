# Actual SkillChainRunner code, fake ECS/actions/cursor adapters, real Unity.Mathematics.
# This is a headless runner contract test, NOT Unity gameplay or action implementation testing.
[CmdletBinding()]
param([string]$MathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (!(Test-Path -LiteralPath $MathPath)) {
 throw "Unity.Mathematics runtime missing: $MathPath" }
Add-Type -Path $MathPath
$source = Get-Content -LiteralPath (Join-Path $root 'Scripts/Items/Skills/Configs/SkillChainNode.cs') -Raw
$enum = [regex]::Match($source, 'enum SkillChainNodeKind\s*\{([^}]+)\}')
$start = $source.IndexOf('public static class SkillChainRunner', [StringComparison]::Ordinal)
if (!$enum.Success -or $start -lt 0) { throw 'Runner/enum extraction failed: source shape changed.' }
# Brace-balanced extraction retains Execute, IsVisible, ConfigurationFailure and OutputName verbatim.
$open = $source.IndexOf('{', $start); $depth = 1; $end = $open + 1
while ($end -lt $source.Length -and $depth -gt 0) {
    if ($source[$end] -eq '{') { $depth++ }
    if ($source[$end] -eq '}') { $depth-- }
    $end++
}
if ($depth -ne 0) { throw 'Unbalanced runner source.' }
$runner = $source.Substring($start, $end - $start)
$namespace = 'SkillExecutionAdapters_' + [guid]::NewGuid().ToString('N')
$adapters = @'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
namespace NAMESPACE {
public enum SkillChainNodeKind { ENUM }
public enum EquipmentSlot { Hand }
public enum CursorWorldPosition { Before, Behind }
public enum ProjectedFaceType { Top, Side }
public struct Entity : IEquatable<Entity> {
 public int Id; public Entity(int id){Id=id;} public static Entity Null=>default;
 public bool Equals(Entity e)=>Id==e.Id; public override bool Equals(object o)=>o is Entity e&&Equals(e);
 public override int GetHashCode()=>Id; public static bool operator ==(Entity a,Entity b)=>a.Equals(b); public static bool operator !=(Entity a,Entity b)=>!a.Equals(b);
}
public struct SelectedProjectedCell { public bool IsValid; public ProjectedFaceType FaceType; }
public enum PlacementSelectionKind {None,Block,Decoration,Item,Structure}
public struct ItemPlacementSelection {public PlacementSelectionKind Kind;}
public struct ItemInstance {public ItemPlacementSelection PlacementSelection;}
public struct PlayerEquipmentSlot { public int Item; public PlacementSelectionKind Kind; }
public struct ItemLibrary {}
public struct IgnitableCell { public int3 Position; }
public class ItemDefinition { public int Id; }
public class WorldItemMatch { public ItemDefinition Item; public SkillChainNode Output; }
public class SkillChainNode {
 public string name="adapter node"; public SkillChainNodeKind Kind; public SkillChainNode Next,PositionSource;
 public bool SelectionKindConnected=true;
 public SkillChainNode[] SelectionOutputs=new SkillChainNode[3]; public WorldItemMatch[] ItemMatches=Array.Empty<WorldItemMatch>(); public CursorWorldPosition Cursor;
 public ActionDistanceDefinition Distance; public ItemSkillAction Action; public OtherHandItemCondition HandCondition;
 public ItemSkillCondition Condition; public string FailureMessage="adapter failure";
}
public static class Debug { public static List<string> Errors=new List<string>(); public static void LogError(string s,object context){Errors.Add(s);} }
public class ComponentType { public static ComponentType ReadOnly<T>()=>new ComponentType(); }
public class Query : IDisposable { public int CalculateEntityCount()=>1; public Entity GetSingletonEntity()=>new Entity(3); public void Dispose(){} }
public class EntityManager {
 public PlacementSelectionKind SelectionKind;public bool WorldObjectPresent=true;public int StructureId=1024;public CursorWorldPosition LastCursor;
 public int Mutations,Lookups,CursorReads,RangeCalls; public int Item=7,WorldDefinition=20; public bool Selected=true,CursorValid=true,HasFlame;
 public Entity Target=new Entity(42); public float3 Surface=new float3(-16.5f,64.5f,-32.5f);
 public int3 Cell=new int3(-17,64,-33); public Entity LastTarget; public float3 LastPosition,RangePosition,LookupPosition;
 public List<string> Trace=new List<string>();
 public bool HasComponent<T>(Entity e)=>Selected;
 public T GetComponentData<T>(Entity e) { if(typeof(T)==typeof(SelectedProjectedCell))return (T)(object)new SelectedProjectedCell{IsValid=true,FaceType=ProjectedFaceType.Top}; throw new Exception("Unexpected component "+typeof(T)); }
 public Query CreateEntityQuery(params ComponentType[] t)=>new Query();
 public List<T> GetBuffer<T>(Entity e,bool readOnly) {
  if(typeof(T)==typeof(PlayerEquipmentSlot))return (List<T>)(object)new List<PlayerEquipmentSlot>{new PlayerEquipmentSlot{Item=Item,Kind=SelectionKind}};
  if(typeof(T)==typeof(IgnitableCell))return (List<T>)(object)(HasFlame?new List<IgnitableCell>{new IgnitableCell{Position=(int3)math.floor(Surface)}}:new List<IgnitableCell>()); throw new Exception("Unexpected buffer "+typeof(T));
 }
 public bool Exists(Entity e)=>e!=Entity.Null; public bool HasBuffer<T>(Entity e)=>true;
 public bool Mutate(Entity target,float3 position){Mutations++;LastTarget=target;LastPosition=position;Trace.Add("mutation");return true;}
}
public static class PlayerEquipmentUtility { public static int GetItem(List<PlayerEquipmentSlot> b,EquipmentSlot s)=>b[0].Item; public static ItemInstance GetInstance(List<PlayerEquipmentSlot> b,EquipmentSlot s)=>new ItemInstance{PlacementSelection=new ItemPlacementSelection{Kind=b[0].Kind}}; }
public static class CursorWorldUtility {
 public static bool TryGetPosition(EntityManager m,Entity p,CursorWorldPosition c,out int3 v){m.CursorReads++;m.LastCursor=c;v=m.Cell;return m.CursorValid;}
 public static bool TryGetBlock(EntityManager m,int3 p,out int b){b=p.y<m.Cell.y?1:0;return true;}
}
public static class BlockUtility { public static bool IsAir(int b)=>b==0;public static bool IsSolid(int b)=>b!=0; }
public static class ProjectedCellInteractionUtility { public static float3 GetTopSurfacePosition(SelectedProjectedCell s,int3 c)=>new float3(c.x+.5f,c.y+.5f,c.z+.5f); }
public class ActionDistanceDefinition {
 public float MaxDistance=2; public bool Allowed=true;
 public bool Allows(EntityManager m,Entity p,float3 v){m.RangeCalls++;m.RangePosition=v;m.Trace.Add("range");return Allowed;}
}
public class ItemSkillCondition { public bool Allowed=true,ControlsVisibility; public int Calls; public bool Evaluate(EntityManager m,Entity p,EquipmentSlot s,int i){Calls++;return Allowed;} }
public class OtherHandItemCondition : ItemSkillCondition {}
public class ItemSkillAction { public bool Allowed=true;public int Checks,Calls;public bool IsAvailable(EntityManager m,Entity p,EquipmentSlot s,int i){Checks++;return Allowed;} }
public class TogglePlacementShapeAction : ItemSkillAction { public bool ItemSelectionConnected=true,ShapeStateConnected=true; public bool OpenPicker(EntityManager m,Entity p,EquipmentSlot s,int i){Calls++;return m.Mutate(default,default);} }
public class ReplaceEquippedItemAction : ItemSkillAction { public string GetConfigurationError(EntityManager m,EquipmentSlot s)=>null;public bool ExecuteReplacement(EntityManager m,Entity p,EquipmentSlot s,int i){Calls++;return m.Mutate(default,default);} }
public class ReplaceWorldItemAction : ItemSkillAction {
 public Entity CheckedTarget; public float3 CheckedPosition;
 public bool CanReplaceTarget(EntityManager m,Entity t,float3 p){Checks++;CheckedTarget=t;CheckedPosition=p;return Allowed;}
 public bool ExecuteTarget(EntityManager m,Entity t,float3 p){Calls++;return m.Mutate(t,p);}
}
public enum WorldObjectCategory {Item,Structure}
public class DestroyWorldObjectAction : ItemSkillAction {
 public WorldObjectCategory Category;
 public int LastStructureId;
 public bool CanDestroy(EntityManager m,Entity target,int id,float3 surface){Checks++;return Allowed&&m.WorldObjectPresent&&target==m.Target&&(id==0||id==m.StructureId);}
 public bool ExecuteTarget(EntityManager m,Entity target,int id,float3 surface){Calls++;LastStructureId=id;return CanDestroy(m,target,id,surface)&&m.Mutate(target,surface);}
}
public static class WorldObjectTargetUtility {
 public static bool TryFind(EntityManager m,Entity player,int3 before,bool structure,out Entity owner,out int id,out float3 surface){
 m.Lookups++;owner=m.Target;id=structure?m.StructureId:0;surface=m.Surface;return m.WorldObjectPresent;
 }
}
public static class WorldItemMatchUtility {
 public static Entity Find(EntityManager m,float3 p,WorldItemMatch[] rows,out int row){m.Lookups++;m.LookupPosition=p;m.Trace.Add("lookup");for(int i=0;i<rows.Length;i++)if(rows[i].Item.Id==m.WorldDefinition){row=i;return m.Target;}row=-1;return Entity.Null;}
}
public class PickUpItemAction : ItemSkillAction {
 public Entity CheckedTarget;public float3 CheckedPosition;
 public static Entity FindAt(EntityManager m,Entity p,EquipmentSlot s,int3 c){m.Lookups++;return m.Target;}
 public static float3 Surface(EntityManager m,Entity t){if(t!=m.Target)throw new Exception("Wrong surface entity");return m.Surface;}
 public bool CanPickUp(EntityManager m,Entity p,EquipmentSlot s,Entity t,float3 v,bool checkDistance=true){Checks++;CheckedTarget=t;CheckedPosition=v;return Allowed&&!checkDistance;}
 public bool ExecuteTarget(EntityManager m,Entity p,EquipmentSlot s,Entity t,float3 v,bool checkDistance=true){Calls++;return !checkDistance&&m.Mutate(t,v);}
}
public class ThrowItemAction : ItemSkillAction { public string GetConfigurationError(EntityManager m,Entity p,int i)=>null;public bool CanThrowAt(EntityManager m,Entity p,EquipmentSlot s,int i,int3 c)=>Allowed;public bool ExecuteAtPosition(EntityManager m,Entity p,EquipmentSlot s,int i,int3 c)=>m.Mutate(default,c); }
// Model a rejecting legacy action Distance: chain calls must explicitly bypass it.
public class SetWorldBlockAction : ItemSkillAction { public string GetConfigurationError(EntityManager m)=>null;public bool CanSetAt(EntityManager m,Entity p,EquipmentSlot s,int i,int3 c,bool checkDistance=true){Checks++;return Allowed&&!checkDistance;}public bool ExecuteAtPosition(EntityManager m,Entity p,EquipmentSlot s,int i,int3 c,bool checkDistance=true){Calls++;return !checkDistance&&m.Mutate(default,c);} }
RUNNER
public static class Tests {
 static Entity Player=new Entity(1);
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static SkillChainNode Node(SkillChainNodeKind k,ItemSkillAction a=null)=>new SkillChainNode{Kind=k,Action=a};
 static bool Run(SkillChainNode n,EntityManager m,out string failure)=>SkillChainRunner.Execute(n,m,Player,EquipmentSlot.Hand,m.Item,out failure);
 static void Pass(string s)=>Console.WriteLine("PASS adapter execution: "+s);
 static SkillChainNode MatchGraph(out ActionDistanceDefinition distance,out ReplaceWorldItemAction wrong,out ReplaceWorldItemAction right,out SkillChainNode match) {
  wrong=new ReplaceWorldItemAction();right=new ReplaceWorldItemAction();distance=new ActionDistanceDefinition();
  var top=Node(SkillChainNodeKind.TopSurfacePosition);var range=Node(SkillChainNodeKind.WithinDistance);range.Distance=distance;
  match=Node(SkillChainNodeKind.ReplaceableWorldItemTarget);top.Next=range;range.Next=match;
  match.ItemMatches=new[]{new WorldItemMatch{Item=new ItemDefinition{Id=10},Output=Node(SkillChainNodeKind.Action,wrong)},new WorldItemMatch{Item=new ItemDefinition{Id=20},Output=Node(SkillChainNodeKind.Action,right)}};
  return top;
 }
 public static void RunAll() {
  foreach(var kind in new[]{PlacementSelectionKind.None,PlacementSelectionKind.Block,PlacementSelectionKind.Item,PlacementSelectionKind.Structure}) {
   var routing=Node(SkillChainNodeKind.SelectionKindBranch);
   var behind=Node(SkillChainNodeKind.ValidCursorTarget);behind.Cursor=CursorWorldPosition.Behind;behind.Next=Node(SkillChainNodeKind.Action,new SetWorldBlockAction());
   var itemBefore=Node(SkillChainNodeKind.ValidCursorTarget);itemBefore.Cursor=CursorWorldPosition.Before;var itemFind=Node(SkillChainNodeKind.WorldItemTarget);itemBefore.Next=itemFind;
   var destroyItem=new DestroyWorldObjectAction();itemFind.Next=Node(SkillChainNodeKind.Action,destroyItem);
   var structureBefore=Node(SkillChainNodeKind.ValidCursorTarget);structureBefore.Cursor=CursorWorldPosition.Before;var structureFind=Node(SkillChainNodeKind.StructureTarget);structureBefore.Next=structureFind;
   var destroyStructure=new DestroyWorldObjectAction();structureFind.Next=Node(SkillChainNodeKind.Action,destroyStructure);
   routing.SelectionOutputs=new[]{behind,itemBefore,structureBefore};
   var branchManager=new EntityManager{SelectionKind=kind};
   string branchFailure;
   if(!SkillChainRunner.Execute(routing,branchManager,new Entity(1),default,7,out branchFailure)||branchManager.Mutations!=1)throw new Exception("Selection branch failed: "+kind);
   bool objectKind=kind==PlacementSelectionKind.Item||kind==PlacementSelectionKind.Structure;
   if(branchManager.LastCursor!=(objectKind?CursorWorldPosition.Before:CursorWorldPosition.Behind))throw new Exception("Wrong cursor branch: "+kind);
   if(kind==PlacementSelectionKind.Structure&&destroyStructure.LastStructureId!=branchManager.StructureId)throw new Exception("Structure instance handle lost");
   if(objectKind) {
    branchManager=new EntityManager{SelectionKind=kind,WorldObjectPresent=false};
    if(SkillChainRunner.Execute(routing,branchManager,new Entity(1),default,7,out branchFailure)||branchManager.Mutations!=0)throw new Exception("Missing object destroyed terrain");
   }
   routing.SelectionKindConnected=false;
   branchManager=new EntityManager{SelectionKind=kind};
   if(SkillChainRunner.Execute(routing,branchManager,new Entity(1),default,7,out branchFailure)||branchManager.Mutations!=0)throw new Exception("Disconnected Kind input mutated state");
   routing.SelectionKindConnected=true;
   routing.SelectionOutputs[1]=routing;branchManager=new EntityManager{SelectionKind=PlacementSelectionKind.Item};
   if(SkillChainRunner.Execute(routing,branchManager,new Entity(1),default,7,out branchFailure)||branchManager.Mutations!=0)throw new Exception("Branch cycle mutated state");
  }

  string failure;ActionDistanceDefinition distance;ReplaceWorldItemAction wrong,right;SkillChainNode match;
  var graph=MatchGraph(out distance,out wrong,out right,out match);var m=new EntityManager();
  Check(Run(graph,m,out failure)&&failure==null,"Matching branch failed");
  Check(m.Mutations==1&&wrong.Checks==0&&wrong.Calls==0&&right.Checks==1&&right.Calls==1,"Executed nonmatching row or wrong mutation count");
  Check(right.CheckedTarget==m.Target&&m.LastTarget==m.Target&&math.all(right.CheckedPosition==m.Surface)&&math.all(m.LastPosition==m.Surface),"Replacement lost exact entity/surface");
  Check(m.CursorReads==1&&m.Lookups==1&&m.Trace[0]=="range"&&m.Trace[1]=="lookup","Cursor reread or lookup before range");
  Pass("only matching row executes; SAME entity and half-height negative-coordinate position reach replacement");
  graph=MatchGraph(out distance,out wrong,out right,out match);distance.Allowed=false;m=new EntityManager();
  Check(!Run(graph,m,out failure)&&failure==graph.Next.FailureMessage&&m.Lookups==0&&m.Mutations==0&&right.Checks==0,"Failed range performed lookup/mutation");
  Pass("range failure prevents even world-item lookup");
  graph=MatchGraph(out distance,out wrong,out right,out match);right.Allowed=false;m=new EntityManager();
  Check(!Run(graph,m,out failure)&&m.Mutations==0&&right.Calls==0,"Failed action precheck mutated");
  graph=MatchGraph(out distance,out wrong,out right,out match);m=new EntityManager{WorldDefinition=99};
  Check(!Run(graph,m,out failure)&&m.Mutations==0&&wrong.Calls==0&&right.Calls==0,"Unmatched item mutated");
  Pass("unsuccessful match and action checks prevent mutation");
  var queued=new ReplaceEquippedItemAction();var rejected=new ReplaceEquippedItemAction{Allowed=false};
  var first=Node(SkillChainNodeKind.Action,queued);first.Next=Node(SkillChainNodeKind.Action,rejected);m=new EntityManager();
  Check(!Run(first,m,out failure)&&queued.Checks==1&&rejected.Checks==1&&queued.Calls==0&&m.Mutations==0,"Queued action ran before full validation");
  Pass("later failed precheck prevents earlier queued mutation");
  foreach(var label in new[]{"Rubedo","extinguish"}) {
   var replacement=new ReplaceEquippedItemAction();first=Node(SkillChainNodeKind.Action,replacement);m=new EntityManager{Selected=false,CursorValid=false};
   Check(Run(first,m,out failure)&&replacement.Calls==1&&m.Mutations==1&&m.CursorReads==0&&m.Lookups==0,"Positionless "+label+" failed");
   Pass("direct positionless "+label+" via equipped-replacement adapter");
  }
  var pick=new PickUpItemAction();first=Node(SkillChainNodeKind.GroundItemTarget);var r=Node(SkillChainNodeKind.WithinDistance);r.Distance=new ActionDistanceDefinition();first.Next=r;r.Next=Node(SkillChainNodeKind.Action,pick);m=new EntityManager{Item=0};
  Check(Run(first,m,out failure)&&pick.Calls==1&&m.Mutations==1&&pick.CheckedTarget==m.Target&&m.LastTarget==m.Target&&math.all(pick.CheckedPosition==m.Surface)&&math.all(m.RangePosition==m.Surface)&&math.all(m.LastPosition==m.Surface),"Pick lost entity/position through range");
  Pass("Pick forwards exact entity and surface through range");
  foreach(var gateKind in new[]{SkillChainNodeKind.OtherHandItem,SkillChainNodeKind.Ignitable}) {
   graph=MatchGraph(out distance,out wrong,out right,out match);
   var gate=Node(gateKind);gate.HandCondition=new OtherHandItemCondition();gate.Next=match.ItemMatches[1].Output;match.ItemMatches[1].Output=gate;
   m=new EntityManager{HasFlame=true};
   Check(Run(graph,m,out failure)&&right.Calls==1&&wrong.Calls==0&&m.Mutations==1&&right.CheckedTarget==m.Target&&m.LastTarget==m.Target&&math.all(right.CheckedPosition==m.Surface)&&math.all(m.LastPosition==m.Surface),"World Item lost through "+gateKind);
   Check(m.Lookups==1&&m.CursorReads==1,"Passthrough gate re-resolved target/cursor");
   Pass("World Item entity and exact surface pass through "+gateKind+" to replacement");
  }
  var block=new SetWorldBlockAction();first=Node(SkillChainNodeKind.TopSurfacePosition);r=Node(SkillChainNodeKind.WithinDistance);r.Distance=new ActionDistanceDefinition();first.Next=r;r.Next=Node(SkillChainNodeKind.Action,block);m=new EntityManager();
  Check(Run(first,m,out failure)&&block.Checks==1&&block.Calls==1&&m.Mutations==1&&m.RangeCalls==1,"SetWorldBlock did not bypass legacy distance");
  r.Distance.Allowed=false;m=new EntityManager();
  Check(!Run(first,m,out failure)&&m.Mutations==0&&block.Checks==1&&block.Calls==1,"Visible range failure reached block action");
  Pass("Pick and SetWorldBlock bypass rejecting legacy distance; visible WithinDistance remains authoritative");
  queued=new ReplaceEquippedItemAction();first=Node(SkillChainNodeKind.Action,queued);first.Next=first;m=new EntityManager();Debug.Errors.Clear();
  Check(!Run(first,m,out failure)&&failure==null&&m.Mutations==0&&queued.Calls==0&&Debug.Errors.Exists(s=>s.Contains("cycle")),"Execution cycle not rejected before mutation");
  Check(!SkillChainRunner.IsVisible(first,m,Player,EquipmentSlot.Hand,7),"Visibility cycle did not terminate");
  Pass("Execute rejects cycles before mutation; IsVisible terminates cyclic traversal");
  var hand=new OtherHandItemCondition{Allowed=false};first=Node(SkillChainNodeKind.OtherHandItem);first.HandCondition=hand;first.Next=Node(SkillChainNodeKind.Action,new ReplaceEquippedItemAction());m=new EntityManager();
  Check(!SkillChainRunner.IsVisible(first,m,Player,EquipmentSlot.Hand,7)&&!Run(first,m,out failure)&&m.Mutations==0,"Other-hand failure ignored");
  hand.Allowed=true;Check(SkillChainRunner.IsVisible(first,m,Player,EquipmentSlot.Hand,7)&&Run(first,m,out failure),"Other-hand success ignored");
  var condition=new ItemSkillCondition{Allowed=false,ControlsVisibility=true};first=Node(SkillChainNodeKind.ItemCondition);first.Condition=condition;first.Next=Node(SkillChainNodeKind.Action,new ReplaceEquippedItemAction());m=new EntityManager();
  Check(!SkillChainRunner.IsVisible(first,m,Player,EquipmentSlot.Hand,7)&&!Run(first,m,out failure)&&m.Mutations==0,"Item condition failure ignored");
  condition.ControlsVisibility=false;Check(SkillChainRunner.IsVisible(first,m,Player,EquipmentSlot.Hand,7),"Nonvisibility condition hides skill");
  Pass("actual IsVisible/Execute honor other-hand and item-condition gates");
 }
}
}
'@
$code = $adapters.Replace('NAMESPACE', $namespace).Replace('ENUM', $enum.Groups[1].Value).Replace('RUNNER', $runner)
$refs = @((Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $MathPath
Add-Type -TypeDefinition $code -ReferencedAssemblies $refs
$type = ($namespace + '.Tests') -as [type]
try { $type.GetMethod('RunAll').Invoke($null, @()) | Out-Null }
catch { throw "Adapter runtime regression failed: $($_.Exception.GetBaseException().Message)" }
Write-Output 'PASS: actual extracted SkillChainRunner.Execute/IsVisible with C# ECS/action/cursor adapters and real Unity.Mathematics. No Unity gameplay or real action implementation tests executed.'
