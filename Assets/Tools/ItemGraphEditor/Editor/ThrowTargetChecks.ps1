$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/Items/Skills/Configs/ThrowItemAction.cs') -Raw
$method = [regex]::Match($source, '(?s)        public static float3 GetThrowTarget\(.*?\n        \}')
if (!$method.Success) { throw 'Throw target helper missing.' }
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$adapter = @'
using Unity.Mathematics;
public enum ProjectedFaceType { Top, SideUpper, SideLower }
public struct SelectedProjectedCell { public bool IsValid; public ProjectedFaceType FaceType; public bool Half; }
public static class ProjectedCellInteractionUtility {
 public static float3 GetTopSurfacePosition(in SelectedProjectedCell s,int3 p) => new float3(p.x+.5f,p.y-(s.Half?.5f:0),p.z+.5f);
}
public static class ThrowChecks {
'@
$tests = @'
 public static void Run() {
  var p = new int3(-17,64,-33);
  foreach(var face in new[]{ProjectedFaceType.SideUpper,ProjectedFaceType.SideLower}) {
   var s = new SelectedProjectedCell { IsValid=true, FaceType=face };
   if(!math.all(GetThrowTarget(s,p,.04f)==new float3(-16.5f,64.5f,-32.5f))) throw new System.Exception("Side target changed Before cell");
  }
  foreach(bool half in new[]{false,true}) {
   var s = new SelectedProjectedCell { IsValid=true, FaceType=ProjectedFaceType.Top, Half=half };
   var expected = new float3(-16.5f,64-(half?.5f:0)+.04f,-32.5f);
   if(!math.all(GetThrowTarget(s,p,.04f)==expected)) throw new System.Exception("Top landing height changed");
  }
 }
}
'@
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
Add-Type -TypeDefinition ($adapter + $method.Value + $tests) -ReferencedAssemblies $refs
[ThrowChecks]::Run()
$graph = Get-Content (Join-Path $root 'Configs/Items/SharedSkills/ThrowTopTarget.asset') -Raw
if ($graph -notmatch '(?m)^  Kind: 6\r?$' -or $graph -notmatch '(?m)^  Cursor: 0\r?$') { throw 'Throw must use Cursor Before without a Top-only gate.' }
if ($source.Contains('out var support') -or $source.Contains('!selection.IsValid || selection.FaceType != ProjectedFaceType.Top')) { throw 'Top/floor-only restriction returned.' }
'PASS Throw: actual target helper with selection adapter; upper/lower sides, negative Before coordinates, top and half-block heights; serialized Cursor Before gate. Unity physics not executed.'
