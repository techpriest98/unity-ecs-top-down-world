$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/Items/Skills/Configs/ItemSkillDefinition.cs') -Raw
$binding = $source.Substring($source.IndexOf('    public enum SkillInputButton'))
$binding = $binding.Substring(0, $binding.IndexOf('    [CreateAssetMenu'))
$harness = @'
using System;
using UnityEngine.InputSystem;
namespace UnityEngine.InputSystem {
 public enum Key { None, Digit1, Digit2, Digit3, Digit4, Digit5, E, F }
 public class Button { public bool wasPressedThisFrame; }
 public class KeyControl:Button {public Key keyCode;public string displayName;}
 public class Keyboard {public static Keyboard current;public KeyControl[] allKeys;}
 public class Mouse {public static Mouse current;public Button leftButton=new Button(),middleButton=new Button(),rightButton=new Button();}
}
public static class SkillBindingChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(){
  var binding=new SkillInputBinding();
  Check(!binding.WasPressed(0),"Missing devices must not trigger");
  for(int slot=0;slot<5;slot++){
   Keyboard.current=new Keyboard {allKeys=new[]{new KeyControl{keyCode=(Key)((int)Key.Digit1+slot),displayName=(slot+1).ToString(),wasPressedThisFrame=true}}};
   Check(binding.WasPressed(slot)&&binding.Label(slot)==(slot+1).ToString(),"Default slot compatibility failed");
  }
  binding.Button=SkillInputButton.Keyboard;binding.Key=Key.E;
  Keyboard.current=new Keyboard{allKeys=new[]{new KeyControl{keyCode=Key.E,displayName="E",wasPressedThisFrame=true}}};
  Check(binding.WasPressed(4)&&binding.Label(4)=="E","Custom keyboard binding depends on slot");
  Keyboard.current.allKeys[0].displayName="У";
  Check(binding.WasPressed(4)&&binding.Label(4)=="E","Layout change translated label or changed physical binding");
  Keyboard.current=null;
  Check(binding.Label(4)=="E","Keyboard label requires a connected device");
  binding.Key=Key.Digit3;Check(binding.Label(0)=="3","Physical digit label retained enum prefix");
  binding.Key=Key.None;Check(!binding.WasPressed(0)&&binding.Label(0)=="","None key triggered");
  Keyboard.current=null;Mouse.current=new Mouse();
  var buttons=new[]{SkillInputButton.LeftMouse,SkillInputButton.MiddleMouse,SkillInputButton.RightMouse};
  var labels=new[]{"LMB","CMB","RMB"};
  var controls=new[]{Mouse.current.leftButton,Mouse.current.middleButton,Mouse.current.rightButton};
  for(int i=0;i<3;i++){
   binding.Button=buttons[i];Check(binding.IsMouse&&!binding.WasPressed(2),"Mouse false trigger");
   controls[i].wasPressedThisFrame=true;
   Check(binding.WasPressed(2)&&binding.Label(2)==labels[i],"Mouse button/label mismatch");
   controls[i].wasPressedThisFrame=false;
  }
  binding.Button=SkillInputButton.None;Check(!binding.WasPressed(0)&&binding.Label(0)=="","Disabled binding triggered");
  Mouse.current=null;binding.Button=SkillInputButton.LeftMouse;Check(!binding.WasPressed(0),"Missing mouse triggered");
 }
}
'@
Add-Type -TypeDefinition ($harness + $binding)
[SkillBindingChecks]::Run()
$input = Get-Content (Join-Path $root 'Scripts/Player/Input/Systems/PlayerSkillInputSystem.cs') -Raw
$hud = Get-Content (Join-Path $root 'Scripts/UI/EquipmentHud.cs') -Raw
if ($input -notmatch 'skill.InputBinding.WasPressed\(index\)' -or
    $input -notmatch 'Time.timeScale <= 0f' -or $input -notmatch 'IsPointerOverGameObject' -or
    $hud -notmatch 'skill.InputBinding.Label\(index\)') { throw 'Shared binding, pause/UI guards or HUD label missing.' }
'PASS actual binding code: legacy 1–5, custom keyboard, LMB/CMB/RMB without keyboard, disabled/missing devices; shared HUD input and pause/UI contracts. Unity input/UI Play Mode remains manual.'
