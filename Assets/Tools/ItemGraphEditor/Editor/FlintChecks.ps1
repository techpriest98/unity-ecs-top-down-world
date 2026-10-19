$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
$sources = ''
foreach ($path in 'Scripts/Items/ItemInstance.cs','Scripts/Items/BlockPlacementMode.cs','Scripts/Items/WorldItemReplacementUtility.cs') {
 $sources += (Get-Content (Join-Path $root $path) -Raw) -replace '(?m)^using [^;]+;\r?\n',''
}
$tests = @'
public static class FlintChecks {
 static void Check(bool ok,string message){if(!ok)throw new System.Exception(message);}
 public static void Run(){
  var cell=new int3(-17,64,-33);var p=new float3(-16.5f,64.5f,-32.5f);
  Check(Game.Items.WorldItemReplacementUtility.MatchesSurface(true,50,p,cell,64.5f),"Half-height ground item rejected");
  Check(!Game.Items.WorldItemReplacementUtility.MatchesSurface(false,50,p,cell,64.5f),"Flying item accepted");
  Check(!Game.Items.WorldItemReplacementUtility.MatchesSurface(true,0,p,cell,64.5f),"Broken item accepted");
  Check(!Game.Items.WorldItemReplacementUtility.MatchesSurface(true,50,p,cell,65),"Different floor accepted");
  Check(!Game.Items.WorldItemReplacementUtility.MatchesSurface(true,50,p+new float3(1,0,0),cell,64.5f),"Neighbour cell accepted");
  var original=new Game.Items.ItemInstance{DefinitionId=1,Durability=37,MaxDurability=100,DurabilityElapsed=8};
  var replaced=Game.Items.WorldItemReplacementUtility.Replace(original,2,100);
  Check(original.DefinitionId==1&&replaced.DefinitionId==2&&replaced.Durability==37&&replaced.MaxDurability==100&&replaced.DurabilityElapsed==0,"Replacement repaired/consumed original resource");
  Check(Game.Items.WorldItemReplacementUtility.Replace(original,2,20).Durability==20,"Replacement exceeds new maximum");
 }
}
'@
Add-Type -TypeDefinition ("using Unity.Mathematics;`n"+$sources+$tests) -ReferencedAssemblies $refs
[FlintChecks]::Run()
$flint=Get-Content (Join-Path $root 'Resources/Flint.asset') -Raw
$ignite=Get-Content (Join-Path $root 'Configs/Items/Flint/Ignite.asset') -Raw
$action=Get-Content (Join-Path $root 'Configs/Items/Flint/ReplaceWorldItem.asset') -Raw
$range=Get-Content (Join-Path $root 'Configs/Items/Flint/IgniteWithinDistance.asset') -Raw
$db=Get-Content (Join-Path $root 'Configs/ItemDatabase.asset') -Raw
$knapped=Get-Content (Join-Path $root 'Resources/FlintShard.asset') -Raw
$otherPyrite=Get-Content (Join-Path $root 'Configs/Items/Flint/OtherFlint.asset') -Raw
if ($flint.Contains('c019f11a00004e66b200000000000004') -or $flint.Contains('ab11c01f00004e66b200000000000002')) {throw 'Whole flint must not own Knapping or Ignite.'}
if (!$knapped.Contains('ab11c01f00004e66b200000000000002') -or $knapped.Contains('c019f11a00004e66b200000000000004')) {throw 'Knapped flint must own Ignite, not Knapping.'}
if (!$otherPyrite.Contains('RequiredItem: {fileID: 11400000, guid: c019f11a00004e66b200000000000003')) {throw 'Incorrect opposite-hand material.'}
if (!$flint.Contains('OnCollision: {fileID: 11400000, guid: c011151000004e66b200000000000002') -or $knapped -match 'OnCollision:.*guid:') {throw 'Only whole Flint should have a collision graph.'}
$collisionResult=Get-Content (Join-Path $root 'Configs/Items/Flint/SpawnShards.asset') -Raw
if (!$collisionResult.Contains('Count: 2') -or !$collisionResult.Contains('guid: c019f11a00004e66b200000000000002')) {throw 'Collision must create two flint shards.'}
if ((Test-Path (Join-Path $root 'Resources/Stone.asset')) -or (Test-Path (Join-Path $root 'Configs/Items/Flint/Knapping.asset'))) {throw 'Retired Stone/Knapping assets still present.'}
foreach ($newGuid in 'c019f11a00004e66b200000000000002','c019f11a00004e66b200000000000003') {
 if (!$db.Contains($newGuid)) {throw "New item missing from library: $newGuid"}
}
if ($flint -notmatch '(?m)^  Id: 4$' -or $ignite -notmatch '(?m)^  Id: 13$') {throw 'Flint/skill IDs changed.'}
if (!$db.Contains('ab11c01f00004e66b200000000000001')) {throw 'Flint not registered.'}
if (!$range.Contains('6c46bfa4e2fd4c6da648b787b9d6724e')) {throw 'Ignite no longer shares Pick distance.'}
if (!$action.Contains('ReplacementItem: {fileID: 11400000, guid: 22b3719fb2c9947c28e5c2258dc66c34') -or $action.Contains('Replacements:')) {throw 'Replacement is not a generic single-target action.'}
if (!$ignite.Contains('UseChain: 1') -or !$ignite.Contains('FirstNode: {fileID: 11400000, guid: ab11c01f00004e66b200000000000006') -or !$ignite.Contains('Conditions: []') -or !$ignite.Contains('Actions: []')) {throw 'Ignite is not a flow skill.'}
$hand=Get-Content (Join-Path $root 'Configs/Items/Flint/IgniteOtherHand.asset') -Raw
$target=Get-Content (Join-Path $root 'Configs/Items/Flint/IgniteWorldTarget.asset') -Raw
$step=Get-Content (Join-Path $root 'Configs/Items/Flint/IgniteChainAction.asset') -Raw
if (!$hand.Contains('HandCondition: {fileID: 11400000, guid: ab11c01f00004e66b200000000000003') -or !$hand.Contains('Next: {fileID: 11400000, guid: ab11c01f00004e66b20000000000000a')) {throw 'Other-hand requirement missing.'}
if (!$range.Contains('Next: {fileID: 11400000, guid: ab11c01f00004e66b200000000000007') -or !$target.Contains('Output: {fileID: 11400000, guid: ab11c01f00004e66b200000000000009')) {throw 'Ignite data path is disconnected.'}
if (!$target.Contains('Item: {fileID: 11400000, guid: 21863c02d6dcf45e395849a4c0b02231') -or !$step.Contains('PositionSource: {fileID: 11400000, guid: ab11c01f00004e66b200000000000007')) {throw 'World Item reference is not passed to replacement.'}
'Flint PASS: actual replacement/surface logic, half heights, negative coordinates, resource retention, registered assets, ignition pair and shared Pick distance. Unity gameplay/spawn integration not executed.'
