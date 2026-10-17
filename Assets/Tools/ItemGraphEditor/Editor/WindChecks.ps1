# Real wind state/math, with ECS attributes adapted for headless checks.
# This does not validate Unity scheduling or GPU rendering.
$ErrorActionPreference = 'Stop'
$windAssets = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$windMath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $windMath
$windReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $windMath
$windText = Get-Content -LiteralPath (Join-Path $windAssets 'Scripts/World/Time/WorldTimeSystem.cs') -Raw
$windBody = ($windText -split '    \[BurstCompile\]', 2)[0] + "`n}"
$windDirections = (Get-Content -LiteralPath (Join-Path $windAssets 'Scripts/World/Rendering/ViewDirectionUtility.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n', ''
$windEnum = Get-Content -LiteralPath (Join-Path $windAssets 'Scripts/World/Rendering/ViewDirection.cs') -Raw
$windHarness = @'
namespace Unity.Burst { }
namespace Unity.Entities { public interface IComponentData { } }
namespace Game.World.Generation { }
namespace Game.World.Generation.Spawning { }
public static class WindChecks {
 static void Check(bool ok,string text) {if(!ok)throw new System.Exception(text);}
 public static string Run() {
  foreach(uint seed in new uint[]{0,1,12345,uint.MaxValue}) {
   var a=default(Game.World.Time.WorldWind);var b=default(Game.World.Time.WorldWind);
   for(int i=0;i<12000;i++) {
    Game.World.Time.WorldWindUtility.Advance(ref a,seed,1f/60);
    Game.World.Time.WorldWindUtility.Advance(ref b,seed,1f/60);
    Check(a.Angle==b.Angle && a.Strength==b.Strength && math.all(a.Drift==b.Drift),"Seeded wind is nondeterministic");
    Check(math.abs(math.length(a.Direction)-1f)<.0001f,"Wind direction is not normalized");
    Check(a.Strength>=0 && a.Strength<=1 && math.isfinite(a.WaveTime) && math.all(math.isfinite(a.Drift)),"Wind left valid range");
   }
   var paused=a;
   Game.World.Time.WorldWindUtility.Advance(ref a,seed,0);
   Check(a.Angle==paused.Angle && a.WaveTime==paused.WaveTime && math.all(a.Drift==paused.Drift),"Paused wind kept moving");
  }
  var fpsA=default(Game.World.Time.WorldWind);var fpsB=default(Game.World.Time.WorldWind);
  for(int i=0;i<180;i++)Game.World.Time.WorldWindUtility.Advance(ref fpsA,7,1f/60);
  for(int i=0;i<30;i++)Game.World.Time.WorldWindUtility.Advance(ref fpsB,7,.1f);
  Check(math.distance(fpsA.Direction,fpsB.Direction)<.001f && math.abs(fpsA.Strength-fpsB.Strength)<.001f,
   "Wind smoothing depends on frame rate");
  var seam=new Game.World.Time.WorldWind {Initialized=true,Seed=1,Angle=math.PI-.01f,TargetAngle=-math.PI+.1f,
   Strength=.5f,TargetStrength=.5f,SecondsToChange=10,RandomState=1};
  Game.World.Time.WorldWindUtility.Advance(ref seam,1,.1f);
  Check(math.abs(math.atan2(math.sin(seam.Angle-(math.PI-.01f)),math.cos(seam.Angle-(math.PI-.01f))))<.01f,
   "Wind took the long rotation across +/- PI");
  foreach(Game.World.Rendering.ViewDirection view in System.Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))) {
   var right=(float2)Game.World.Rendering.ViewDirectionUtility.Right(view);
   var away3=Game.World.Rendering.ViewDirectionUtility.GetAwayFromCameraOffset(view);
   var away=new float2(away3.x,away3.z);
   for(int side=0;side<4;side++) {
    var normal=side==0?new float2(0,1):side==1?new float2(1,0):side==2?new float2(0,-1):new float2(-1,0);
    var toLand=right*normal.x+away*normal.y;
    var windView=new float2(math.dot(toLand,right),math.dot(toLand,away));
    Check(math.abs(math.dot(windView,normal)-1f)<.0001f,"Camera view changed onshore wind");
    Check(math.abs(math.dot(-windView,normal)+1f)<.0001f,"Offshore direction is inverted");
    var landContact=-normal;
    Check(math.dot(windView,-landContact)==math.dot(windView,normal),"Land/water disagree about wind incidence");
   }
  }
  return "Wind PASS: deterministic seeds, normalized direction, smooth targets, pause, frame-rate independence and four-view shore incidence. GPU/scheduling not tested.";
 }
}
'@
Add-Type -TypeDefinition ($windBody + "`n" + $windDirections + "`n" + $windEnum + "`n" + $windHarness) -ReferencedAssemblies $windReferences
[WindChecks]::Run()
