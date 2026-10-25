$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read($path) { Get-Content (Join-Path $root $path) -Raw }
function Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, "internal static [^\r\n]+\b$name\(")
    if (!$match.Success) { throw "Missing production method $name" }
    $open = $source.IndexOf('{', $match.Index)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($match.Index, $end - $match.Index).Replace('internal static', 'public static')
}
$system = Read 'Scripts/World/Trees/TreeCoarseSunShadowSystem.cs'
$trace = Read 'Shaders/TreeShadowTrace.hlsl'
$compute = (Read 'Resources/TreeCoarseSunVisibility.compute').Replace('#include "../Shaders/TreeShadowTrace.hlsl"', $trace)
# Reuse only the real Windows D3D compiler helper; unrelated sprite fixtures
# have their own macro adapter and must not affect this test.
$compilerHelper = [regex]::Match((Read 'Tools/ItemGraphEditor/Editor/WorldItemBatchShaderChecks.ps1'), "(?s)Add-Type -TypeDefinition @'(.*?)'@").Groups[1].Value
Add-Type -TypeDefinition $compilerHelper
[ItemBatchShaderCompile]::Compile($compute, 'BuildChunk', 'cs_5_0')
$coarse = Read 'Shaders/TreeCoarseSunLighting.hlsl'
[ItemBatchShaderCompile]::Compile(($coarse + "`nfloat4 Check(float4 p:SV_Position):SV_Target { return CoarseTreeSunVisibility(p.xy,p.z); }"), 'Check', 'ps_5_0')
$terrain = Read 'Shaders/ChunkProcedural.shader'
$sunFunction = [regex]::Match($terrain, '(?s)float GpuSunVisibility\(.*?\n            \}').Value
$sunTrace = (Read 'Shaders/GpuSunTrace.hlsl').Replace('#include "TreeShadowTrace.hlsl"', $trace)
$composition = @'
float _GpuSunEnabled, _DetailedLightingRadius;
float4 _GpuSunFeet, _GpuSunProjection, _GpuSunBasis, _GpuSunOrigin, _GpuSunDirection;
StructuredBuffer<uint> _GpuSunCache, _PlayerLightOccupancy;
float4 _PlayerLightGridOrigin;
'@ + "`n" + $coarse + "`n" + $sunTrace + "`n" + $sunFunction + "`n" + @'
float4 Check(float4 p:SV_Position):SV_Target { return GpuSunVisibility(p.xy,p.z,float3(0,1,0),1); }
'@
[ItemBatchShaderCompile]::Compile($composition, 'Check', 'ps_5_0')
$version = ((Read '../ProjectSettings/ProjectVersion.txt') -split "`n" | Where-Object { $_ -match '^m_EditorVersion: ' }) -replace '^m_EditorVersion: ', ''
$math = "C:/Program Files/Unity/Hub/Editor/$($version.Trim())/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll"
Add-Type -Path $math
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $math
$intersection = [regex]::Match($trace, '(?s)bool TreeVolumeHit\(.*?\n\}').Value.Replace('bool TreeVolumeHit', 'static bool TreeVolumeHit')
$intersection = $intersection -replace '\bfloat3\(', 'new float3(' -replace '(?<![\w.])(\d*\.\d+)(?![\w.])', '${1}f'
$source = @'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public struct ChunkTree { public float3 Position; public float Scale,TrunkRadius,TrunkHeight; }
public struct PineShadowVolume { public float4 A,B; }
public static class CoarseSunChecks {
 const float TraceDistance=32;
 static void Check(bool value,string message) { if(!value)throw new Exception(message); }
 public static string Run() {
  var tree=new ChunkTree{Position=new float3(.5f,12,.5f),Scale=1,TrunkRadius=.23f,TrunkHeight=10};
  var shapes=new List<PineShadowVolume>(); AddProxy(shapes,tree,2.5f,.58f);
  Check(shapes.Count==4 && shapes[0].B.w==1,"Expected exactly one trunk plus three crown lobes");
  for(int i=1;i<4;i++) {
   Check(shapes[i].B.w==0 && all(shapes[i].B.xyz>0),"Invalid crown ellipsoid");
   Check(shapes[i].A.y>tree.Position.y,"Crown below tree base");
   if(i>1)Check(shapes[i].B.x<shapes[i-1].B.x,"Crown does not taper");
  }
  foreach(float height in new[]{2f,8f,12f}) {
   var receiver=new float3(-1,height,.5f);
   var target=receiver+normalize(new float3(.1f,1,0))*32;
   bool hit=false; foreach(var shape in shapes)hit|=TreeVolumeHit(receiver,target,shape);
   Check(hit,"Shadow lost on a lower receiver/ledge");
  }
  foreach(var sun in new[]{new float3(1,0,0),new float3(-1,0,0),new float3(0,0,1),new float3(0,0,-1)}) {
   int2 receiverChunk=(int2)floor((tree.Position.xz-sun.xz*20)/16);
   Check(Affects(tree,receiverChunk,sun,2.5f),"Caster in another chunk was culled");
   Check(!Affects(tree,receiverChunk+new int2(20),sun,2.5f),"Unrelated distant caster was kept");
  }
  foreach(int x in new[]{-17,-16,-1,0,15,16,17}) {
   int cell=(int)floor(x/2f),chunk=(int)floor(cell/8f),local=cell-chunk*8;
   Check(local>=0 && local<8 && chunk==(int)floor(x/16f),"Negative chunk/cache address mismatch");
  }
  shapes.Clear(); tree.Position=new float3(-17,4,-1);tree.TrunkHeight=5;tree.TrunkRadius=.1f;
  AddProxy(shapes,tree,1,.85f);
  Check(shapes.Count==4 && all(shapes[0].A.xyz==tree.Position),"Scaled/manual tree proxy moved its base");
  return "PASS actual coarse proxy/culling methods: four volumes, taper, lower ledges, cross-chunk casters, four sun directions, negative addressing.";
 }
'@ + (Method $system 'Affects') + (Method $system 'AddProxy') + $intersection + "`n}"
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[CoarseSunChecks]::Run()
foreach ($pattern in 'BuildOne\(feet\)', 'nextScan = now \+ .25', '!Pending\(\)', 'cache.DesiredHash = hash', 'caches.Remove\(coordinate\)', 'visibilityBuffer\?\.Release', 'OnStopRunning\(\) => Disable', 'UpdateBefore\(typeof\(WorldProjectionDrawSystem\)\)') {
    if ($system -notmatch $pattern) { throw "Missing cache/lifecycle contract: $pattern" }
}
if ($terrain -notmatch 'if \(bound <= radius-1\) return visible' -or $terrain -notmatch 'lerp\(visible,min\(legacy,CoarseTreeSunVisibility') { throw 'Coarse and detailed sunlight must blend, not multiply or trace coarse trees in the near interior' }
$water = Read 'Shaders/ChunkWaterProcedural.shader'
if ($water -notmatch '#include "TreeCoarseSunLighting.hlsl"' -or $water -notmatch 'CoarseTreeSunVisibility\(input.ProjectionPosition, input.ShadowReceiverHeight\)') { throw 'Water receivers missing' }
if ($system -match 'BlockModification|SetComponentEnabled<ChunkNeeds|GetBuffer<BlockData>|GetBuffer<SplitCellData>') { throw 'Coarse shadows must not mutate/rebuild world projection or occupancy' }
'PASS cache/source contracts, compute kernel and coarse/detailed fragment composition D3DCompile. Unity visual/performance validation still required.'
