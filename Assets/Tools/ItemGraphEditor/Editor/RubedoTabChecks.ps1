$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read($path) {Get-Content (Join-Path $root $path) -Raw}
$source=Read 'Scripts/UI/EquipmentHud.cs'
$enum=[regex]::Match($source,'private enum RubedoTab\s*\{([^}]+)\}').Groups[1].Value
$method=[regex]::Match($source,'(?s)private static RubedoTab NextRubedoTab\([^;]+;').Value.Replace('private static','public static')
if (!$enum -or !$method) {throw 'Tab cycling implementation missing'}
Add-Type -TypeDefinition ('public static class RubedoTabsCheck { public enum RubedoTab {'+$enum+'}'+$method+@'
 public static void Run() {
  for(int start=0;start<3;start++) {
   var current=(RubedoTab)start;
   for(int i=1;i<=6;i++) {
    current=NextRubedoTab(current,false);
    if((int)current!=(start+i)%3)throw new System.Exception("Forward cycling order/wrap incorrect");
   }
   for(int i=1;i<=6;i++) {
    current=NextRubedoTab(current,true);
    if((int)current!=(start+6-i)%3)throw new System.Exception("Reverse cycling order/wrap incorrect");
   }
  }
 }
}
'@)
[RubedoTabsCheck]::Run()
$hud=Read 'Resources/EquipmentHud.prefab'
foreach ($style in 'Primary','Secondary') {
 $prefab=Read ("Art/UI/Prefabs/${style}Button.prefab")
 $guid=[regex]::Match((Read ("Art/UI/Prefabs/${style}Button.prefab.meta")),'guid: ([a-f0-9]+)').Groups[1].Value
 $reference=[regex]::Match($hud,"rubedo${style}Button: \{fileID: ([0-9]+), guid: $guid, type: 3\}")
 if (!$reference.Success -or $prefab -notmatch ('--- !u!114 &'+$reference.Groups[1].Value)) {throw "$style tab prefab reference missing or incorrect"}
 if ($source -notmatch ("CreateRubedoTabButton\(rubedo${style}Button, rect, tab\)")) {throw "$style prefab not instantiated"}
}
foreach ($pattern in 'pickerPrimaryTabs\[i\].gameObject.SetActive\(i == \(int\)tab\)','pickerSecondaryTabs\[i\].gameObject.SetActive\(i != \(int\)tab\)','keyboard.tabKey.wasPressedThisFrame','keyboard.leftShiftKey.isPressed','pickerPrimaryTabs\[\(int\)pickerActiveTab\].Select\(\)','button.onClick = new Button.ButtonClickedEvent\(\)') {
 if ($source -notmatch $pattern) {throw "Missing tab style/input contract: $pattern"}
}
Write-Output 'Rubedo tabs PASS: actual cycle method forward/reverse/wrap, correct menu prefab GUID/component references, active Primary/inactive Secondary variants, Tab input and focus. Unity visual/button events not executed.'
