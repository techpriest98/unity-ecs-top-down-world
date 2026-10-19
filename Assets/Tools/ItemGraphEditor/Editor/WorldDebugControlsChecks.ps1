$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function ReadSource([string]$path) { (Get-Content (Join-Path $root $path) -Raw) -replace '(?m)^using [^;]+;\r?\n','' }
$wind=ReadSource 'Scripts/World/Time/WorldTimeSystem.cs'
$wind=($wind -split '    \[BurstCompile\]',2)[0]+"`n}"
$source="using Unity.Mathematics; using Unity.Entities;`nnamespace Unity.Entities { public interface IComponentData {} }`n"+
 (ReadSource 'Scripts/World/Time/WorldTime.cs')+$wind+(ReadSource 'Scripts/World/Time/WorldTimeDebugUtility.cs')
$tests=@'
public static class WorldDebugControlsChecks {
 static void Check(bool ok,string text){if(!ok)throw new System.Exception(text);}
 public static string Run(){
  var normal=new Game.World.Time.WorldTime{Hour=12};
  var fast=normal;
  Game.World.Time.WorldTimeDebugUtility.Advance(ref normal,90,1);
  Game.World.Time.WorldTimeDebugUtility.Advance(ref fast,90,8);
  Check(normal.Hour==13&&fast.Hour==20,"Calendar multiplier incorrect");
  Check(normal.SimulationSeconds==90&&fast.SimulationSeconds==720&&fast.DeltaSeconds==720,"World clock/delta not accelerated together");
  var slow=new Game.World.Time.WorldTime{Hour=12};
  Game.World.Time.WorldTimeDebugUtility.Advance(ref slow,90,.25f);
  Check(slow.Hour==12&&Game.World.Time.WorldTimeDebugUtility.Minute(slow)==15,"Minute display incorrect");
  var midnight=new Game.World.Time.WorldTime{Hour=23};
  Game.World.Time.WorldTimeDebugUtility.Advance(ref midnight,180,1);
  Check(midnight.Hour==1&&midnight.UpdateTimer==0,"Midnight wrap incorrect");
  var paused=midnight;
  Game.World.Time.WorldTimeDebugUtility.Advance(ref midnight,0,64);
  Game.World.Time.WorldTimeDebugUtility.Advance(ref midnight,float.NaN,64);
  Check(midnight.Hour==paused.Hour&&midnight.SimulationSeconds==paused.SimulationSeconds&&midnight.DeltaSeconds==0,"Pause/invalid delta advances clock");
  var fine=new Game.World.Time.WorldTime{Hour=12};
  for(int i=0;i<90;i++)Game.World.Time.WorldTimeDebugUtility.Advance(ref fine,1,8);
  Check(fine.Hour==fast.Hour&&fine.SimulationSeconds==fast.SimulationSeconds,"World-time rate depends on frame partition");
  float speed=1;
  for(int i=0;i<20;i++)speed=Game.World.Time.WorldTimeDebugUtility.ChangeSpeed(speed,true);
  Check(speed==64,"Speed upper bound missing");
  for(int i=0;i<20;i++)speed=Game.World.Time.WorldTimeDebugUtility.ChangeSpeed(speed,false);
  Check(speed==.25f,"Speed lower bound missing");
  var wind=default(Game.World.Time.WorldWind);
  Game.World.Time.WorldWindUtility.Advance(ref wind,7,0);
  wind.TargetStrength=.6f;wind.SecondsToChange=1000;
  var before=wind;
  Game.World.Time.WorldTimeDebugUtility.NudgeWind(ref wind,-2);
  Check(wind.Strength==0&&wind.TargetStrength==before.TargetStrength&&wind.RandomState==before.RandomState&&
   wind.SecondsToChange==before.SecondsToChange&&wind.Angle==before.Angle&&math.all(wind.Drift==before.Drift),"Wind nudge changed weather trajectory");
  Game.World.Time.WorldWindUtility.Advance(ref wind,7,60);
  Check(wind.Strength>.5f&&wind.Strength<.6f&&wind.TargetStrength==.6f,"Wind does not recover after weakening");
  Game.World.Time.WorldTimeDebugUtility.NudgeWind(ref wind,2);
  Check(wind.Strength==1,"Wind strength not clamped");
  Game.World.Time.WorldWindUtility.Advance(ref wind,7,60);
  Check(wind.Strength>.6f&&wind.Strength<.7f,"Wind does not recover after strengthening");
  Game.World.Time.WorldWindUtility.Advance(ref wind,7,1000);
  Check(wind.RandomState!=before.RandomState&&wind.TargetStrength>=.15f&&wind.TargetStrength<=1,"Normal weather stopped updating");
  return "PASS calendar/minutes/midnight, 0.25..64x limits, scaled world-aging clock and delta, pause, frame partition, temporary wind nudges and natural weather recovery.";
 }
}
'@
$math='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $math
Add-Type -TypeDefinition ($source+$tests) -ReferencedAssemblies (@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$math)
[WorldDebugControlsChecks]::Run()
