$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs') -Raw
$start=$source.IndexOf('        public enum FogDebugState')
$end=$source.IndexOf('        public void SetFogSeaLevel', $start)
if($start -lt 0 -or $end -lt 0){throw 'Fog diagnostic properties not found'}
$production=$source.Substring($start,$end-$start)
$adapter=@'
using System;
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Clamp01(float v)=>Math.Clamp(v,0,1);
 public static float InverseLerp(float a,float b,float v)=>a==b?0:Clamp01((v-a)/(b-a));
 public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
}
public struct Wind { public bool Initialized;public float Strength; }
public struct Pair { public float x,y; }
public class FogDebugStateChecks {
 bool enabled=true,hasFogSeaLevel=true;
 int fogChunkCount=1,fogMode;
 float automaticFog,fogOpacity=.9f;
 Pair fogWindThresholds=new Pair{x=.2f,y=.4f};Wind worldWind;
'@
$tests=@'
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 public static string Run(){
  var f=new FogDebugStateChecks();
  Check(f.DebugFogState==FogDebugState.AutoClear&&f.DebugFogDensity==0,"Initial clear state wrong");
  f.worldWind=new Wind{Initialized=true,Strength=.15f};
  Check(f.DebugFogState==FogDebugState.AutoBuilding,"Calm state not gathering");
  f.worldWind.Strength=.8f;f.automaticFog=.8f;
  Check(f.DebugFogState==FogDebugState.AutoClearing,"Windy state not clearing");
  f.worldWind.Strength=.3f;f.automaticFog=.5f;
  Check(f.DebugFogState==FogDebugState.AutoSteady&&Math.Abs(f.DebugFogDensity-.45f)<.00001,"Stable density wrong");
  f.fogMode=1;Check(f.DebugFogState==FogDebugState.Full&&Math.Abs(f.DebugFogDensity-.9f)<.00001,"Full mode wrong");
  f.fogMode=2;Check(f.DebugFogState==FogDebugState.Off&&f.DebugFogDensity==0,"Off mode wrong");
  f.fogChunkCount=0;Check(f.DebugFogState==FogDebugState.Unavailable&&f.DebugFogDensity==0,"Unavailable fog displayed as active");
  f.fogChunkCount=1;f.enabled=false;Check(f.DebugFogState==FogDebugState.Unavailable,"Disabled fog displayed as active");
  return "PASS actual fog diagnostic properties: Auto clear/building/clearing/steady, Full/Off, density and unavailable renderer.";
 }
}
'@
Add-Type -TypeDefinition ($adapter+$production+$tests)
[FogDebugStateChecks]::Run()
