$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader = Get-Content (Join-Path $root 'Resources/PlayerLocalVisibility.compute') -Raw
# Execute the production DDA arithmetic against deterministic occupancy fixtures.
$body = [regex]::Match($shader, '(?s)float Trace\(float3 origin, float3 target\)\s*\{(.*?)\n\}').Groups[1].Value
if (!$body) { throw 'World-space DDA body not found' }
$body = $body.Replace('[loop]', '') -replace '\b(float3|int3)\(', 'new $1('
$body = $body -replace 'uint index =', 'int index ='
$body = $body -replace '(?<![\w.])(\d*\.\d+|\d+e\d+)(?![\w.])', '${1}f'
$assembly = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $assembly
$source = @'
using System;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public static class LocalBlockRayChecks {
 static float TreeCellVisibility(int3 cell,int size,float3 origin,float3 target)=>1;
 static float4 _PlayerLightGridOrigin = new float4(0,0,0,40);
 static uint[] _PlayerLightOccupancy = new uint[40*80*40];
 static float Ray(float3 origin, float3 target) {
'@ + $body + @'
 }
 static void Check(bool condition, string message) { if(!condition) throw new Exception(message); }
 public static string Run() {
  // Flat ground below Y=1: horizontal/vertical/diagonal clear rays must not self-block.
  for(int z=0;z<40;z++)for(int y=0;y<2;y++)for(int x=0;x<40;x++)
   _PlayerLightOccupancy[(z*80+y)*40+x]=1;
  var start=new float3(2.5f,1.02f,2.5f);
  foreach(var end in new[]{new float3(7.5f,1.1f,2.5f),new float3(2.5f,5,2.5f),new float3(7.5f,2,7.5f),new float3(.5f,1.1f,.5f)})
   Check(Ray(start,end)==1,"Clear floor/parallel-axis ray blocked");
  // Lower half of a block at X=4, Y=1, Z=2.
  _PlayerLightOccupancy[(2*80+2)*40+4]=1;
  Check(Ray(new float3(2.5f,1.25f,2.5f),new float3(7.5f,1.25f,2.5f))==0,"Lower half failed to block");
  Check(Ray(new float3(2.5f,1.75f,2.5f),new float3(7.5f,1.75f,2.5f))==1,"Air above lower half blocked");
  _PlayerLightOccupancy[(2*80+3)*40+4]=1;
  Check(Ray(new float3(7.5f,1.75f,2.5f),new float3(2.5f,1.75f,2.5f))==0,"Reverse ray missed full wall");
  // Shift all world coordinates while leaving grid-relative geometry unchanged.
  _PlayerLightGridOrigin = new float4(-16,-16,-16,40);
  Check(Ray(new float3(-13.5f,-14.25f,-13.5f),new float3(-8.5f,-14.25f,-13.5f))==0,"Negative grid-origin wall missed");
  return "PASS actual production DDA arithmetic: clear floor, zero-axis rays, split half, full wall, reverse rays and negative world/grid coordinates.";
 }
}
'@
$refs = @($assembly, (Join-Path $PSHOME 'ref/netstandard.dll'), (Join-Path $PSHOME 'ref/System.Runtime.dll'))
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[LocalBlockRayChecks]::Run()
