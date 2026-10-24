$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader = Get-Content (Join-Path $root 'Shaders/TreeSprite.shader') -Raw
$body = [regex]::Match($shader,'(?s)float TreeCardDepth\([^)]*\)\s*\{(.*?)\}').Groups[1].Value.Replace('lerp(','Lerp(')
if (!$body) { throw 'Missing tree card depth function' }
Add-Type -TypeDefinition @"
public static class TreeCardCheck {
 static double Lerp(double a,double b,double t) { return a+(b-a)*t; }
 public static double Depth(double anchorDepth,double legacyDepth,double _TerrainDepthOrigin,double _ContinuousTerrainDepth) {
 $body
 }
}
"@
foreach ($anchor in -1000,0,1000) {
 foreach ($origin in -500,0,500) {
  if ([Math]::Abs([TreeCardCheck]::Depth($anchor,-.9,$origin,1)-($anchor-$origin-.001)) -gt 1e-9) { throw 'Wrong tree depth' }
 }
}
if ($shader -match 'SV_Depth|_TreeMasks' -or $shader -notmatch 'TreeCardDepth\(data.Layers.z\)') { throw 'Tree must be a constant-depth card without CPU masks' }
$batch=Get-Content (Join-Path $root 'Scripts/World/Trees/TreeDrawBatch.cs') -Raw
if ($batch -match 'Texture2DArray|SetPixelData|maskVersions' -or $batch -notmatch 'SurfaceDepthBounds') { throw 'Tree batch still uploads CPU masks or lacks bounds' }
Write-Output 'Tree cards: shared depth origin, constant vertex plane, no fragment depth or CPU mask uploads PASS'
