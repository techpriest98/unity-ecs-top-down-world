# Scheduling/source contracts; Unity player-loop execution and GPU output are not tested here.
$ErrorActionPreference = 'Stop'
$assetsRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$world = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/ChunkProceduralRenderer.cs') -Raw
$local = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/LocalProjectionRenderer.cs') -Raw
$systems = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/ChunkProceduralRenderSystem.cs') -Raw
function Get-MethodBody([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $brace = $source.IndexOf('{', $start)
    $depth = 1
    for ($i = $brace + 1; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        if ($source[$i] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $source.Substring($brace, $i - $brace + 1) }
    }
    throw "Unclosed method: $signature"
}
if ($world.Contains('private void LateUpdate()')) { throw 'World rendering must not submit before ECS presentation.' }
$late = Get-MethodBody $local 'private void LateUpdate()'
if ($late -match 'Graphics\.|SubmitFrame\(|RenderLocalLayer\(') { throw 'Local LateUpdate must not submit drawing.' }
foreach ($contract in @(
    '[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]',
    '[UpdateAfter(typeof(LocalProjectionRenderSystem))]',
    '[UpdateAfter(typeof(PlayerSunShadowRenderSystem))]',
    'public partial class WorldProjectionDrawSystem : SystemBase'
)) {
    if (!$systems.Contains($contract)) { throw "Missing draw-order contract: $contract" }
}
$draw = $systems.Substring($systems.IndexOf('public partial class WorldProjectionDrawSystem'))
if (([regex]::Matches($draw, 'terrain\.SubmitFrame\(\)')).Count -ne 1 -or
    ([regex]::Matches($draw, 'local\.SubmitFrame\(\)')).Count -ne 1) {
    throw 'The final draw system must submit each layer exactly once.'
}
$localSubmit = Get-MethodBody $local 'internal void SubmitFrame()'
if (!$localSubmit.Contains('if (!IsReady) return;')) { throw 'Local submission bypasses readiness.' }
$worldSubmit = Get-MethodBody $world 'internal void SubmitFrame()'
if (!$worldSubmit.Contains('if (!WorldRenderDebug.ProjectionRequired) return;') -or
    !$worldSubmit.Contains('if (WorldRenderDebug.Enabled) RenderWorldFog();')) {
    throw 'World submission lost debug gates or fog.'
}
'PASS world/local draw submission follows ECS local publication and shadow preparation; no LateUpdate drawing; ready/F10/fog guards retained (source contracts only).'
