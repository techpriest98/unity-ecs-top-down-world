# Actual window math and full-versus-bounded builder output with ECS/buffer adapters.
$ErrorActionPreference = 'Stop'
$fixture = Get-Content (Join-Path $PSScriptRoot 'HalfBlockProjectionChecks.ps1') -Raw
$fixture = $fixture.Replace('$PSScriptRoot', "'" + $PSScriptRoot.Replace("'", "''") + "'")
# Load the shared source/adapters without running its unrelated baseline suite.
Invoke-Expression $fixture.Substring(0, $fixture.IndexOf('Add-Type -TypeDefinition $halfSource'))
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$windowSource = Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProjectionViewportSystem.cs') -Raw
$start = $windowSource.IndexOf('    public static class ChunkProjectionWindow')
$end = $windowSource.IndexOf('    // Managed camera access', $start)
$window = "namespace Game.World.Rendering {`n" + $windowSource.Substring($start, $end-$start) + "`n}"
$harness = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using Game.World.Rendering;
using Game.World.Chunks;
using Game.World.Blocks;
public static class ProjectionWindowChecks {
 static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
 static (List<ProjectedCellData>,List<ProjectedCellData>) Build(Dictionary<int3,BlockData> map,ViewDirection view,bool clip,int first,int last) {
  var method=typeof(HalfProjectionChecks).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Static);
  return ((List<ProjectedCellData>,List<ProjectedCellData>))method.Invoke(null,new object[]{map,view,clip,null,70f,0,int.MaxValue,7f,7f,null,first,last});
 }
 static string Key(ProjectedCellData c)=>c.ProjectionX+":"+c.ProjectionY+":"+c.FaceData+":"+c.SourceAirIndex+":"+c.BlockData+":"+c.TopFrontMaterial;
 static int Count(int2 rows) {
  var t=ChunkProjectionBuilder.ProjectionTraversalRange(256,16,rows.x,rows.y);int n=0;
  for(int h=t.x;h<t.y;h++)n+=Math.Max(0,Math.Min(15,t.w-h*2)-Math.Max(0,t.z-h*2)+1);
  return n*16;
 }
 public static string Run() {
  var visible=new float4(-12,52,12,76);
  var cached=ChunkProjectionWindow.Expand(visible);
  Check(ChunkProjectionWindow.Contains(cached,visible),"Window did not contain camera");
  Check(!ChunkProjectionWindow.NeedsRefresh(cached,visible+new float4(1,1,1,1)),"Small camera motion rebuilt window");
  Check(ChunkProjectionWindow.NeedsRefresh(cached,visible+new float4(0,40,0,40)),"Height move failed to request new rows");
  Check(ChunkProjectionWindow.NeedsRefresh(cached,visible+new float4(-40,-40,40,40)),"Zoom-out did not request coverage");
  var source=new Dictionary<int3,BlockData>();
  for(int x=0;x<16;x++)for(int z=0;z<16;z++) {
   int surface=56+(x*3+z*5)%32;
   for(int y=52;y<=surface;y++)source[new int3(x,y,z)]=new BlockData(BlockId.Stone,255);
   if((x+z)%5==0)source[new int3(x,surface,z)]=new BlockData(BlockId.Stone,255,BlockShape.LowerHalf);
   if((x+z)%7==0)source[new int3(x,surface+1,z)]=new BlockData(BlockId.OceanWater,255);
  }
  int largest=0;
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) {
   foreach(var coordinate in new[]{new int2(-3,2),new int2(0),new int2(4,-5)}) {
    var position=ChunkRenderPositionUtility.GetPosition(coordinate,view,1,.5f,.01f);
    var window=cached+new float4(position.xy,position.xy);
    Check(ChunkProjectionWindow.TryRows(coordinate,view,window,out var r),"Valid chunk culled");
    Check(ChunkProjectionWindow.TryRows(new int2(0),view,cached,out var originRows)&&math.all(r==originRows),"Chunk translation/negative coordinates changed row mapping");
    Check(!ChunkProjectionWindow.TryRows(coordinate,view,window+new float4(1000,0,1000,0),out _),"Horizontally invisible chunk selected");
    Check(!ChunkProjectionWindow.TryRows(coordinate,view,window+new float4(0,1000,0,1000),out _),"Vertically invisible chunk selected");
   }
   Check(ChunkProjectionWindow.TryRows(new int2(0),view,cached,out var rows),"Missing test rows");
   largest=Math.Max(largest,Count(rows));
   Check(Count(rows)<65536/3,"Representative window did not reduce traversal sufficiently");
   foreach(bool clip in new[]{false,true}) {
    var full=Build(source,view,clip,-10000,10000);
    var bounded=Build(source,view,clip,rows.x,rows.y);
    Func<ProjectedCellData,bool> inside=c=>c.ProjectionY>=rows.x&&c.ProjectionY<=rows.y;
    Check(full.Item1.Where(inside).Select(Key).SequenceEqual(bounded.Item1.Where(inside).Select(Key)),"Bounded solid/half/cut output differs from full build");
    Check(full.Item2.Where(inside).Select(Key).SequenceEqual(bounded.Item2.Where(inside).Select(Key)),"Bounded water output differs from full build");
   }
  }
  return "PASS actual screen window and bounded builder: 4 views, negative chunks, solid/half/water/cut parity, hysteresis, zoom, offscreen rejection. Visited cells 65536 -> "+largest+" in representative padded window.";
 }
}
'@
$extraUsings = [regex]::Matches($harness, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
$allUsings = (($halfUsings + $extraUsings | Sort-Object -Unique) -join "`n")
$harness = [regex]::Replace($harness, '(?m)^using [^;]+;\r?\n', '')
Add-Type -TypeDefinition ($allUsings + "`n" + ($halfBodies -join "`n") + "`n" + $halfHarness + "`n" + $harness + "`n" + $window) -ReferencedAssemblies $halfReferences
[ProjectionWindowChecks]::Run()
$system = Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProjectionSystem.cs') -Raw
foreach ($pattern in 'viewportChanged && !transitionActive', 'viewport.Enabled && !transitionActive', 'rows.x, rows.y', 'cached.Rows.x > required.x', 'projectionSnapshots.Remove\(entity\)', 'MaxChunksProjectedPerFrame = 1') {
 if ($system -notmatch $pattern) { throw "Missing projection/cache contract: $pattern" }
}
if ($system -match 'Camera.main') { throw 'Managed camera access entered Burst system' }
'PASS source contracts: cached viewport enqueue, transition fallback, offscreen deferral and unchanged per-frame chunk budget. Unity camera/GPU timing remains manual.'
