$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader = Get-Content (Join-Path $root 'Shaders/WorldItemSprite.shader') -Raw
$body = [regex]::Match($shader, '(?s)float ItemSurfaceDepth\([^)]*\)\s*\{(.*?)\}').Groups[1].Value
if (!$body) { throw 'Missing item depth function' }
$body = $body.Replace('float physicalDepth','double physicalDepth').Replace('lerp(','Lerp(')
Add-Type -TypeDefinition @"
public static class ItemDepthCheck {
 static double Lerp(double a,double b,double t) { return a+(b-a)*t; }
 public static double Depth(double legacyDepth,double anchorDepth,double projectedY,double centreY,double grounded,double _TerrainDepthOrigin,double _ContinuousTerrainDepth) {
 $body
 }
}
"@
foreach ($anchor in -1000,0,1000) {
    foreach ($origin in -500,0,500) {
        foreach ($delta in -.25,0,.25) {
            $ground=[ItemDepthCheck]::Depth(-.9,$anchor,100+$delta,100,1,$origin,1)
            $air=[ItemDepthCheck]::Depth(-.9,$anchor,100+$delta,100,0,$origin,1)
            if ([Math]::Abs($ground-($anchor+2*$delta-$origin-.002)) -gt 1e-9) { throw 'Ground item is not parallel to Top' }
            if ([Math]::Abs($air-($anchor-$origin-.002)) -gt 1e-9) { throw 'Air item is not upright' }
        }
    }
}
$source=Get-Content (Join-Path $root 'Scripts/Items/WorldItemRenderSystem.cs') -Raw
if ($source -notmatch 'UpdateAfter\(typeof\(WorldProjectionDrawSystem\)\)' -or $source -notmatch 'SurfaceDepthBounds\(bounds\)') { throw 'Missing current camera/bounds integration' }
if ($source -match 'WorldItemOcclusionCache|GetMask\(|occlusion\.' -or $shader -match '_OcclusionMask|MaskPixels') { throw 'CPU item masks must not remain in the rendering path' }
if ($source -notmatch 'TryGetGroundTile\(support, out _\)' -or $shader -notmatch 'clip\(SAMPLE_TEXTURE2D_LOD\(_LocalTileCoverage') { throw 'Local support and per-pixel F10 coverage must remain' }
Write-Output 'Items: grounded Top plane, upright airborne plane, shared origin, draw order and bounds PASS'
