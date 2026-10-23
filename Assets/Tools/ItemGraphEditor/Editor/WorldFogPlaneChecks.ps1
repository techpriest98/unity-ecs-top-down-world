$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WorldItemBatchShaderChecks.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$core = @'
#define TEXTURE2D(n) Texture2D<float4> n
#define SAMPLER(n) SamplerState n
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
#define SAMPLE_TEXTURE2D_LOD(t,s,u,l) t.SampleLevel(s,u,l)
static const float4x4 UNITY_MATRIX_V = float4x4(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1);
float4 TransformWorldToHClip(float3 p) {return float4(p,1);}
float4 TransformObjectToHClip(float3 p) {return float4(p,1);}
'@
foreach ($name in 'WorldFog','WorldFogMask','ChunkProcedural') {
    $shader = Get-Content (Join-Path $root ('Shaders/'+$name+'.shader')) -Raw
    $program = [regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
    $program = $program.Replace('#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"', $core)
    $program = $program.Replace('#include "ProjectedCellPacking.hlsl"', (Get-Content (Join-Path $root 'Shaders/ProjectedCellPacking.hlsl') -Raw))
    $program = $program.Replace('#include "ChunkBorders.hlsl"', (Get-Content (Join-Path $root 'Shaders/ChunkBorders.hlsl') -Raw))
    $program = $program.Replace('#include "ShoreFoam.hlsl"', (Get-Content (Join-Path $root 'Shaders/ShoreFoam.hlsl') -Raw))
    $program = $program.Replace('#include "GpuSunTrace.hlsl"', (Get-Content (Join-Path $root 'Shaders/GpuSunTrace.hlsl') -Raw))
    $program = $program.Replace('#include "DetailedLocalLighting.hlsl"', (Get-Content (Join-Path $root 'Shaders/DetailedLocalLighting.hlsl') -Raw))
    [ItemBatchShaderCompile]::Compile($program,'Vert','vs_5_0')
    [ItemBatchShaderCompile]::Compile($program,'Frag','ps_5_0')
    if ($name -eq 'WorldFogMask') {
        $alphaProgram = "#define _FOG_SKY_ALPHA 1`n" + $program
        [ItemBatchShaderCompile]::Compile($alphaProgram,'Vert','vs_5_0')
        [ItemBatchShaderCompile]::Compile($alphaProgram,'Frag','ps_5_0')
    }
    if ($name -eq 'WorldFog') {
        [ItemBatchShaderCompile]::Compile(("#define UNITY_UV_STARTS_AT_TOP 1`n" + $program),'Frag','ps_5_0')
    }
    if ($name -eq 'ChunkProcedural') {
        foreach ($variant in '_PLAYER_CLIPPING','_DEBUG_NORMALS') {
            $variantProgram = "#define $variant 1`n" + $program
            [ItemBatchShaderCompile]::Compile($variantProgram,'Vert','vs_5_0')
            [ItemBatchShaderCompile]::Compile($variantProgram,'Frag','ps_5_0')
        }
    }
}
# Verify world-space noise unprojection using real projection helpers and math.
Add-Type -Path 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
$production = ''
foreach ($name in 'ViewDirection','ViewDirectionUtility','WorldPositionProjectionUtility','ChunkRenderPositionUtility') {
    $production += (Get-Content (Join-Path $root ('Scripts/World/Rendering/'+$name+'.cs')) -Raw) -replace '(?m)^using [^;]+;\r?\n',''
}
$source = @'
using System;
using Unity.Mathematics;
using Game.World.Chunks;
using Game.World.Rendering;
namespace Game.World.Chunks { public static class ChunkSettings { public const int SizeX=16,SizeY=256,SizeZ=16; } }
public static class FogPlaneChecks {
 public static string Run() {
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection)))
  foreach(float height in new[]{62.5f,66f,75f}) {
   float2 sea=WorldPositionProjectionUtility.Project(new float3(0,64,0),view)+new float2(0,.25f);
   var toward=ViewDirectionUtility.Forward(view);float2 right=new float2(toward.y,-toward.x),away=-(float2)toward;
   for(int x=-33;x<=33;x+=7)for(int z=-33;z<=33;z+=9) {
    float3 p=new float3(x+.25f,height,z+.75f);
    float2 projected=WorldPositionProjectionUtility.Project(p,view)+new float2(0,.25f);
    float2 plane=projected-sea-new float2(0,height-64);
    float2 restored=right*plane.x+away*plane.y*2;
    if(math.distance(restored,p.xz)>.001)throw new Exception("Fog moves with view "+view+" "+restored+" vs "+p.xz);
   }
  }
  return "PASS world-space noise coordinates across four views/heights/negative chunk boundaries. Stencil overlap and Unity FPS need manual validation.";
 }
}
'@
$math = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -TypeDefinition ($source+$production) -ReferencedAssemblies (@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$math)
[FogPlaneChecks]::Run()


$fogSource = Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs') -Raw
if (!$fogSource.Contains('new Vector4(hasFogPlayer ? 1f : 0f, 0,') -or
    $fogSource.Contains('FogPlayerClearance') -or $fogSource.Contains('fogPlayerFeetHeight')) {
    throw 'Player clearance must depend on player presence, not height'
}
'PASS player clearance always active when player is present, including below/inside fog; screen gradient shader compiled.'

$itemMethod = [regex]::Match($fogSource, '(?s)public bool IsAboveFogPlane\(float itemBaseHeight\) =>.*?;').Value
if (!$itemMethod) { throw 'Item fog-height predicate missing' }
$itemHarness = @'
using System;
public class ItemFogHeightChecks {
 bool enabled=true, hasFogSeaLevel=true;
 float fogSeaLevel=64, fogHeightAboveSea=2, automaticFog=1, fogOpacity=.9f;
 int fogMode=1;
'@
$itemTests = @'
 public static string Run(){
  var f=new ItemFogHeightChecks();
  if(f.IsAboveFogPlane(65.9f)||!f.IsAboveFogPlane(66)||!f.IsAboveFogPlane(70))throw new Exception("Wrong item height threshold");
  f.fogMode=2;if(f.IsAboveFogPlane(70))throw new Exception("Off fog splits item batches");
  f.fogMode=0;f.automaticFog=0;if(f.IsAboveFogPlane(70))throw new Exception("Clear weather splits item batches");
  f.automaticFog=1;if(!f.IsAboveFogPlane(70))throw new Exception("Automatic fog ignores high items");
  f.hasFogSeaLevel=false;if(f.IsAboveFogPlane(70))throw new Exception("Missing plane protects items");
  return "PASS extracted item-height predicate: below/at/above plane, Full/Off/Auto and missing sea level.";
 }
}
'@
Add-Type -TypeDefinition ($itemHarness+$itemMethod+$itemTests)
[ItemFogHeightChecks]::Run()
