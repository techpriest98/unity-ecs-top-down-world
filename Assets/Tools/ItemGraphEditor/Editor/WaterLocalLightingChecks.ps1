$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$water = Get-Content (Join-Path $root 'Shaders/ChunkWaterProcedural.shader') -Raw
$terrain = Get-Content (Join-Path $root 'Shaders/ChunkProcedural.shader') -Raw
foreach ($shader in @($water, $terrain)) {
    if ($shader -notmatch '#include "DetailedLocalLighting.hlsl"' -or
        $shader -notmatch 'localLight = SingleLocalLightDiagnostic\(input.ProjectionPosition, input.ShadowReceiverHeight,') {
        throw 'Terrain and water must use the same detailed local-light implementation.'
    }
}
if ($water -notmatch 'sourceHeight = \(cell.FaceData >> 24\) & 255u' -or
    $water -notmatch 'output.ShadowReceiverHeight -= insetHeight' -or
    $water -notmatch 'referenceNormal, worldNormal, referenceNormal, localLight') {
    throw 'Water must use physical receiver height, inset and wave-normal response.'
}
'PASS water/terrain shared local lighting, packed receiver height, water inset and wave-normal wiring.'
& (Join-Path $PSScriptRoot 'OceanWaveChecks.ps1')
