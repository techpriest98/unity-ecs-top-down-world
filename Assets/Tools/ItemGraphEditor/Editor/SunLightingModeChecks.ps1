$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
. (Join-Path $PSScriptRoot 'WorldItemBatchShaderChecks.ps1')
$trace=Get-Content (Join-Path $root 'Shaders/GpuSunTrace.hlsl') -Raw
$compute=(Get-Content (Join-Path $root 'Resources/GpuSunVisibility.compute') -Raw).Replace('#include "../Shaders/GpuSunTrace.hlsl"',$trace)
$compute=$compute -replace '(?m)^#pragma[^\r\n]*',''
foreach($kernel in 'BuildSun','BuildSunEdges'){[ItemBatchShaderCompile]::Compile($compute,$kernel,'cs_5_0')}
$body=[regex]::Match($trace,'(?s)float TraceGpuSun\(float3 origin\)\s*\{(.*?)\n\}').Groups[1].Value
if(!$body){throw 'Production directional trace missing'}
$body=$body.Replace('[loop]','') -replace '\b(float3|int3)\(', 'new $1('
$body=$body -replace '\b2u\b','2' -replace '\b0u\b','0'
$body=$body -replace '(?<![\w.])(\d*\.\d+|\d+e\d+)(?![\w.])','${1}f'
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$source=@'
using System;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public static class SunModeChecks {
 static uint[] _PlayerLightOccupancy=new uint[40*80*40];
 static float4 _PlayerLightGridOrigin=new float4(0,0,0,40);
 static float4 _GpuSunDirection=new float4(0,1,0,0);
 static float Trace(float3 origin) {
'@+$body+@'
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static string Run(){
  var p=new float3(10.5f,2.02f,10.5f);
  Check(Trace(p)==1,"Clear sky blocked");
  _PlayerLightOccupancy[(10*80+20)*40+10]=1;
  Check(Trace(p)==0,"Overhead blocker missed");
  _PlayerLightOccupancy[(10*80+20)*40+10]=2;
  Check(Trace(p)==-1,"Unknown geometry must retain base visibility");
  Array.Clear(_PlayerLightOccupancy,0,_PlayerLightOccupancy.Length);
  _GpuSunDirection=new float4(-1,0,0,0);
  Check(Trace(p)==-1,"Grid exit must not assert sun visibility");
  _PlayerLightOccupancy[(10*80+4)*40+8]=1;
  Check(Trace(p)==0,"Low sun blocker before grid exit missed");
  Array.Clear(_PlayerLightOccupancy,0,_PlayerLightOccupancy.Length);
  _GpuSunDirection=new float4(0,1,0,0);_PlayerLightGridOrigin=new float4(-16,-16,-16,40);
  Check(Trace(p-new float3(16,16,16))==1,"Negative grid origin changed sun trace");
  _PlayerLightOccupancy=new uint[72*144*72];_PlayerLightGridOrigin=new float4(0,0,0,72);
  _GpuSunDirection=new float4(1,0,0,0);
  _PlayerLightOccupancy[(40*144+4)*72+50]=1;
  Check(Trace(new float3(40.5f,2.02f,40.5f))==0,"Expanded sun geometry still uses old 40-cell bounds/stride");
  return "PASS actual GPU-sun DDA arithmetic: open sky, half-height blockers, unknown coverage, low-angle sun and negative origins.";
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[SunModeChecks]::Run()
$chunks=Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/ChunkLightingSystem.cs') -Raw
$state=Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/SunShadowStateSystem.cs') -Raw
$control=Get-Content (Join-Path $root 'Scripts/World/Lighting/SunLightingDebug.cs') -Raw
$shader=Get-Content (Join-Path $root 'Shaders/ChunkProcedural.shader') -Raw
$cache=Get-Content (Join-Path $root 'Scripts/Player/Visual/PlayerGpuSunLighting.cs') -Raw
if($chunks -notmatch 'shadowState.CpuShadowsDisabled \|\| IsSunVisible' -or
 $chunks -notmatch 'bool needsShadowUpdate = !cpuDisabled &&' -or
 $state -notmatch '(?s)if \(disableCpu\).*?IsTransitioning = false.*?pending.ValueRW = false.*?return;' -or
 $state -notmatch '(?s)if \(shadowState.CpuShadowsDisabled\).*?IsTransitioning = true.*?pending.ValueRW = true' -or
 $control -notmatch 'private bool cpuSunShadows = true' -or $control -notmatch 'private bool gpuSunLighting;' -or
 $shader -notmatch 'if \(_CpuSunShadowsDisabled > .5\) return 1' -or
 $shader -match 'receiver\+direction\*1000' -or
 $shader -notmatch 'float rayDistance = -receiver.z/direction.z' -or
 $cache -notmatch 'gpuSunGeometryVersion != occupancyVersion' -or $cache -notmatch 'gpuSunCachedDirection != direction'){
 throw 'CPU-off must skip sun tracing/queues, re-enable must rebuild, and GPU must use shared geometry/silhouette with independent controls'
}
'PASS independent CPU/GPU modes, actual CPU ray/queue bypass, re-enable invalidation, cached sun and shared silhouette contracts. Loading time/FPS need in-game measurements.'
if($cache -notmatch 'new ComputeBuffer\(16000' -or
 $compute -notmatch 'uint3\(20,40,20\)' -or
 $shader -notmatch 'if \(bound >= 8\) return legacy' -or
 $shader -notmatch 'smoothstep\(7,8,bound\)' -or
 $shader -notmatch 'length\(receiver.xz\)>=8' -or
 $shader -notmatch 'if \(bound >= 8\) return legacy') {
 throw 'GPU sun and local light regions must retain radius eight with the small sun cache'
}
'PASS restored radius-eight sun/cache bounds and unchanged local-light region.'
