$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeTestSystem.cs') -Raw
$resources=$source.Substring($source.IndexOf('public partial class TreeResourcePreparationSystem'),$source.IndexOf('public partial class TreePopulationSystem')-$source.IndexOf('public partial class TreeResourcePreparationSystem'))
$population=$source.Substring($source.IndexOf('public partial class TreePopulationSystem'),$source.IndexOf('public partial class TreeRenderSystem')-$source.IndexOf('public partial class TreePopulationSystem'))
if ($resources -match 'override void OnStopRunning' -or $population -match 'override void OnStopRunning') {throw 'Simulation stop must retain tree resources and population; do not add destructive stop callbacks'}
if ($resources -notmatch 'OnDestroy\(\) => ReleaseVisuals\(\)' -or $resources -notmatch 'visualConfiguration != visuals' -or $population -notmatch 'observedResourceRevision != resources.Revision') {throw 'World disposal and explicit resource/configuration invalidation must still work'}
$pause=Get-Content (Join-Path $root 'Scripts/UI/PauseMenu.cs') -Raw
$hud=Get-Content (Join-Path $root 'Scripts/UI/EquipmentHud.cs') -Raw
if ($pause -notmatch 'group.Enabled = false' -or $hud -notmatch 'simulation.Enabled = false' -or $pause -match 'PresentationSystemGroup') {throw 'Expected simulation-only ESC/Rubedo pause; review tree presentation lifetime if this changes'}
if ($source -notmatch '(?s)UpdateInGroup\(typeof\(PresentationSystemGroup\), OrderLast = true\).*?public partial class TreeRenderSystem') {throw 'Tree rendering must remain in presentation during simulation pause'}
Write-Output 'Tree pause source contracts PASS: ESC/Rubedo stop only simulation; tree resources, pending bake, instance buffers and population bookkeeping remain resident; disposal/configuration invalidation retained. Unity pause/resume visual behavior not executed.'
