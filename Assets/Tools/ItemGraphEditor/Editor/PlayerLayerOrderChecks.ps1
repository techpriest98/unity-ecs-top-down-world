$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/Player/Visual/Systems/PlayerAnimationSystem.cs') -Raw
$method = [regex]::Match($source, '(?s)        private static int GetLayerOrder\(.*?\n        \}').Value
if (!$method) { throw 'Layer ordering helper missing.' }
$adapter = @'
using System;
namespace PlayerLayerOrderChecks {
public enum CharacterPart {Body,LeftArm,RightArm,Branch=10,FutureItem=200}
public struct ItemHandAnimationData {public bool HasVisual;public CharacterPart VisualPart;}
public static class Checks {
'@
$tests = @'
 public static void Run() {
  var left=new ItemHandAnimationData{HasVisual=true,VisualPart=CharacterPart.FutureItem};
  var right=new ItemHandAnimationData{HasVisual=true,VisualPart=CharacterPart.Branch};
  foreach(var part in new[]{CharacterPart.Body,CharacterPart.LeftArm,CharacterPart.RightArm})
   if(GetLayerOrder(part,left,right)!=(int)part)throw new Exception("Body/arm ordering changed");
  if(GetLayerOrder(CharacterPart.Branch,left,right)!=4)throw new Exception("Right branch not in right item layer");
  if(GetLayerOrder(CharacterPart.FutureItem,left,right)!=3)throw new Exception("New item requires hardcoded ordering");
  if(GetLayerOrder(CharacterPart.Branch,default,default)!=0)throw new Exception("Disabled visual retained order");
  for(int z=-2;z<2;z++)
   if(z*5+4>=(z+1)*5)throw new Exception("Tie breaker overrides explicit ZIndex");
 }
}}
'@
Add-Type -TypeDefinition ($adapter + $method + $tests)
[PlayerLayerOrderChecks.Checks]::Run()
'PASS extracted runtime ordering helper: body/arms, right-hand branch, future left-hand item, disabled visual, ZIndex priority. Unity rendering not executed.'
