$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $PSScriptRoot 'WorldItemBatchShaderChecks.ps1')
$shader=Get-Content (Join-Path $root 'Shaders/PlayerSunShadowMask.shader') -Raw
$program=[regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
$program=[regex]::Replace($program,'(?m)^\s*#include[^\r\n]*','')
$adapter=@'
#define TEXTURE2D_ARRAY(n) Texture2DArray<float4> n
#define SAMPLE_TEXTURE2D_ARRAY(t,s,u,p) t.Sample(s,float3(u,p))
'@
foreach($top in 0..1){
 [ItemBatchShaderCompile]::Compile(("#define UNITY_UV_STARTS_AT_TOP $top`n"+$adapter+$program),'Vert','vs_5_0')
 [ItemBatchShaderCompile]::Compile(("#define UNITY_UV_STARTS_AT_TOP $top`n"+$adapter+$program),'Frag','ps_5_0')
}
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$production=''
foreach($path in @('Scripts/Player/Visual/PlayerSunShadowUtility.cs','Scripts/World/Rendering/ViewDirection.cs','Scripts/World/Rendering/ViewDirectionUtility.cs','Scripts/World/Rendering/WorldPositionProjectionUtility.cs','Scripts/World/Rendering/ChunkRenderPositionUtility.cs')){
 $production+=(Get-Content (Join-Path $root $path) -Raw)-replace '(?m)^using [^;]+;\r?\n',''
}
$tests=@'
using System;
using Unity.Mathematics;
using Game.World.Rendering;
using Game.World.Chunks;
namespace Game.World.Chunks { public static class ChunkSettings { public const int SizeX=16,SizeY=256,SizeZ=16; } }
public static class PlayerSunShadowChecks {
 static void Check(bool v,string s){if(!v)throw new Exception(s);}
 public static void Run(){
  var directional=math.normalize(new float3(.2f,.8f,.5f));
  foreach(float distance in new[]{.002f,.02f,.1f,.5f,2f}) {
   float s=2f/98;
   var hit=new float3(.1f,directional.y*distance,0);
   var receiver=hit-directional*distance;
   float2 directionalUv;
   Check(Game.Player.PlayerSunShadowUtility.DirectionalSilhouetteUv(receiver,directional,s,out directionalUv),"Directional shadow clipped near feet");
   Check(math.distance(directionalUv,new float2((hit.x/s+32)/64,(hit.y/s+25)/128))<.0001,"Directional shadow inverse hit mismatch");
   Check(!Game.Player.PlayerSunShadowUtility.DirectionalSilhouetteUv(receiver,-directional,s,out directionalUv),"Directional caster behind ray accepted");
  }
  var feet=new float3(-17.3f,64,35.1f);float scale=2f/98;
  foreach(var source in new[]{new float3(.2f,1.5f,1),new float3(-.3f,2,-2)}){
   var caster=new float3(.1f,.8f,0);
   var receiver=source+(caster-source)*(source.y/(source.y-caster.y));
   float2 localUv;
   Check(Game.Player.PlayerSunShadowUtility.LocalSilhouetteUv(receiver,source,scale,out localUv),"Point light fails to project caster between light and floor");
   var expected=new float2((caster.x/scale+32)/64,(caster.y/scale+25)/128);
   Check(math.distance(localUv,expected)<.0001,"Point light inverse magnification incorrect");
  }
  float2 rejected;
  Check(!Game.Player.PlayerSunShadowUtility.LocalSilhouetteUv(new float3(0,0,.5f),new float3(0,2,1),scale,out rejected),"Caster behind receiver accepted");
  Check(!Game.Player.PlayerSunShadowUtility.LocalSilhouetteUv(new float3(0,0,2),new float3(0,2,1),scale,out rejected),"Caster beyond light accepted");
  Check(!Game.Player.PlayerSunShadowUtility.LocalSilhouetteUv(new float3(0,0,0),new float3(0,2,0),scale,out rejected),"Parallel/coincident plane division unstable");
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))){
   var right=ViewDirectionUtility.Right(view);var toward=ViewDirectionUtility.Forward(view);
   foreach(var sun in new[]{new float3(.3f,.8f,.4f),new float3(-.8f,.3f,-.2f),new float3(0,1,0)}){
    var slope=Game.Player.PlayerSunShadowUtility.Slopes(sun);
    Check(math.all(math.isfinite(slope)) && math.abs(slope.y)>=.15f,"Unstable edge-on projection");
    foreach(float h in new[]{.1f,1f,2f})foreach(float receiverDelta in new[]{-.5f,0f,.5f}){
     float sx=.2f;
     var ground=new float2(sx-slope.x*(h-receiverDelta),-slope.y*(h-receiverDelta));
     var hit=feet+new float3(right.x*ground.x+toward.x*ground.y,receiverDelta,right.y*ground.x+toward.y*ground.y);
     var relative=WorldPositionProjectionUtility.Project(hit,view)-WorldPositionProjectionUtility.Project(feet,view);
     var reconstructed=new float2(relative.x,-2*(relative.y-receiverDelta));
     Check(math.distance(reconstructed,ground)<.0001,"Ground receiver differs across view/chunk boundary");
     var uv=Game.Player.PlayerSunShadowUtility.SilhouetteUv(reconstructed,receiverDelta,slope,scale);
     Check(math.distance(uv,new float2((sx/scale+32)/64,(h/scale+25)/128))<.0001,"Shadow does not land at expected ground point");
    }
   }
  }
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition ($tests+$production)
[PlayerSunShadowChecks]::Run()
$terrain=Get-Content (Join-Path $root 'Shaders/ChunkProcedural.shader') -Raw
$terrain = $terrain.Replace('#include "DetailedLocalLighting.hlsl"', (Get-Content (Join-Path $root 'Shaders/DetailedLocalLighting.hlsl') -Raw))
if($terrain -notmatch 'input.FaceIndex == 0u && input.IsCutSurface == 0u' -or $terrain -notmatch 'directLight \*= 1 - PlayerSunOcclusion'){
 throw 'Shadow must affect only direct sun on real Top receivers'
}
'PASS mask HLSL for both UV conventions; actual inverse silhouette/ground projection in four views, half-step heights and negative chunk coordinates; top-only/direct-sun contracts. Unity visual integration still pending.'
if ($terrain -match 'PlayerLocalShadowRatio' -or
    $terrain -notmatch 'sum \+= _PlayerLocalShadowColors\[i\]\.rgb \* attenuation \* visibility \* response \* \(1 - occlusion\)' -or
    $terrain -notmatch 'localLight = PlayerLocalLighting' -or
    $terrain -notmatch 'StructuredBuffer<uint> _PlayerLightOccupancy' -or
    $terrain -notmatch 'float ShadowReceiverHeight : TEXCOORD13') {
    throw 'Local light must add source contributions after visibility/occlusion and interpolate wall receiver height'
}
$grid = Get-Content (Join-Path $root 'Scripts/Player/Visual/PlayerLocalLightGrid.cs') -Raw
if ($grid -match 'GetBuffer<PlayerNormalPointLight>' -or $grid -notmatch 'GetChangeVersion' -or $grid -notmatch 'if \(count == 8\) return false') {
    throw 'Local lighting requires independent source gathering, geometry invalidation and complete-source fallback'
}
'PASS source-separated accumulation, wall height, occupancy and overflow-fallback source contracts.'
$shadowSystem = Get-Content (Join-Path $root 'Scripts/Player/Visual/PlayerSunShadowRenderSystem.cs') -Raw
if (($grid + $shadowSystem) -match 'SetGlobalInt\("_PlayerLocalShadowCount"' -or
    $grid -notmatch 'SetGlobalInteger\("_PlayerLocalShadowCount", count\)' -or
    ([regex]::Matches($shadowSystem, 'SetGlobalInteger\("_PlayerLocalShadowCount", 0\)')).Count -ne 4) {
    throw 'The HLSL int light count must use integer uploads, including all lifecycle resets'
}
'PASS integer local light count uploads and lifecycle resets.'
if ($terrain -notmatch '#define PLAYER_LOCAL_LIGHT_REPLACEMENT 0' -or
    $terrain -notmatch '(?s)#if PLAYER_LOCAL_LIGHT_REPLACEMENT\s+if \(input.IsCutSurface == 0u\).*?localLight = PlayerLocalLighting.*?#endif' -or
    $shadowSystem -notmatch 'LocalLightingReplacementEnabled = false' -or
    $shadowSystem -notmatch 'bool localActive = LocalLightingReplacementEnabled &&') {
    throw 'Temporary rollback must gate shader replacement and runtime local-grid preparation together'
}
'PASS temporary local-light rollback gates; voxel lighting retained independently of local-shadow inspector state.'
$single = [regex]::Match($terrain, '(?s)float3 SingleLocalLightDiagnostic\(.*?\n            \}').Value
if (!$single -or $single -notmatch 'if \(_SingleLocalLightTestEnabled < .5\) return legacy' -or
    $single -match 'LocalWorldVisibility\(|_PlayerLocalShadowCount|SAMPLE_TEXTURE' -or
    $grid -notmatch 'entity == diagnosticSource \? -1 : distance' -or
    $shadowSystem -notmatch 'if \(localLightingEnabled\)') {
    throw 'Single-source diagnostic must be opt-in, preserve legacy when disabled and bypass occlusion/counter paths'
}
'PASS isolated opt-in single-source diagnostic with stable emitter selection.'
if ($single -notmatch 'sum \+= _DiagnosticLocalLightColors\[i\]\.rgb \* attenuation' -or
    $single -notmatch 'if \(source.w <= 0\) continue' -or
    $grid -notmatch 'System.Array.Clear\(diagnosticPositions' -or
    $grid -notmatch 'System.Array.Clear\(diagnosticColors' -or
    $grid -notmatch 'else if \(count == 0\) return' -or
    $shadowSystem -notmatch 'PrepareSingleLocalLightTest\(light.Feet, SystemAPI.GetSingleton<ViewDirectionComponent>\(\).Value, true, true\)') {
    throw 'Multi-source diagnostic must add contributions, clear unused slots and preserve opt-in switching'
}
'PASS additive multi-source diagnostic and stale-slot clearing contracts.'
if ($single -notmatch 'if \(_DiagnosticLocalShadowStrength > 0 && attenuation > 0\)' -or
    $single -notmatch 'attenuation \* transmission' -or
    $single -notmatch 'casterSource \+= casterReceiver - float3\(ground.x, delta, ground.y\)' -or
    $shadowSystem -notmatch '!sunActive && !localActive && !diagnosticShadows' -or
    ([regex]::Matches($shadowSystem, 'SetGlobalFloat\("_DiagnosticLocalShadowStrength", 0f\)')).Count -ne 4) {
    throw 'Diagnostic shadows must be opt-in per source, preserve offset alignment and reset safely, including at night'
}
'PASS optional per-source diagnostic silhouette shadows, common offset and lifecycle reset contracts.'
if ($single -notmatch 'if \(_DiagnosticLocalBlockOcclusion > .5\)' -or
    $single -notmatch '(?s)if \(_DiagnosticPreciseEdges > .5 &&.*?_DiagnosticBlockEdges.*?transmission = WorldBlockVisibility' -or
    $single -notmatch 'transmission = _DiagnosticBlockVisibility\[' -or
    $grid -notmatch '(?s)if \(blockOcclusion\).*?PrepareLocalBlockGrid\(feet\)' -or
    ([regex]::Matches($shadowSystem, 'SetGlobalFloat\("_DiagnosticLocalBlockOcclusion", 0f\)')).Count -ne 4) {
    throw 'Diagnostic block visibility must be opt-in, world-space, surface-biased and reset safely'
}
'PASS optional direct world-space block visibility and lifecycle gating.'
$compute = Get-Content (Join-Path $root 'Resources/PlayerLocalVisibility.compute') -Raw
[ItemBatchShaderCompile]::Compile(($compute -replace '(?m)^#pragma[^\r\n]*',''),'BuildVisibility','cs_5_0')
[ItemBatchShaderCompile]::Compile(($compute -replace '(?m)^#pragma[^\r\n]*',''),'BuildEdges','cs_5_0')
if ($grid -notmatch 'cachedVisibilitySources\[i\].Equals\(source\) && cachedVisibilityVersions\[i\] == occupancyVersion' -or
    $compute -notmatch '_VisibilitySlot \* 16000' -or $single -notmatch 'i \* 16000' -or
    $compute -notmatch 'uint3\(20,40,20\)' -or $single -notmatch 'int3\(20,40,20\)') {
    throw 'Visibility cache indexing/dimensions and source/geometry invalidation must agree'
}
'PASS cached visibility/edge compute HLSL, shared indexing, dirty-source invalidation and opt-in edge-only raymarch.'
if ($compute -notmatch 'if \(neighbour != center\) edge = 1' -or
    $grid -notmatch '(?s)Dispatch\(kernel, 5, 10, 5\).*?Dispatch\(edgeKernel, 5, 10, 5\)' -or
    $shadowSystem -notmatch '"_DiagnosticPreciseEdges", localLightingEnabled \? 1f : 0f') {
    throw 'Precise edges must use cached neighbour classification and a separate compute dispatch'
}
$sky = Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/SkyLightSystem.cs') -Raw
$modification = Get-Content (Join-Path $root 'Scripts/World/Blocks/Systems/BlockModificationSystem.cs') -Raw
$generation = Get-Content (Join-Path $root 'Scripts/World/Chunks/ChunkGenerationSystem.cs') -Raw
$streaming = Get-Content (Join-Path $root 'Scripts/World/Chunks/ChunkStreamingSystem.cs') -Raw
if ($sky -notmatch 'GetBufferLookup<BlockData>\(true\)' -or
    $sky -notmatch 'GetBufferLookup<SplitCellData>\(true\)' -or
    $sky -match 'SystemAPI.Query<\s*DynamicBuffer<BlockData>' -or
    $grid -notmatch 'versioned \? EntityManager.GetComponentData<ChunkGeometryRevision>\(entity\).Value' -or
    $streaming -notmatch 'typeof\(ChunkGeometryRevision\)' -or
    $generation -notmatch 'geometryRevision.Value\+\+' -or
    $modification -notmatch '(?s)worldChanged = true;\s*var geometryRevision.*?geometryRevision.Value\+\+.*?var surface' -or
    $grid.IndexOf('else if (count == 0) return;') -gt $grid.IndexOf('if (blockOcclusion)')) {
    throw 'Visibility cache must use actual geometry revisions, read-only skylight inputs and skip all grid work without sources'
}
'PASS actual geometry revision invalidation, block edit/generation coverage, read-only skylight and no-source early exit.'
$debugControl = Get-Content (Join-Path $root 'Scripts/Player/Lighting/LocalLightingDebug.cs') -Raw
$rendererSource = Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.cs') -Raw
if (([regex]::Matches($debugControl, '\[SerializeField\]')).Count -ne 1 -or
    $debugControl -notmatch 'isActiveAndEnabled && enableLocalLighting' -or
    $rendererSource -match 'testSingleLocalLight|testMultipleLocalLights|testLocalLightPreciseEdges' -or
    $shadowSystem -notmatch 'TryGetComponent<LocalLightingDebug>' -or
    $debugControl -notmatch 'void OnDisable\(') {
    throw 'Validated lighting bundle must have one independent switch with safe disable and no obsolete renderer toggles'
}
'PASS separate single-checkbox local-light debug component and legacy fallback.'
if ($single -notmatch 'LocalPointNormalResponse\(worldNormal, referenceNormal, source.xyz - receiver\)' -or
    $single -notmatch 'attenuation \* transmission \* normalResponse' -or
    $terrain -notmatch '_SideFaceNormal, worldNormal, referenceNormal, localLight\)' -or
    $terrain -notmatch 'max\(dot\(toLight, toLight\), .000001\)') {
    throw 'Local lights must apply finite per-source world-space normal response before summation, independently of geometry bias'
}
'PASS per-source block normal response, geometry-bias separation and coincident-light safety.'
$normalFunction = [regex]::Match($terrain, '(?s)float LocalPointNormalResponse\(.*?\n            \}').Value
$normalFunction = $normalFunction -replace '^float LocalPointNormalResponse', 'static float LocalPointNormalResponse'
$normalFunction = $normalFunction -replace '(?<![\w.])(\d*\.\d+|\d+e\d+)(?![\w.])', '${1}f'
$normalTests = @'
using System;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public static class LocalPointNormalChecks {
 static float _LocalNormalStrength=1;
'@ + $normalFunction + @'
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static string Run(){
  var reference=new float3(0,0,1);var normal=new float3(1,0,0);var ray=new float3(1,0,0);
  Check(LocalPointNormalResponse(reference,reference,ray)==1,"Reference surface brightness changed");
  Check(LocalPointNormalResponse(normal,reference,ray)>1,"Normal facing light did not brighten");
  Check(LocalPointNormalResponse(normal,reference,-ray)<1,"Opposite light did not change response");
  Check(LocalPointNormalResponse(normal,reference,new float3(0))==1,"Coincident source not neutral");
  Check(math.abs(LocalPointNormalResponse(normal,reference,ray)-LocalPointNormalResponse(normal,reference,ray*7))<.0001f,"Direction response incorrectly depends on distance");
  _LocalNormalStrength=0;
  Check(LocalPointNormalResponse(normal,reference,ray)==1,"Zero strength must preserve previous lighting");
  return "PASS actual HLSL normal arithmetic: flat baseline, opposing sources, distance independence, coincident source and zero strength.";
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition $normalTests
[LocalPointNormalChecks]::Run()
