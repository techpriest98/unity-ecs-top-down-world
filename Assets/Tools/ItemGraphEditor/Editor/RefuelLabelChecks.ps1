param([string]$MathPath='C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -Path $MathPath
$ui=Get-Content (Join-Path $root 'Scripts/UI/RefuelTargetDurabilitySystem.cs') -Raw
$available=Get-Content (Join-Path $root 'Scripts/Player/Items/Systems/PlayerAvailableSkillsSystem.cs') -Raw
$anchor=[regex]::Match($ui,'(?s)internal static Vector3 LabelAnchor\(.*?(?=        protected override void OnUpdate)').Value
$entry=[regex]::Match($available,'(?s)private static void SetEntry\(.*?\n        \}').Value
if(!$anchor -or !$entry){throw 'Production preview helpers not found.'}
$code=@'
using System;
using Unity.Mathematics;
public struct Entity {public int Id;public static Entity Null=>default;public override bool Equals(object x)=>x is Entity e&&e.Id==Id;public override int GetHashCode()=>Id;public static bool operator==(Entity a,Entity b)=>a.Id==b.Id;public static bool operator!=(Entity a,Entity b)=>a.Id!=b.Id;}
public enum EquipmentSlot {LeftHand,RightHand}
public enum ViewDirection {Front,Back,Left,Right}
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
public struct PlayerAvailableSkill {public EquipmentSlot SourceSlot;public int SourceItem,SkillIndex;public Entity Target;public float3 TargetPosition;}
public class DynamicBuffer<T> {public T[] Values=new T[1];public int Writes;public T this[int i]{get=>Values[i];set{Values[i]=value;Writes++;}}}
// Projection adapter: validate anchoring relative to the shared projection,
// not the renderer's separately tested camera/view rotation implementation.
public static class WorldPositionProjectionUtility {public static float2 Project(float3 p,ViewDirection d)=>new float2(p.x-p.z,p.y+p.z*.5f);}
public static class LabelChecks {
ANCHOR
ENTRY
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(){
  foreach(float surfaceY in new[]{63.5f,64f})foreach(float origin in new[]{-1000f,0f,1000f}){
   var surface=new float3(-16.5f,surfaceY,-32.5f);var p=WorldPositionProjectionUtility.Project(surface,ViewDirection.Front);
   var a=LabelAnchor(surface,ViewDirection.Front,2,origin);var b=LabelAnchor(surface,ViewDirection.Front,3,origin);
   Check(math.abs(a.x-p.x)<.0001f&&math.abs(a.y-(p.y+.25f+2+.12f))<.0001f,"Label not anchored above sprite head");
   Check(math.abs(a.z-(2f*(p.y+.25f-surfaceY-9f)-origin-.002f))<.0001f&&a.z==b.z&&math.abs(b.y-a.y-1)<.0001f,"Label depth must follow foot plane, not its head height");
  }
  var entries=new DynamicBuffer<PlayerAvailableSkill>();var first=new Entity{Id=2};var second=new Entity{Id=3};var position=new float3(1,63.5f,2);
  SetEntry(entries,0,EquipmentSlot.LeftHand,10,0,first,position);Check(entries.Writes==1&&entries[0].Target==first,"Missing resolved HUD target");
  SetEntry(entries,0,EquipmentSlot.LeftHand,10,0,first,position);Check(entries.Writes==1,"Unchanged visibility data rewritten");
  SetEntry(entries,0,EquipmentSlot.LeftHand,10,0,second,position);Check(entries.Writes==2&&entries[0].Target==second,"Moving cursor to another torch kept stale target");
  position.y+=.5f;SetEntry(entries,0,EquipmentSlot.LeftHand,10,0,second,position);Check(entries.Writes==3&&math.all(entries[0].TargetPosition==position),"Changed support height not published");
  SetEntry(entries,0,EquipmentSlot.RightHand,0,-1);Check(entries[0].Target==Entity.Null&&math.all(entries[0].TargetPosition==default(float3)),"Cleared/changed-hand skill retained HUD target");
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$MathPath
Add-Type -TypeDefinition $code.Replace('ANCHOR',$anchor).Replace('ENTRY',$entry) -ReferencedAssemblies $refs
[LabelChecks]::Run()
foreach($guard in @('entry.SourceSlot != activeHand.ValueRO.Slot','!EntityManager.Exists(entry.Target)','action.CanInspect(', 'WorldToScreenPoint(anchor)', 'current != shownCurrent || maximum != shownMaximum', 'WorldLabelStyle.Apply(label)', 'OnStopRunning() => Hide()')) {
    if(!$ui.Contains($guard)){throw "Missing label lifecycle/input/live-target guard: $guard"}
}
if($ui.Contains('WorldItemMatchUtility.Find') -or $ui.Contains('ExecuteTarget(')){throw 'Preview must never search for a second world target or execute Refuel.'}
if(!$ui.Contains('entry.TargetPosition, checkDistance: false)')) {throw 'Fuel label must remain visible outside the execution range.'}
$style=Get-Content (Join-Path $root 'Scripts/UI/WorldLabelStyle.cs') -Raw
$speech=Get-Content (Join-Path $root 'Scripts/UI/PlayerSkillMessageSystem.cs') -Raw
if(!$speech.Contains('WorldLabelStyle.Apply(label)')){throw 'Player comments must share the world-label style.'}
foreach($guard in @('new(1f, .93f, .75f, 1f)', 'FontSize = 26f', 'label.fontSize = FontSize', 'label.enableAutoSizing = false', 'OutlineWidth = 0f', 'label.outlineWidth = OutlineWidth', 'DisableKeyword("OUTLINE_ON")', 'label.extraPadding = false', 'label.UpdateMeshPadding()', 'label.raycastTarget = false')) {
    if(!$style.Contains($guard)){throw "Missing world-label styling: $guard"}
}
if(!$ui.Contains('WorldLabelStyle.EnsureApplied(label)') -or !$speech.Contains('WorldLabelStyle.EnsureApplied(label)')) {throw 'Existing world labels must receive style updates.'}
'PASS actual label anchor/cache helpers: full/half heights, foot-relative depth origin, changing target/support/hand, unchanged cache reuse; live target/range lifecycle gates verified. Native Unity UI rendering not executed.'
