$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read($path) { Get-Content (Join-Path $root $path) -Raw }
$shader=Read 'Shaders/TreeSprite.shader'
$render=Read 'Scripts/World/Trees/PineTreeTestSystem.cs'
$batch=Read 'Scripts/World/Trees/TreeDrawBatch.cs'
$fog=Read 'Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs'
foreach ($pattern in 'WriteMask 8 Comp Always Pass Replace','receiver.y - _TreeFogHeight - .01','clip\(color.a-.1\)','TreeReceiverInCutVolume','clip\(clearance - noise \* _TreeFogEdge.x\)','return 0;') {
 if ($shader -notmatch $pattern) { throw "Missing tree fog contract: $pattern" }
}
foreach ($pattern in 'renderQueue = 3099','CompareFunction.Equal','SetFloat\("_TreeZWrite", 0f\)','SetFloat\("_TreeColorMask", 0f\)','SetFloat\("_TreeStencilRef", 8f\)','batch.Submit\(fogMaskMaterial, properties, false\)','Object.Destroy\(fogMaskMaterial\)') {
 if ($render -notmatch $pattern) { throw "Missing tree fog submission contract: $pattern" }
}
if ($batch -notmatch 'if \(upload\) buffer.SetData' -or $render -match 'GetTreeMaskPixels') { throw 'Fog must reuse tree buffers without CPU masks or duplicate upload' }
if ($fog -notmatch 'ApplyTreeFog' -or $fog -notmatch 'DebugFogDensity >= .001f' -or $fog -notmatch 'WorldRenderDebug.Enabled && hasFogSeaLevel') { throw 'Fog activation must follow world fog state' }
foreach ($pattern in 'material.SetFloat\("_TreeFogMaskPass", 0f\)','material.SetFloat\("_TreeColorMask", 15f\)','material.SetFloat\("_TreeZWrite", 1f\)','material.SetFloat\("_TreeZTest", \(float\)UnityEngine.Rendering.CompareFunction.LessEqual\)') {
 if ($render -notmatch $pattern) { throw "Missing explicit normal-pass state after shader reload: $pattern" }
}
if ($shader -notmatch 'SamplerState tree_fog_linear_repeat_sampler;' -or $shader -match '\btree_fog_repeat_sampler\b') { throw 'Fog inline sampler must specify filtering and wrap mode for Unity' }
Write-Output 'Tree fog source contracts: visible alpha/local clips, physical pixel height, noisy edge, depth-equal stencil pass before fog, reuse/no CPU masks and material cleanup PASS (Unity visual result not tested)'
