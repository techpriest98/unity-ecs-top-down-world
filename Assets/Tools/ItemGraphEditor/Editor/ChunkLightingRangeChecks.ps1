# Source-contract checks, not a Unity ECS/GPU runtime test.
$ErrorActionPreference = 'Stop'
$assetsRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/ChunkProceduralRenderSystem.cs') -Raw
$baseSystem = $source.Substring(0, $source.IndexOf('internal struct LocalProjectionBuildJob'))
$call = $baseSystem.IndexOf('TryUpdateLightingRanges(renderer)')
$barrier = $baseSystem.IndexOf('if (pendingProjectionQuery.CalculateEntityCount() > 0 ||')
if ($call -lt 0 -or $barrier -lt 0 -or $call -ge $barrier) {
    throw 'Lighting-only uploads must run before the global full-snapshot barrier.'
}
$methodStart = $baseSystem.IndexOf('private bool TryUpdateLightingRanges(')
$methodEnd = $baseSystem.IndexOf('private static bool UpdateLightRecord', $methodStart)
$method = $baseSystem.Substring($methodStart, $methodEnd - $methodStart)
if ($method -match 'return false;') {
    throw 'An incompatible chunk must not abort lighting updates for other chunks.'
}
foreach ($contract in @(
    'IsComponentEnabled<ChunkNeedsProjection>(entity)',
    'IsComponentEnabled<ChunkNeedsLighting>(entity)',
    'snapshot.Opaque.Length != cells.Length',
    'snapshot.Water.Length != water.Length',
    'snapshot.ClipCells.Length != caps.Length',
    'renderer.CanUpdateRange(snapshot.OpaqueStart',
    'renderer.CanUpdateRange(snapshot.WaterStart',
    'snapshot.Opaque[i].HasSameGeometry',
    'snapshot.Water[i].HasSameGeometry',
    'if (!sameGeometry) { layoutReady = false; continue; }',
    'ready.Add(entity)',
    'if (!ready.Contains(entity) || !uploadedChunks.TryGetValue(entity, out var snapshot)) continue;',
    'return layoutReady && count == uploadedChunks.Count;'
)) {
    if (!$method.Contains($contract)) { throw "Missing lighting-range safety contract: $contract" }
}
$guard = $method.IndexOf('if (!ready.Contains(entity)')
$write = $method.IndexOf('renderer.UpdateRange(')
$clear = $method.IndexOf('SetComponentEnabled<ChunkNeedsRender>(entity, false)')
if ($guard -ge $write -or $clear -le $write) {
    throw 'Only validated chunks may upload ranges and clear their render-dirty flag.'
}
'PASS independent ready-chunk lighting uploads, geometry/range validation and retained full-snapshot barrier (source contracts only).'
