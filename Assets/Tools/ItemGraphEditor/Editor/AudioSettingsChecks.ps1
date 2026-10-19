$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
# Test production preference logic without touching the developer's real PlayerPrefs.
$stubs = @'
using System;
namespace UnityEngine {
 public enum RuntimeInitializeLoadType { SubsystemRegistration }
 public sealed class RuntimeInitializeOnLoadMethodAttribute:Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){} }
 public static class Mathf { public static float Clamp01(float v)=>Math.Min(1,Math.Max(0,v)); }
 public static class PlayerPrefs {
  public static readonly System.Collections.Generic.Dictionary<string,float> Values=new();
  public static int Saves;
  public static float GetFloat(string key,float fallback)=>Values.TryGetValue(key,out var v)?v:fallback;
  public static void SetFloat(string key,float v)=>Values[key]=v;
  public static void Save(){Saves++;}
 }
}
'@
$production = Get-Content (Join-Path $root 'Scripts/UI/AudioVolumeSettings.cs') -Raw
$production = $production -replace '(?m)^using [^;]+;\r?\n',''
$production = $production -replace 'namespace Game.UI\s*\{', 'namespace Game.UI { using UnityEngine; using System;'
$tests = @'
public static class AudioPreferenceChecks {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(){
  Check(Game.UI.AudioVolumeSettings.Music==1&&Game.UI.AudioVolumeSettings.Ambience==1,"Default volume changed");
  int events=0;Game.UI.AudioVolumeSettings.Changed+=()=>events++;
  Game.UI.AudioVolumeSettings.Music=.25f;
  Game.UI.AudioVolumeSettings.Ambience=.7f;
  Check(events==2,"Live changes not published");
  Game.UI.AudioVolumeSettings.Music=.25f;Check(events==2,"Duplicate change notification");
  Game.UI.AudioVolumeSettings.Save();Check(UnityEngine.PlayerPrefs.Saves==1,"Save not forwarded");
  typeof(Game.UI.AudioVolumeSettings).GetMethod("Reset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
  Check(Game.UI.AudioVolumeSettings.Music==.25f&&Game.UI.AudioVolumeSettings.Ambience==.7f,"Preferences not restored on restart");
  Game.UI.AudioVolumeSettings.Music=-3;Check(Game.UI.AudioVolumeSettings.Music==0,"Mute/clamp broken");
  Game.UI.AudioVolumeSettings.Ambience=3;Check(Game.UI.AudioVolumeSettings.Ambience==1,"Upper clamp broken");
  Game.UI.AudioVolumeSettings.Music=float.NaN;Check(Game.UI.AudioVolumeSettings.Music==1,"Invalid preference reaches audio");
 }
}
'@
Add-Type -TypeDefinition ($stubs + $production + $tests)
[AudioPreferenceChecks]::Run()

$button = Get-Content (Join-Path $root 'Art/UI/Prefabs/ListButton.prefab') -Raw
if (!$button.Contains('--- !u!114 &1044404767601426280')) { throw 'Settings button prefab component ID changed.' }
$panel = Get-Content (Join-Path $root 'Art/UI/Prefabs/UIPanel.prefab') -Raw
if (!$panel.Contains('--- !u!1 &5335655854925384093')) { throw 'Settings panel prefab ID changed.' }
foreach ($scene in 'MainMenu','WorldScene') {
 $text = Get-Content (Join-Path $root "Scenes/$scene.unity") -Raw
 $ids = [regex]::Matches($text,'(?m)^--- !u!\d+ &(-?\d+)') | ForEach-Object { $_.Groups[1].Value }
 if (($ids | Group-Object | Where-Object Count -gt 1)) { throw "$scene has duplicate object IDs." }
 if (!$text.Contains('m_Script: {fileID: 11500000, guid: 743a32dc00a445f3a18a87f5e00c32ab, type: 3}') -or
     !$text.Contains('buttonPrefab: {fileID: 1044404767601426280, guid: 0b971797c26574811b4deca807afad45, type: 3}')) { throw "$scene settings wiring missing." }
}
'Audio settings PASS: production preference defaults, change events, persistence reload, mute/clamping, invalid values; scene IDs and prefab references. Unity UI interaction not tested.'
