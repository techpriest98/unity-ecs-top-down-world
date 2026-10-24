$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/World/Interaction/Systems/ProjectedCellSelectionRenderSystem.cs') -Raw
$body = [regex]::Match($source, '(?s)(float sourceHeight = selection.SourceAirIndex >> 8;.*?)properties.SetVector\("_CursorSurface"').Groups[1].Value
if (!$body) { throw 'Missing cursor surface calculation' }
Add-Type -TypeDefinition @"
public enum ProjectedFaceType { Top=1, SideUpper=2, SideLower=3 }
public struct CursorSelection { public int SourceAirIndex; public uint BlockData; public bool IsCutSurface; public ProjectedFaceType FaceType; }
public static class ProjectedCellPacking { public static bool IsLowerHalf(uint data) { return (data & (1u<<12))!=0; } }
public static class CursorDepthCheck {
 public static float[] Heights(int face,bool half,bool cut,bool fullHeightSide) {
  var selection = new CursorSelection { SourceAirIndex=40<<8, BlockData=half?1u<<12:0, IsCutSurface=cut, FaceType=(ProjectedFaceType)face };
  bool side=face!=1; float height=fullHeightSide?1f:.5f;
  $body
  return new float[]{surfaceBottom,surfaceTop};
 }
}
"@
function Check($face,$half,$cut,$full,$bottom,$top) {
    $actual=[CursorDepthCheck]::Heights($face,$half,$cut,$full)
    if ($actual[0] -ne $bottom -or $actual[1] -ne $top) { throw "Wrong cursor surface: $actual" }
}
Check 1 $false $false $false 40 40
Check 1 $true $false $false 39.5 39.5
Check 1 $false $true $false 41 41
Check 1 $true $true $false 40.5 40.5
foreach ($cut in $false,$true) {
    Check 2 $false $cut $false 40.5 41
    Check 3 $false $cut $false 40 40.5
    Check 2 $true $cut $false 40 40.5
    Check 2 $false $cut $true 40 41
    Check 3 $false $cut $true 40 41
}
if ($source -notmatch 'UpdateAfter\(typeof\(WorldProjectionDrawSystem\)\)') { throw 'Cursor must follow current depth origin and camera publication' }
Write-Output 'Cursor: Top, half Top, cut Top, upper/lower/full Sides and current-frame draw order PASS'
