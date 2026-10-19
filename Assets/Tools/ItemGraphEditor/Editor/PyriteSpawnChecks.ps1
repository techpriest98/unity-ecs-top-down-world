$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/Items/CoastalItemSpawnSystem.cs') -Raw
if ($source.Contains('Resources.Load') -or $source.Contains('registered != flint') -or $source.Contains('registeredPyrite != pyrite')) {throw 'Spawner must use baked references, not Resource object identity'}
foreach ($entry in @(@('Flint',4),@('Pyrite',7))) {
 $asset=Get-Content (Join-Path $root ('Resources/'+$entry[0]+'.asset')) -Raw
 if ($asset -notmatch ('(?m)^  Id: '+$entry[1]+'\r?$')) {throw 'Spawner stable ID differs from item asset'}
}
if (!$source.Contains('out flint') -or !$source.Contains('out pyrite') -or !$source.Contains('[CoastalItemSpawn]')) {throw 'Baked lookup or build diagnostics missing'}
$method=[regex]::Match($source,'(?s)public static bool ShouldSpawnPyrite\(.*?;').Value
if (!$method) {throw 'Spawn predicate missing'}
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$adapter='using Unity.Mathematics; public enum WorldBiome {RockyShore,Other} public enum BlockId {Stone,Sand,Turf,OceanWater,Air} public static class Checks {'
$tests=@'
 public static void Run(){
 int found=0;
 for(int z=-128;z<128;z++)for(int x=-128;x<128;x++){
  var p=new int2(x,z);
  bool result=ShouldSpawnPyrite(WorldBiome.RockyShore,BlockId.Stone,p,42);
  if(result)found++;
  if(result!=ShouldSpawnPyrite(WorldBiome.RockyShore,BlockId.Stone,p,42))throw new System.Exception("Nondeterministic spawn");
  foreach(var material in new[]{BlockId.Sand,BlockId.Turf,BlockId.OceanWater,BlockId.Air})
   if(ShouldSpawnPyrite(WorldBiome.RockyShore,material,p,42))throw new System.Exception("Covered/non-stone surface accepted");
  if(ShouldSpawnPyrite(WorldBiome.Other,BlockId.Stone,p,42))throw new System.Exception("Wrong biome accepted");
 }
 if(found<700||found>1400)throw new System.Exception("Unexpected spawn density");
 }
}
'@
Add-Type -TypeDefinition ($adapter+$method+$tests) -ReferencedAssemblies $refs
[Checks]::Run()
if (!$source.Contains('pyritePositions.Count < 2') -or !$source.Contains('visited.Contains(coordinate)')) {throw 'Cap or spawn ledger missing'}
'PASS production spawn predicate: deterministic positive/negative coordinates, Rocky Shore stone only, density, static cap and shared ledger. Gameplay not executed.'
