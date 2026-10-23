$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mask = Get-Content (Join-Path $root 'Shaders/WorldFogMask.shader') -Raw
$sky = Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/SkyLightSystem.cs') -Raw
$renderer = Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs') -Raw
if ($mask -notmatch 'o.sky=lerp\(bottomSky,topSky,corner.y\)' -or
    $mask -notmatch 'return smoothstep\(.05,.85,saturate\(i.sky\)\).xxxx' -or
    $sky -notmatch 'MaximumLight = 15' -or $sky -notmatch 'SpreadAttenuation = [1-9][0-9]*;') {
    throw 'Alpha mask must use interpolated skylight independent of solar/local light.'
}
$previous = -1
for ($level = 0; $level -le 15; $level++) {
    $t = [Math]::Clamp(($level / 15.0 - .05) / .8, 0.0, 1.0)
    $density = $t * $t * (3 - 2 * $t)
    if ($density -lt $previous) {
        throw 'Alpha must monotonically track skylight density.'
    }
    if (($level -eq 0 -and $density -ne 0) -or ($level -eq 15 -and $density -ne 1)) { throw 'Wrong fade endpoints.' }
    $previous = $density
}
if ($renderer -notmatch 'DrawFogMask\(projectedWaterCellsBuffer' -or
    $renderer -notmatch 'DrawFogMask\(projectedClippedWaterCellsBuffer' -or
    $renderer -notmatch 'CompareFunction.LessEqual' -or
    $mask -notmatch 'HasTopInset\(cell.FaceData\)') {
    throw 'Sheltered water must be masked with its own depth test and actual inset geometry.'
}
$fog = Get-Content (Join-Path $root 'Shaders/WorldFog.shader') -Raw
if ($mask -match 'threshold|uint low=|uint high=' -or
    $fog -notmatch 'opacity \*= SAMPLE_TEXTURE2D\(_FogSkyMask' -or
    $renderer -notmatch 'RenderTextureFormat.R8' -or $renderer -notmatch 'ColorWriteMask.Red') {
    throw 'Smooth alpha must replace dithering and write/sample the red mask channel.'
}
'PASS continuous skylight alpha, monotonic dark/open endpoints, R8 channel wiring, no dithering and both water layers.'
