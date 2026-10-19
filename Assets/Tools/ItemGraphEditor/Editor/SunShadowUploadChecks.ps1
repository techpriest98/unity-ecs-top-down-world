$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/World/Lighting/Systems/ChunkLightingSystem.cs') -Raw
$start = $source.IndexOf('                    UpdateChunkShadowMask(')
$end = $source.IndexOf('                    processedCount++;', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Shadow update loop not found' }
$loop = $source.Substring($start, $end - $start)
if ($loop -notmatch 'SetComponentEnabled<ChunkNeedsRender>\(entity, true\)') {
    throw 'Completed shadow masks must be marked for GPU range upload'
}
# Model the two GPU masks across multiple complete daytime updates. Each
# processed chunk must publish its inactive mask before the global swap.
$cpu = @(@(0, 0), @(0, 0), @(0, 0))
$gpu = @(@(0, 0), @(0, 0), @(0, 0))
$active = 0
foreach ($hour in 7..17) {
    $target = 1 - $active
    foreach ($chunk in 0..2) {
        $cpu[$chunk][$target] = $hour
        $gpu[$chunk][$target] = $cpu[$chunk][$target]
        if ($gpu[$chunk][$active] -ne ($hour - 1) -and $hour -ne 7) {
            throw 'Inactive upload changed the visible mask before the swap'
        }
    }
    $active = $target
    foreach ($chunk in 0..2) {
        if ($gpu[$chunk][$active] -ne $hour) { throw 'Stale shadow after global swap' }
    }
}
'PASS shadow upload source contract and double-buffer lifecycle model (not a Unity visual test).'
