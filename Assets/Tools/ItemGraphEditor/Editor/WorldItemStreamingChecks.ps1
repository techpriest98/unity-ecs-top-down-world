$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read([string]$p){Get-Content (Join-Path $root $p) -Raw}
$source=Read 'Scripts/Items/WorldItemStreamingSystem.cs'
$start=$source.IndexOf('    public static class WorldItemStreamingUtility')
$end=$source.IndexOf('    [UpdateInGroup', $start)
$utility=$source.Substring($start,$end-$start)
$tick=[regex]::Match((Read 'Scripts/Items/ItemRuntimeUtility.cs'),'(?s)public static void TickDurability\(.*?\n        \}').Value
$item=[regex]::Match((Read 'Scripts/Items/WorldItem.cs'),'(?s)public struct WorldItem : IComponentData\s*\{.*?\n    \}').Value.Replace(': IComponentData','')
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$code='using System; using Unity.Mathematics; using Game.World.Chunks; using Game.World.Saving; using Game.Items;'
$payload=(Read 'Scripts/Items/ItemInstance.cs')+(Read 'Scripts/Items/BlockPlacementMode.cs')+(Read 'Scripts/World/Saving/WorldStateData.cs')
$code+=($payload -replace '(?m)^using [^;]+;\r?\n','')
$code+='namespace Game.World.Chunks { public static class ChunkSettings {public const int SizeX=16,SizeZ=16;} }'
$code+='namespace Game.Items {'+$item+' public struct ItemData {public float DurabilityLossAmount,DurabilityLossInterval;} public static class ActiveDurability {'+$tick+'}'+$utility+'}'
$tests=@'
public static class StreamingChecks {
 static void Check(bool condition,string error){if(!condition)throw new Exception(error);}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static void Run(){
  foreach(int x in new[]{-33,-32,-17,-16,-1,0,15,16,31,32}){
   var key=WorldItemStreamingUtility.Coordinate(new float3(x+.5f,40,x+.5f));
   Check(key.x==(int)Math.Floor((x+.5)/16)&&key.y==key.x,"Negative/boundary chunk coordinate");
  }
  var original=new WorldItem {Instance=new ItemInstance{DefinitionId=2,Durability=100,MaxDurability=100,DurabilityElapsed=.3f},
   Position=new float3(-16.5f,41.08f,12.5f),Velocity=new float3(2,-3,4),Gravity=30,CollisionRadius=.08f,Grounded=false};
  var saved=WorldItemStreamingUtility.Save(original,1234.5);
  var restored=WorldItemStreamingUtility.Restore(saved);
  Check(saved.LastSimulationSeconds==1234.5&&math.all(restored.Position==original.Position)&&math.all(restored.Velocity==original.Velocity)&&
   restored.Gravity==30&&restored.CollisionRadius==.08f&&!restored.Grounded&&restored.Instance.DurabilityElapsed==.3f,"Snapshot state lost");
  var definition=new ItemData{DurabilityLossAmount=.7f,DurabilityLossInterval=2.3f};
  var active=original.Instance;
  ActiveDurability.TickDurability(ref active,definition,156f);
  WorldItemStreamingUtility.CatchUp(ref restored,definition,1234.5,1390.5);
  Check(Near(active.Durability,restored.Instance.Durability)&&Near(active.DurabilityElapsed,restored.Instance.DurabilityElapsed),"Inactive decay differs from active decay");
  var unchanged=restored;
  WorldItemStreamingUtility.CatchUp(ref restored,definition,1390.5,1390.5);
  Check(restored.Instance.Durability==unchanged.Instance.Durability&&restored.Instance.DurabilityElapsed==unchanged.Instance.DurabilityElapsed,"Paused/offline time changed state");
  WorldItemStreamingUtility.CatchUp(ref restored,definition,1390.5,1e12);
  Check(restored.Instance.Durability==0&&restored.Instance.DurabilityElapsed>=0,"Expired torch survived");
  var stone=original;
  WorldItemStreamingUtility.CatchUp(ref stone,new ItemData(),0,1e12);
  Check(stone.Instance.Durability==100&&stone.Instance.DurabilityElapsed==.3f,"Simple item aged");
  var repeat=original;
  var exact=new ItemData{DurabilityLossAmount=1,DurabilityLossInterval=2};
  for(int i=0;i<200;i++){
   var snapshot=WorldItemStreamingUtility.Save(repeat,100+i*.125);
   repeat=WorldItemStreamingUtility.Restore(snapshot);
   WorldItemStreamingUtility.CatchUp(ref repeat,exact,snapshot.LastSimulationSeconds,100+(i+1)*.125);
  }
  var reference=original.Instance;ActiveDurability.TickDurability(ref reference,exact,25);
  Check(Near(repeat.Instance.Durability,reference.Durability)&&Near(repeat.Instance.DurabilityElapsed,reference.DurabilityElapsed),"Repeated stream roundtrips changed decay remainder");
 }
}
'@
Add-Type -TypeDefinition ($code+$tests) -ReferencedAssemblies $refs
[StreamingChecks]::Run()
$state=Read 'Scripts/World/Saving/Systems/WorldStateSystem.cs'
if (!$state.Contains('.Import(data.WorldItems)') -or !$state.Contains('?.AppendInactive(result)')) {throw 'Inactive save/load integration missing'}
if (!$source.Contains('.WithNone<WorldItemSleeping>()') -or !$source.Contains('if (membershipChanged)')) {throw 'Sleeping item gate missing'}
if (!(Read 'Scripts/World/Saving/WorldStateStorage.cs').Contains('data.Version < 8')) {throw 'Legacy clock migration missing'}
'PASS production snapshot/catch-up utilities: negative coordinates, complete state, active/inactive decay equivalence, pause/offline interval, expiration, simple items, 200 roundtrips; static save/load and sleep gates. ECS/Unity streaming integration not executed.'
