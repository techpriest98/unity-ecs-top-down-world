$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader = Get-Content (Join-Path $root 'Shaders/PlayerNormalTest.shader') -Raw
$body = [regex]::Match($shader, '(?s)float PlayerSurfaceDepth\([^)]*\)\s*\{(.*?)\}').Groups[1].Value
if (!$body) { throw 'Missing player depth function' }
$body = $body.Replace('_PlayerTerrainAnchor.x','anchor').Replace('_PlayerTerrainAnchor.w','ready').Replace('float physicalDepth','double physicalDepth').Replace('lerp(','Lerp(')
Add-Type -TypeDefinition @"
public static class PlayerDepthCheck {
 static double Lerp(double a,double b,double t) { return a+(b-a)*t; }
 public static double Depth(double legacyDepth,double anchor,double _TerrainDepthOrigin,double _ContinuousTerrainDepth,double ready) {
 $body
 }
}
"@
foreach ($anchor in -1000,-16,0,16,1000) {
    foreach ($origin in -500,0,500) {
        $bodyDepth = [PlayerDepthCheck]::Depth(-1,$anchor,$origin,1,1)
        if ([Math]::Abs($bodyDepth - ($anchor-$origin-.001)) -gt 1e-9) { throw 'Wrong physical anchor' }
        $armDepth = [PlayerDepthCheck]::Depth(-1.006,$anchor,$origin,1,1)
        if ([Math]::Abs(($armDepth-$bodyDepth)+.006) -gt 1e-9) { throw 'Lost animation layer order' }
        if ([PlayerDepthCheck]::Depth(-1.006,$anchor,$origin,1,0) -ne -1.006) { throw 'Missing player anchor did not fall back' }
    }
}
if ($shader -notmatch 'positionWS.z = PlayerSurfaceDepth\(positionWS.z\)') { throw 'Depth not applied in vertex shader' }
Write-Output 'Player depth: physical anchor, shared origin, part ordering, unavailable-anchor fallback PASS'
