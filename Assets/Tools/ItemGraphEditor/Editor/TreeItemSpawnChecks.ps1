$ErrorActionPreference = 'Stop'
$fixture = Get-Content (Join-Path $PSScriptRoot 'HalfBlockProjectionChecks.ps1') -Raw
$fixture = $fixture.Replace('$PSScriptRoot', "'" + $PSScriptRoot.Replace("'", "''") + "'")
Invoke-Expression $fixture.Substring(0, $fixture.IndexOf('Add-Type -TypeDefinition $halfSource'))
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read($path) { Get-Content (Join-Path $root $path) -Raw }
$spawn = Read 'Scripts/Items/TreeItemSpawnSystem.cs'
$start = $spawn.IndexOf('    public static class TreeItemPlacement')
$end = $spawn.IndexOf('    [UpdateInGroup', $start)
$utility = 'namespace Game.Items {' + $spawn.Substring($start, $end-$start) + '}'
$test = @'
public static class TreeItemChecks {
 static void Check(bool value,string message) { if(!value)throw new System.Exception(message); }
 static bool Valid(Game.World.Saving.WorldStateData state) {
  try { typeof(Game.World.Saving.WorldStateStorage).GetMethod("Validate",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{state,"tree",7u}); return true; }
  catch(System.Reflection.TargetInvocationException e) when(e.InnerException is System.IO.InvalidDataException) { return false; }
 }
 public static string Run() {
  int large=0,small=0;
  for(int i=0;i<1000;i++) {
   var chunk=new Unity.Mathematics.int2(i%20-10,i/20-25);
   uint hash=Game.Items.TreeItemPlacement.Hash(chunk,1,7);
   Check(hash==Game.Items.TreeItemPlacement.Hash(chunk,1,7),"Nondeterministic population");
   int count=Game.Items.TreeItemPlacement.SmallCount(hash);
   Check(count>=1&&count<=2,"Small branch count");small+=count;
   if(Game.Items.TreeItemPlacement.HasLarge(hash))large++;
   var tree=new Unity.Mathematics.float3(chunk.x*16+7.5f,20,chunk.y*16+7.5f);
   for(int attempt=0;attempt<12;attempt++) {
    var cell=Game.Items.TreeItemPlacement.Candidate(tree,hash,0,attempt);
    var center=new Unity.Mathematics.float2(cell.x+.5f,cell.y+.5f);
    float distance=Unity.Mathematics.math.distance(tree.xz,center);
    Check(distance>.5f&&distance<3.8f,"Candidate not near its tree");
   }
  }
  Check(large>250&&large<420&&small>1400&&small<1600,"Distribution drift");
  var basePosition=new Unity.Mathematics.float3(0,20,0);
  foreach(var material in new[]{Game.World.Blocks.BlockId.Turf,Game.World.Blocks.BlockId.Sand,Game.World.Blocks.BlockId.Stone})
   Check(Game.Items.TreeItemPlacement.ValidSurface(basePosition,.3f,new Unity.Mathematics.float3(2,20.58f,0),material),"Dry half-height support rejected");
  Check(!Game.Items.TreeItemPlacement.ValidSurface(basePosition,.3f,new Unity.Mathematics.float3(2,20.08f,0),Game.World.Blocks.BlockId.OceanWater),"Branch spawned on water");
  Check(!Game.Items.TreeItemPlacement.ValidSurface(basePosition,.3f,new Unity.Mathematics.float3(2,24.08f,0),Game.World.Blocks.BlockId.Turf),"Branch jumped a cliff");
  Check(!Game.Items.TreeItemPlacement.ValidSurface(basePosition,.3f,new Unity.Mathematics.float3(0,20.08f,0),Game.World.Blocks.BlockId.Turf),"Branch overlaps trunk");
  var state=new Game.World.Saving.WorldStateData { Version=10,WorldId="tree",Seed=7,ChunkSizeX=16,ChunkSizeY=256,ChunkSizeZ=16,
   Chunks=System.Array.Empty<Game.World.Saving.ChunkChangesData>(),Equipment=System.Array.Empty<Game.World.Saving.EquipmentSlotData>(),
   TreeItemChunks=new[]{new Game.World.Saving.TreeItemChunkData{X=-1,Z=2},new Game.World.Saving.TreeItemChunkData{X=1,Z=-2}} };
  Check(Valid(state),"New spawn ledger rejected");
  state.TreeItemChunks[1]=state.TreeItemChunks[0];Check(!Valid(state),"Duplicate ledger accepted");
  state.TreeItemChunks=new Game.World.Saving.TreeItemChunkData[]{null};Check(!Valid(state),"Null ledger entry accepted");
  state.TreeItemChunks=null;state.Version=9;Check(Valid(state),"Older world rejected");
  state.Version=10;state.SimulationSeconds=10;
  state.WorldItems=new[]{new Game.World.Saving.WorldItemData{Instance=new Game.Items.ItemInstance{DefinitionId=8,Durability=100,MaxDurability=100},X=-15.5f,Y=20.08f,Z=2.5f,Grounded=true,CollisionRadius=.08f,Gravity=9.81f,LastSimulationSeconds=10}};
  Check(Valid(state),"Saved branch rejected");
  return "PASS actual placement/hash and save validation: deterministic counts, rarity, negative coordinates, dry half-height support, cliffs/trunks/water, v9 compatibility, v10 ledger validation and saved branches.";
 }
}
'@
Add-Type -TypeDefinition ($halfSource + $utility + $test) -ReferencedAssemblies $halfReferences
[TreeItemChecks]::Run()
$database = Read 'Configs/ItemDatabase.asset'
Add-Type -AssemblyName System.Drawing
foreach ($spriteName in 'branch','large_branch') {
 foreach ($suffix in '', '_world') {
  $relative = "Art/UI/Icons/$spriteName$suffix.png"
  $bitmap = [System.Drawing.Bitmap]::new((Join-Path $root $relative))
  try {
   $expectedHeight = if ($suffix -eq '_world') { 16 } else { 32 }
   if ($bitmap.Width -ne 32 -or $bitmap.Height -ne $expectedHeight) { throw "Wrong actual PNG size: $relative" }
   if ($bitmap.GetPixel(0,0).A -ne 0) { throw "Lost alpha: $relative" }
   $opaqueCount=0
   for ($y=0; $y -lt $bitmap.Height; $y++) {
    for ($x=0; $x -lt $bitmap.Width; $x++) { if ($bitmap.GetPixel($x,$y).A -ge 128) { $opaqueCount++ } }
   }
   if ($opaqueCount -lt 8) { throw "Sprite is empty: $relative" }
  } finally { $bitmap.Dispose() }
 }
 $definition = if ($spriteName -eq 'branch') { 'Branch' } else { 'LargeBranch' }
 $asset = Read "Resources/$definition.asset"
 $worldGuid = [regex]::Match((Read "Art/UI/Icons/$($spriteName)_world.png.meta"),'guid: ([a-f0-9]+)').Groups[1].Value
 if ($asset -notmatch "WorldSprite: \{fileID: 21300000, guid: $worldGuid," -or $asset -match "Icon: \{fileID: 21300000, guid: $worldGuid,") {
  throw "Icon and world sprite must be separate: $definition"
 }
}
foreach ($name in 'Branch','LargeBranch') {
 $asset=Read "Resources/$name.asset"
 $guid=[regex]::Match((Read "Resources/$name.asset.meta"),'guid: ([a-f0-9]+)').Groups[1].Value
 if ($database -notmatch $guid -or $asset -notmatch 'WorldSprite: \{fileID: 21300000' -or $asset -notmatch 'AllowedSlots: 3') { throw "Unregistered/non-renderable item $name" }
}
foreach ($pattern in 'visited.Contains\(coordinate\)', 'pending.Clear\(\)', 'occupied.Contains\(cell\)', 'tree.PlacedByPlayer', 'visited.Add\(coordinate\)', 'GetBuffer<TreeItemSpawnedChunk>\(owner\).Add', 'break; // At most one', 'WorldItemEntityUtility.Create') {
 if ($spawn -notmatch $pattern) { throw "Missing spawn lifecycle contract: $pattern" }
}
$save=Read 'Scripts/World/Saving/Systems/WorldStateSystem.cs'
if ($save -notmatch 'data.TreeItemChunks' -or $save -notmatch 'TreeItemChunks = CaptureTreeItemChunks\(\)') { throw 'Spawn ledger restore/capture missing' }
'PASS registered item assets and bounded/once-per-chunk spawn + save/restore contracts. Unity sprite import, pickup and visual distribution require Play Mode.'
