$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$packing = Get-Content (Join-Path $root 'Shaders/ProjectedCellPacking.hlsl') -Raw
$body = [regex]::Match($packing, '(?s)float TerrainSurfaceDepth\([^)]*\)\s*\{(.*?)\}').Groups[1].Value
if (!$body) { throw 'TerrainSurfaceDepth not found' }
$body = $body.Replace('cell.ChunkPosition.y', 'chunkY').Replace('cell.ChunkPosition.z', 'chunkZ')
$body = $body.Replace('float localDepth', 'double localDepth')
$body = $body.Replace('float physicalDepth', 'double physicalDepth').Replace('lerp(', 'Lerp(')
Add-Type -TypeDefinition @"
public static class TerrainDepthCheck {
 const double _TerrainDepthOrigin = 123.5;
 static double Lerp(double a, double b, double t) { return a + (b-a)*t; }
 public static double Depth(double chunkY, double chunkZ, double projectedY, double height, double _ContinuousTerrainDepth) {
 $body
 }
}
"@
function Assert-Near($actual, $expected, $label) {
    if ([Math]::Abs($actual - $expected) -gt 1e-10) { throw "$label : $actual != $expected" }
}
# All four views reduce to the same view-depth lattice; include negative chunks.
foreach ($chunk in -12,-1,0,1,12) {
    $chunkY = -8.0 * $chunk
    $chunkZ = -.01 * $chunk
    foreach ($v in 0..16) {
        foreach ($height in 0,40,40.5,255) {
            $nearY = $chunkY + 8.5 + $height - .5 * $v
            $farY = $nearY + .5
            Assert-Near ([TerrainDepthCheck]::Depth($chunkY,$chunkZ,$nearY,$height,1)) ($chunkZ*1600 - ($v+1) - 123.5) 'Top near edge'
            Assert-Near ([TerrainDepthCheck]::Depth($chunkY,$chunkZ,$farY,$height,1)) ($chunkZ*1600 - $v - 123.5) 'Top far edge'
            # A vertical half-side shares its near Top edge at the upper endpoint.
            Assert-Near ([TerrainDepthCheck]::Depth($chunkY,$chunkZ,$nearY-.5,$height-.5,1)) ($chunkZ*1600 - ($v+1) - 123.5) 'Side constant plane'
        }
    }
    Assert-Near ([TerrainDepthCheck]::Depth($chunkY,$chunkZ,$chunkY+9-8,0,1)) ([TerrainDepthCheck]::Depth($chunkY-8,$chunkZ-.01,$chunkY-8+9,0,1)) 'Chunk boundary continuity'
}
Write-Output 'Terrain surface depth: Top slope, Side plane, half heights, negative chunks, boundaries PASS'
