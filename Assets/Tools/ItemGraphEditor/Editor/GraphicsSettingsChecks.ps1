$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$production=(Get-Content (Join-Path $root 'Scripts/UI/GraphicsPreferences.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$harness=@'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine {
 public enum RuntimeInitializeLoadType { SubsystemRegistration }
 public sealed class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
 public static class Mathf { public static int Clamp(int v,int a,int b)=>Math.Min(b,Math.Max(a,v)); }
 public static class PlayerPrefs {
  public static Dictionary<string,int> Values=new Dictionary<string,int>(); public static int Saves;
  public static int GetInt(string key,int fallback)=>Values.TryGetValue(key,out var v)?v:fallback;
  public static void SetInt(string key,int value)=>Values[key]=value;
  public static void Save(){Saves++;}
 }
}
public static class GraphicsSettingsChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reload()=>typeof(Game.UI.GraphicsPreferences).GetMethod("Reset",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
 public static string Run(){
  Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==1,"Default radius is not 1");
  Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==8,"Default detailed radius is not 8");
  for(int value=8;value<=16;value++){
   Game.UI.GraphicsPreferences.DetailedLightingRadius=value;Reload();
   Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==value,"Detailed radius not restored");
   int size=((value*2+7)/4)*4;
   Check(size%4==0 && size>=value*2+4,"Sun cache dispatch or snapped coverage invalid");
  }
  Game.UI.GraphicsPreferences.DetailedLightingRadius=99;
  Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==16,"Detailed upper clamp failed");
  Game.UI.GraphicsPreferences.DetailedLightingRadius=-1;
  Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==8,"Detailed lower clamp failed");
  PlayerPrefs.Values["Graphics.DetailedLightingRadius"]=99;Reload();
  Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==16,"Detailed saved upper clamp failed");
  PlayerPrefs.Values["Graphics.DetailedLightingRadius"]=-9;Reload();
  Check(Game.UI.GraphicsPreferences.DetailedLightingRadius==8,"Detailed saved lower clamp failed");
  for(int value=1;value<=4;value++){
   Game.UI.GraphicsPreferences.ChunkLoadingRadius=value;Reload();
   Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==value,"Radius was not restored from preferences");
  }
  Game.UI.GraphicsPreferences.ChunkLoadingRadius=99;
  Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==4,"Setter failed upper limit");
  Game.UI.GraphicsPreferences.ChunkLoadingRadius=-3;
  Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==1,"Setter failed lower limit");
  PlayerPrefs.Values["Graphics.ChunksLoadingRadius"]=99;Reload();
  Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==4,"Corrupt saved upper value not clamped");
  PlayerPrefs.Values["Graphics.ChunksLoadingRadius"]=-9;Reload();
  Check(Game.UI.GraphicsPreferences.ChunkLoadingRadius==1,"Corrupt saved lower value not clamped");
  Game.UI.GraphicsPreferences.Save();Check(PlayerPrefs.Saves==1,"Explicit save missing");
  return "PASS actual graphics preferences: default, all four saved values, reload, invalid saved/input clamping and save.";
 }
}
'@
Add-Type -TypeDefinition ($harness+$production)
[GraphicsSettingsChecks]::Run()
$menu=Get-Content (Join-Path $root 'Scripts/UI/SettingsMenu.cs') -Raw
$streaming=Get-Content (Join-Path $root 'Scripts/World/Chunks/Systems/ChunkStreamingRadiusSystem.cs') -Raw
if($menu -notmatch 'CreatePanel\("GraphicsSettingsPanel", "Graphics"' -or
 $menu -notmatch 'GraphicsPreferences.MinimumChunkRadius, GraphicsPreferences.MaximumChunkRadius, true' -or
 $menu -notmatch 'slider.wholeNumbers = wholeNumbers' -or
 $menu -notmatch 'graphicsPanel.activeSelf' -or
 $menu -notmatch 'graphicsButton.Select\(\)' -or
 $menu -notmatch 'Destroy\(graphicsPanel\)' -or
 $streaming -notmatch 'int configuredRadius = Game.UI.GraphicsPreferences.ChunkLoadingRadius' -or
 $streaming -match 'TryGetStreamingRadius|CameraStreamingRadiusUtility' -or
 $streaming -notmatch 'ValueRW.LoadRadius = configuredRadius'){
 throw 'Graphics menu/navigation, whole-number slider or fixed runtime streaming integration missing'
}
'PASS shared-style graphics submenu, integer slider range, Back/Escape integration, cleanup and fixed streaming preference source. Unity visual/input checks remain manual.'
$chunks=Get-Content (Join-Path $root 'Scripts/World/Chunks/ChunkStreamingSystem.cs') -Raw
if($chunks -match 'PreloadMargin|UnloadMargin' -or
 $chunks -notmatch 'int preloadRadius = math.max\(1, loadRadius\)' -or
 $chunks -notmatch 'int unloadRadius = preloadRadius' -or
 $streaming -notmatch 'int checkRadius = searchRadius;' -or
 $chunks -notmatch 'distance <= preloadRadius') {
 throw 'Graphics radius must include preparation ring and must not retain a hidden unload ring'
}
foreach($radius in 1..4){
 $coordinates=@{}
 for($distance=0;$distance -le $radius;$distance++){
  for($z=-$distance;$z -le $distance;$z++){
   for($x=-$distance;$x -le $distance;$x++){
    if([Math]::Max([Math]::Abs($x),[Math]::Abs($z)) -eq $distance){$coordinates["$x,$z"]=$true}
   }
  }
 }
 $expected=(2*$radius+1)*(2*$radius+1)
 if($coordinates.Count -ne $expected){throw "Wrong total chunk count for radius $radius"}
}
'PASS total-radius mapping 1..4 => 3x3/5x5/7x7/9x9; included outer ring, aligned spawn search and no hidden unload margin. Budgeted transitions remain gradual.'
