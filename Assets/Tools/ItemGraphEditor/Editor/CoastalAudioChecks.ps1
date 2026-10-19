$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$references = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
$utility = Get-Content (Join-Path $root 'Scripts/World/Effects/CoastalAudioUtility.cs') -Raw
$tests = @'
public static class CoastalAudioChecks {
 static void Check(bool ok,string message){if(!ok)throw new System.Exception(message);}
 public static void Run(){
  var cell=new Unity.Mathematics.int2(-17,-33);
  var center=(Unity.Mathematics.float2)cell+.5f;
  var directions=new[]{new Unity.Mathematics.float2(0,-1),new Unity.Mathematics.float2(1,0),
   new Unity.Mathematics.float2(0,1),new Unity.Mathematics.float2(-1,0)};
  for(int i=0;i<4;i++){
   var edge=center+directions[i]*.5f;
   for(int d=0;d<=40;d++){
    float water=Game.World.Effects.CoastalAudioUtility.Distance(edge-directions[i]*d,cell,(byte)(1<<i));
    float land=Game.World.Effects.CoastalAudioUtility.Distance(edge+directions[i]*d,cell,(byte)(1<<i));
    Check(Unity.Mathematics.math.abs(water-d)<.0001f&&Unity.Mathematics.math.abs(land-d)<.0001f,"Asymmetric shore falloff");
   }
  }
  var corners=new[]{new Unity.Mathematics.float2(1,0),new Unity.Mathematics.float2(1,1),
   new Unity.Mathematics.float2(0,1),new Unity.Mathematics.float2(0,0)};
  for(int i=0;i<4;i++)Check(Game.World.Effects.CoastalAudioUtility.Distance((Unity.Mathematics.float2)cell+corners[i],cell,(byte)(16<<i))==0,"Corner mapping mismatch");
  Check(float.IsPositiveInfinity(Game.World.Effects.CoastalAudioUtility.Distance(center,cell,0)),"Missing shore creates sound");
  float previous=1;
  for(int i=0;i<=400;i++){
   float d=i*.1f,gain=Game.World.Effects.CoastalAudioUtility.Gain(d,3,32);
   Check(gain>=0&&gain<=previous+.00001f,"Non-monotonic gain");
   if(d<=3)Check(gain==1,"Full-volume band not full");
   if(d>=32)Check(gain==0,"Audio audible outside radius");
   previous=gain;
  }
 }
}
'@
Add-Type -TypeDefinition ($utility + $tests) -ReferencedAssemblies $references
[CoastalAudioChecks]::Run()
$asset = Get-Content (Join-Path $root 'Resources/CoastalAudioSettings.asset') -Raw
$musicGuid = [regex]::Match((Get-Content (Join-Path $root 'Audio/sea-shore (2).mp3.meta') -Raw), 'guid: (\w+)').Groups[1].Value
$seaGuid = [regex]::Match((Get-Content (Join-Path $root 'Audio/oceanwaves.wav.meta') -Raw), 'guid: (\w+)').Groups[1].Value
if (!$asset.Contains("rockyShoreMusic: {fileID: 8300000, guid: $musicGuid") -or
    !$asset.Contains("seaAmbience: {fileID: 8300000, guid: $seaGuid")) { throw 'Audio references do not match intro assets.' }
'Coastal audio PASS: production distance/gain, both shore sides, four edges/corners, negative coordinates, monotonic fade, clip references. Runtime playback not tested.'
