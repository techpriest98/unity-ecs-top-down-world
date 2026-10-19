$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/Items/WorldItemSleepSystem.cs') -Raw
$method = [regex]::Match($source, '(?s)public static bool Supports\(.*?;').Value
if (!$method) { throw 'Support predicate missing' }
$code = @'
public enum BlockId { Air, Stone, Sand }
public struct Part { public BlockId Id; }
public struct CellContents { public Part Lower,Upper; }
public static class BlockUtility { public static bool IsSolid(BlockId id)=>id!=BlockId.Air; }
public static class SleepChecks {
'@
$tests = @'
public static void Run(){
 var full = new CellContents {Lower=new Part{Id=BlockId.Stone},Upper=new Part{Id=BlockId.Stone}};
 var half = new CellContents {Lower=new Part{Id=BlockId.Stone}};
 var upper = new CellContents {Upper=new Part{Id=BlockId.Sand}};
 foreach(int y in new[]{0,40,127}){
  if(!Supports(full,y,y+1f)||!Supports(half,y,y+.5f)||!Supports(upper,y,y+1f))throw new System.Exception("Valid support rejected");
  if(Supports(default,y,y+1f)||Supports(half,y,y+1f)||Supports(full,y,y+.5f)||Supports(upper,y,y+.5f))throw new System.Exception("Missing or obstructed support accepted");
 }
}
}
'@
Add-Type -TypeDefinition ($code+$method+$tests)
[SleepChecks]::Run()
$motion = Get-Content (Join-Path $root 'Scripts/Items/WorldItemMotionSystem.cs') -Raw
if (!$motion.Contains('if (awakeItems.IsEmptyIgnoreFilter) return;') -or !$motion.Contains('.WithNone<WorldItemSleeping>()')) { throw 'Sleeping items must be excluded from motion' }
if (!$source.Contains('WithChangeFilter<BlockData>()')) { throw 'Sleeping support must be terrain-change driven' }
'PASS actual support predicate: full/lower/upper halves, removed support, obstructed half; static sleep exclusion and terrain-change gate. Unity ECS wake/load integration and FPS require Play Mode.'
