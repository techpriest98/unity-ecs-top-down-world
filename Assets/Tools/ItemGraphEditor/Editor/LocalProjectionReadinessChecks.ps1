$ErrorActionPreference = 'Stop'
$assetsRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$renderer = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/LocalProjectionRenderer.cs') -Raw
# Execute the actual readiness implementation with an adapted component lifecycle.
$start = $renderer.IndexOf('public bool IsReady =>')
$end = $renderer.IndexOf('public GraphicsBuffer OpaqueBuffer', $start)
$readiness = $renderer.Substring($start, $end - $start)
Add-Type -TypeDefinition @"
namespace UnityEngine { public static class Time { public static int frameCount; } }
public class LocalReadinessProbe {
 public bool IsActive = true;
 $readiness
 public static void Run() {
  var p = new LocalReadinessProbe();
  if(p.IsReady) throw new System.Exception("Unpublished snapshot is visible");
  p.MarkReady();
  if(!p.IsReady) throw new System.Exception("Published snapshot is hidden");
  UnityEngine.Time.frameCount++;
  if(!p.IsReady) throw new System.Exception("Valid snapshot disappeared solely because the render frame advanced");
  p.OnValidate();
  if(p.IsReady) throw new System.Exception("Inspector change exposed the retained snapshot before validation");
  p.MarkReady(); // Validated cache hit: no GPU upload required.
  if(!p.IsReady) throw new System.Exception("Validated cached snapshot is hidden");
  p.MarkReady(); p.IsActive = false;
  if(p.IsReady) throw new System.Exception("Disabled snapshot is visible");
  p.Invalidate(); p.IsActive = true;
  if(p.IsReady) throw new System.Exception("Re-enable exposed invalidated snapshot");
 }
}
"@
[LocalReadinessProbe]::Run()
$system = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/ChunkProceduralRenderSystem.cs') -Raw
$local = $system.Substring($system.IndexOf('public partial class LocalProjectionRenderSystem'))
if ($local -notmatch 'renderer\.MarkReady\(\); return;' -or
    $local -notmatch 'UploadLocalCoverage\([^;]+;\s*renderer\.MarkReady\(\);' -or
    $renderer -notmatch 'if \(!IsReady\) return;' -or
    $renderer -notmatch '!localLayer && IsReady &&') {
    throw 'Draw/coverage readiness gate or complete-publication/cache-hit validation is missing.'
}
$fog = Get-Content (Join-Path $assetsRoot 'Scripts/World/Rendering/ChunkProceduralRenderer.Fog.cs') -Raw
if ($fog -match 'IsActive: true' -or ([regex]::Matches($fog, 'IsReady: true')).Count -ne 2) {
    throw 'Fog must not consume an unvalidated local snapshot.'
}
'PASS actual readiness gate: retained ready snapshot across render frames, inspector invalidation, re-enable and validated reuse; publication/draw/fog source contracts. Unity scheduling/GPU not tested.'
