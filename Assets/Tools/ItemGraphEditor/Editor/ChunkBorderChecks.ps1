$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader=Get-Content (Join-Path $root 'Shaders/ChunkBorders.hlsl') -Raw
$function=[regex]::Match($shader,'(?s)float2 ChunkBorderCoordinates\(.*?\n\}').Value
if(!$function){throw 'Production border coordinate helper missing'}
$function=$function -replace '^float2 ChunkBorderCoordinates','static float2 ChunkBorderCoordinates'
$function=$function -replace '\bfloat2\(', 'new float2('
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$source=@'
using System;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public static class ChunkBorderChecks {
 static float4 _ChunkBorderFacing;
'@+$function+@'
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static string Run(){
  foreach(var toward in new[]{new float2(0,1),new float2(-1,0),new float2(0,-1),new float2(1,0)}){
   _ChunkBorderFacing=new float4(toward.x,0,toward.y,0);
   var right=new float2(toward.y,-toward.x);
   for(int x=0;x<16;x++)for(int z=0;z<16;z++){
    uint packed=(uint)((73<<8)|(z<<4)|x)<<16;
    float expectedU=math.dot(new float2(x,z),right)+(math.cmin(right)<0?15:0);
    float expectedV=math.dot(new float2(x,z),toward)+(math.cmin(toward)<0?15:0);
    var top=ChunkBorderCoordinates(packed,new float2(0,1),0);
    var bottom=ChunkBorderCoordinates(packed,new float2(1,0),0);
    Check(math.distance(top,new float2(expectedU,expectedV))<.00001f,"View rotation misaligned top chunk border");
    Check(math.distance(bottom,new float2(expectedU+1,expectedV+1))<.00001f,"Corner orientation inconsistent");
    Check(math.all(top>=0)&&math.all(bottom<=16),"Border coordinates escaped chunk");
    var side=ChunkBorderCoordinates(packed,new float2(1,0),1);
    Check(side.x==expectedU+1&&side.y==expectedV,"Side stripes do not align with tops");
   }
  }
  return "PASS actual shader chunk coordinates: all 256 cells, four camera directions, corner orientation and side/top alignment.";
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[ChunkBorderChecks]::Run()
$ui=Get-Content (Join-Path $root 'Scripts/UI/FpsCounter.cs') -Raw
$renderer=Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.cs') -Raw
if($ui -notmatch 'keyboard.f9Key.wasPressedThisFrame' -or
 $ui.IndexOf('keyboard.f9Key.wasPressedThisFrame') -gt $ui.IndexOf('if (overlay == null || !overlay.enabled) return;') -or
 $ui -notmatch 'Chunk Borders: On' -or $ui -notmatch 'Chunk Borders: Off' -or
 $renderer -notmatch 'SetFloat\("_ChunkBordersEnabled", ChunkBorderDebug.Enabled' -or
 $shader -notmatch 'if \(_ChunkBordersEnabled < .5\) return color'){
 throw 'Borders must toggle independently of overlay visibility and publish state without chunk rebuild'
}
foreach($name in 'ChunkProcedural','ChunkWaterProcedural'){
 $terrain=Get-Content (Join-Path $root "Shaders/$name.shader") -Raw
 if($terrain -notmatch 'ChunkBorderCoordinates\(cell.FaceData,corner,' -or $terrain -notmatch 'ApplyChunkBorders\(color.rgb,input.ChunkBorderUv,input.FaceIndex\)'){
  throw "Missing border integration in $name"
 }
}
'PASS F9 independent toggle, F3 state, shared terrain/water integration and disabled shader fast path. Unity visual/input checks remain manual.'
if ($ui.IndexOf('keyboard.f10Key.wasPressedThisFrame') -lt 0 -or
 $ui.IndexOf('keyboard.f10Key.wasPressedThisFrame') -gt $ui.IndexOf('if (overlay == null || !overlay.enabled) return;')) {
 throw 'F10 must work independently of F3 overlay visibility'
}
$gate=Get-Content (Join-Path $root 'Scripts/World/Rendering/Clipping/Components/ChunkShaderClippingEnabled.cs') -Raw
if ($gate -notmatch 'SharedStatic<int>' -or $gate -notmatch 'SetComponentEnabled<ChunkNeedsProjection>\(chunks, true\)') {
 throw 'Burst-safe world render gate or resume invalidation missing'
}
foreach ($path in @('Scripts/World/Rendering/ChunkProjectionSystem.cs',
 'Scripts/World/Lighting/Systems/ChunkLightingSystem.cs',
 'Scripts/World/Rendering/ChunkProceduralRenderSystem.cs',
 'Scripts/World/Rendering/ChunkProceduralRenderer.cs',
 'Scripts/World/Trees/PineTreeTestSystem.cs',
 'Scripts/Items/WorldItemRenderSystem.cs',
 'Scripts/Player/Visual/PlayerSunShadowRenderSystem.cs')) {
 if ((Get-Content (Join-Path $root $path) -Raw) -notmatch '!.*WorldRenderDebug\.(Enabled|ProjectionRequired)') {
  throw "Missing world render gate: $path"
 }
}
'PASS F10 input, visual consumer gates and forced projection refresh on resume (source contracts).'
if ($gate -notmatch 'ProjectionRequired => Enabled \|\| OverlayRequested' -or
 $ui -notmatch 'Local overlay: On' -or
 $ui -notmatch 'LocalProjectionCellCount') { throw 'Independent overlay gate/status missing' }
$draw=Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.cs') -Raw
$localDraw=Get-Content (Join-Path $root 'Scripts/World/Rendering/LocalProjectionRenderer.cs') -Raw
if ($draw -notmatch 'if \(WorldRenderDebug.Enabled\) \{ RenderOpaque\(\); RenderClipped\(\); RenderWater\(\); RenderClippedWater\(\); \}' -or
 $draw -notmatch 'if \(WorldRenderDebug.Enabled\) RenderWorldFog\(\)' -or
 $localDraw -notmatch 'OnDisable\(') { throw 'Overlay-only draw isolation or disable cleanup missing' }
'PASS overlay-only preparation/draw gate, full-world fog isolation and F3 counts (source contracts).'
foreach ($path in @('Shaders/ChunkProcedural.shader','Shaders/ChunkWaterProcedural.shader','Shaders/WorldFogMask.shader',
 'Scripts/World/Rendering/ChunkProceduralRenderer.cs','Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs',
 'Scripts/World/Rendering/ChunkProceduralRenderSystem.cs')) {
 if ((Get-Content (Join-Path $root $path) -Raw) -match '_LocalProjectionCircle|_LocalProjectionMode|SetLocalProjectionCircle|Intersects\(face') {
  throw "Unexpected pixel-cropping overlay mask: $path"
 }
}
'PASS local projection tiles are not cropped by a circular shader mask.'
$background=Get-Content (Join-Path $root 'Shaders/LocalProjectionBackground.shader') -Raw
if ($background -notmatch 'Geometry-1' -or $background -notmatch 'ZWrite Off' -or
 $background -notmatch 'ZTest Always' -or $localDraw -notmatch '!localLayer && IsActive' -or
 $localDraw -notmatch 'TextureFormat.R8' -or $localDraw -notmatch 'filterMode = FilterMode.Point') {
 throw 'Whole-tile background/coverage draw ordering or local-layer exemption missing'
}
'PASS whole-tile replacement covers empty cells without altering world buffers or local fragment shapes (source contracts).'
if ($draw -match 'localProjectionBuffer|localWaterBuffer|localTileBuffer|UploadLocalCoverage|clippedMaterial|clippedWaterMaterial|UploadClipMask' -or
 $localDraw -notmatch 'GetComponent<ChunkProceduralRenderer>' -or $ui -notmatch 'var terrain = LocalProjectionRenderer.Instance') {
 throw 'Local projection resources still owned by chunk renderer or debug UI is bound to the wrong component'
}
foreach ($path in @('Art/World/ChunkProceduralClipped.mat','Art/World/ChunkWaterProceduralClipped.mat')) {
 if (Test-Path (Join-Path $root $path)) { throw "Retired clipping material remains: $path" }
}
'PASS local resource ownership, separate component/debug binding and removal of retired clipping materials.'
