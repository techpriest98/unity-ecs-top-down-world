$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$scene = Get-Content (Join-Path $root 'Scenes/WorldScene/World.unity') -Raw
$authoring = Get-Content (Join-Path $root 'Scripts/Player/PlayerAuthoring.cs') -Raw
$spawn = Get-Content (Join-Path $root 'Scripts/Player/Visual/Systems/PlayerVisualSpawnSystem.cs') -Raw
$parts = [regex]::Match($scene, '(?s)  visualParts:\r?\n(.*?)(?=---)').Groups[1].Value
$references = [regex]::Matches($parts, 'fileID: (\d+), guid: ([a-f0-9]{32}), type: 3')
if ($references.Count -lt 11) { throw 'Expected core visual parts plus both branch hands.' }
$prefabs = @(Get-ChildItem (Join-Path $root 'Prefabs/PlayerVisuals') -Filter '*.prefab')
if ($references.Count -ne $prefabs.Count) { throw 'Every visual prefab must have a scene reference.' }
$seen = @{}
foreach ($reference in $references) {
    $asset = @($prefabs | Where-Object { (Get-Content ($_.FullName + '.meta') -Raw).Contains($reference.Groups[2].Value) })
    if ($asset.Count -ne 1) { throw 'Prefab GUID must resolve to exactly one asset.' }
    $text = Get-Content $asset[0].FullName -Raw
    if ($text -notmatch ('--- !u!114 &' + $reference.Groups[1].Value + '\r?\n') -or
        $text -notmatch 'm_Father: \{fileID: 0\}' -or
        $text -notmatch 'm_LocalPosition: \{x: 0, y: 1, z: 0\}' -or
        $text -notmatch 'm_LocalScale: \{x: 1.2, y: 2.4, z: 1\}' -or
        $text -notmatch 'guid: 9f123a8ce88b74caa8bdbe5519935cc7') {
        throw ('Missing authoring root, original transform or player material: ' + $asset[0].Name)
    }
    $part = [regex]::Match($text, '(?m)^  part: (\d+)').Groups[1].Value
    if (!$part -or $seen.ContainsKey($part)) { throw 'Missing/duplicate CharacterPart.' }
    $seen[$part] = $true
    if ($scene -match ('(?m)^  m_Name: ' + [regex]::Escape($asset[0].BaseName) + '\r?$')) {
        throw 'Scene still contains a migrated visual object.'
    }
}
if ($authoring -notmatch 'GetEntity\(part.gameObject, TransformUsageFlags.Dynamic\)' -or
    $spawn -notmatch 'InitializationSystemGroup' -or
    $spawn -notmatch 'EntityManager.Instantiate\(request.Prefab\)' -or
    $spawn -notmatch 'new Parent \{ Value = owner \}' -or
    $spawn -notmatch 'GetBuffer<LinkedEntityGroup>\(owner\)' -or
    $spawn -notmatch 'RemoveComponent<PlayerVisualPrefab>\(owner\)') {
    throw 'Prefab registration, per-owner instantiation, parenting, ownership or one-shot consumption missing.'
}
if (!$seen.ContainsKey('9') -or !$seen.ContainsKey('10')) { throw 'Missing left/right branch visual prefab.' }
'PASS unique prefab references including both branch hands, original transforms/material, no scene duplicates, initialization/parent/linked ownership and one-shot spawn contracts. Unity baking/Play Mode still required.'
