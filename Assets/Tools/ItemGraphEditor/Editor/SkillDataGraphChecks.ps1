# Headless serialized-graph and extracted pure-method regression checks; no Unity gameplay.
[CmdletBinding()]
param([switch]$SkipOutputName)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Check($ok, [string]$message) { if (!$ok) { throw $message } }
function Read-Asset([string]$path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw }
function Field([string]$text, [string]$name) {
    $m = [regex]::Match($text, '(?m)^  ' + [regex]::Escape($name) + ':\s*([^\r\n]*)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}
function Guid-Of([string]$text) {
    $m = [regex]::Match($text, '\bguid:\s*([a-fA-F0-9]+)')
    if ($m.Success) { return $m.Groups[1].Value.ToLowerInvariant() }
    return ''
}
function Script-Guid([string]$name) { Guid-Of (Read-Asset "Scripts/Items/Skills/Configs/$name.cs.meta") }
$index = @{}
# Index existing metadata, preserving legacy non-32-character GUIDs used by this project.
foreach ($meta in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.meta' -File) {
    $guid = Guid-Of (Get-Content -LiteralPath $meta.FullName -Raw)
    if (!$guid) { continue }
    Check (!$index.ContainsKey($guid)) "Duplicate GUID $guid : $($meta.FullName)"
    $index[$guid] = $meta.FullName.Substring(0, $meta.FullName.Length - 5)
}
function Resolve-Ref([string]$reference, [string]$context, [switch]$Required) {
    $guid = Guid-Of $reference
    if (!$guid) {
        Check (!$Required -and (!$reference -or $reference -match '^\{fileID: 0\}$')) "Missing/malformed reference: $context ($reference)"
        return ''
    }
    Check ($index.ContainsKey($guid) -and (Test-Path -LiteralPath $index[$guid])) "Unresolved GUID $guid : $context"
    return $guid
}
$nodeScript = Script-Guid 'SkillChainNode'
$skillScript = Script-Guid 'ItemSkillDefinition'
$replaceScript = Script-Guid 'ReplaceWorldItemAction'
$nodes = @{}; $skills = @(); $replacements = @()
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'Configs') -Recurse -Filter '*.asset' -File) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $script = Guid-Of (Field $text 'm_Script')
    if ($script -notin @($nodeScript, $skillScript, $replaceScript)) { continue }
    $guid = Guid-Of (Get-Content -LiteralPath ($file.FullName + '.meta') -Raw)
    $asset = [pscustomobject]@{ Guid=$guid; Path=$file.FullName; Text=$text }
    # Validate every reference in graph/skill/replacement assets, including scripts and icons.
    foreach ($reference in [regex]::Matches($text, '\{fileID:[^}\r\n]+\}')) {
        $null = Resolve-Ref $reference.Value $file.FullName
    }
    if ($script -eq $nodeScript) { $nodes[$guid] = $asset }
    elseif ($script -eq $skillScript) { $skills += $asset }
    else { $replacements += $asset }
}
Check ($skills.Count -eq 8) "Expected ALL 8 skills, found $($skills.Count)"
$enumSource = Read-Asset 'Scripts/Items/Skills/Configs/SkillChainNode.cs'
$enumMatch = [regex]::Match($enumSource, 'enum SkillChainNodeKind\s*\{([^}]+)\}')
Check $enumMatch.Success 'Cannot read current node-kind enum'
$kinds = @($enumMatch.Groups[1].Value.Split(',') | ForEach-Object { $_.Trim() })
function Kind($node) {
    $value = Field $node.Text 'Kind'
    Check ($value -match '^\d+$' -and [int]$value -lt $kinds.Count) "Invalid Kind: $($node.Path)"
    return $kinds[[int]$value]
}
$seen = [System.Collections.Generic.HashSet[string]]::new()
$ids = [System.Collections.Generic.HashSet[string]]::new()
$pickDistance = Resolve-Ref (Field (Read-Asset 'Configs/PickUpWithinDistance.asset') 'Distance') 'Pick distance' -Required
function Visit([string]$guid, [string[]]$ancestors, [bool]$actionStarted, [bool]$hasRange) {
    Check ($nodes.ContainsKey($guid)) "Expected chain node: $guid"
    $node = $nodes[$guid]; $kind = Kind $node
    Check ($guid -notin $ancestors) "Cycle: $($node.Path)"
    Check ($ancestors.Count -lt 64) "Graph exceeds runtime 64-node limit: $($node.Path)"
    $null = $seen.Add($guid)
    $source = Resolve-Ref (Field $node.Text 'PositionSource') $node.Path
    Check (!$source -or $source -in $ancestors) "PositionSource is not an earlier node on this path: $($node.Path)"
    Check (!$actionStarted -or $kind -eq 'Action') "Condition after action: $($node.Path)"
    if ($kind -eq 'WithinDistance') {
        $distance = Resolve-Ref (Field $node.Text 'Distance') $node.Path -Required
        $distanceText = Get-Content -LiteralPath $index[$distance] -Raw
        $number = 0.0
        Check ([double]::TryParse((Field $distanceText 'MaxDistance'), [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -and ![double]::IsNaN($number) -and ![double]::IsInfinity($number) -and $number -ge 0) "Invalid distance: $($index[$distance])"
        if ($node.Path -match 'Ignite') { Check ($distance -eq $pickDistance) "Ignite must share Pick distance: $($node.Path)" }
        $hasRange = $true
    }
    if ($kind -eq 'OtherHandItem') { $null = Resolve-Ref (Field $node.Text 'HandCondition') $node.Path -Required }
    if ($kind -eq 'Action') { $null = Resolve-Ref (Field $node.Text 'Action') $node.Path -Required }
    $next = Resolve-Ref (Field $node.Text 'Next') $node.Path
    $edges = @(); if ($next) { $edges += $next }
    $rows = @([regex]::Matches($node.Text, '(?m)^  - Item:\s*(\{[^}\r\n]+\})\r?\n    Output:\s*(\{[^}\r\n]+\})'))
    $rowCount = [regex]::Matches($node.Text, '(?m)^  - Item:').Count
    Check ($rows.Count -eq $rowCount) "Malformed ItemMatches row: $($node.Path)"
    if ($kind -eq 'ReplaceableWorldItemTarget') {
        Check ($rows.Count -gt 0 -and !$next) "Match needs row outputs and no fallback Next: $($node.Path)"
        Check ($hasRange) "Range must precede world-item match: $($node.Path)"
    } else { Check ($rows.Count -eq 0) "ItemMatches on non-match node: $($node.Path)" }
    $items = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($row in $rows) {
        $item = Resolve-Ref $row.Groups[1].Value $node.Path -Required
        Check ($items.Add($item)) "Duplicate match item: $($node.Path)"
        $edges += Resolve-Ref $row.Groups[2].Value $node.Path -Required
    }
    $started = $actionStarted -or $kind -eq 'Action'
    if (!$edges.Count) { Check $started "Success path has no action: $($node.Path)" }
    foreach ($edge in $edges) { Visit $edge (@($ancestors) + $guid) $started $hasRange }
}
foreach ($skill in $skills) {
    Check ($ids.Add((Field $skill.Text 'Id'))) "Duplicate skill Id: $($skill.Path)"
    Check ((Field $skill.Text 'UseChain') -eq '1' -and (Field $skill.Text 'Conditions') -eq '[]' -and (Field $skill.Text 'Actions') -eq '[]') "Legacy skill configuration: $($skill.Path)"
    $first = Resolve-Ref (Field $skill.Text 'FirstNode') $skill.Path -Required
    Visit $first @() $false $false
    Write-Output "PASS graph: $([IO.Path]::GetRelativePath($root, $skill.Path))"
}
Check ($seen.Count -eq $nodes.Count) "Orphan chain nodes: $(($nodes.Keys | Where-Object { !$seen.Contains($_) } | ForEach-Object { $nodes[$_].Path }) -join ', ')"
Check ($replacements.Count -gt 0) 'No generic ReplaceWorldItem assets found'
foreach ($asset in $replacements) {
    $fields = @([regex]::Matches($asset.Text, '(?m)^  ([A-Za-z_]\w*):') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notlike 'm_*' })
    Check ($fields.Count -eq 2 -and $fields -contains 'ReplacementItem' -and $fields -contains 'Count') "ReplaceWorldItem must contain ReplacementItem and Count: $($asset.Path)"
    Check ([int](Field $asset.Text 'Count') -gt 0) 'Replacement count must be positive'
    $null = Resolve-Ref (Field $asset.Text 'ReplacementItem') $asset.Path -Required
}
$replaceSource = Read-Asset 'Scripts/Items/Skills/Configs/ReplaceWorldItemAction.cs'
$publicFields = @([regex]::Matches($replaceSource, '\bpublic\s+[\w<>]+\s+(\w+)\s*(?:=(?!>)[^;]*)?;') | ForEach-Object { $_.Groups[1].Value })
Check ($publicFields.Count -eq 2 -and $publicFields -contains 'ReplacementItem' -and $publicFields -contains 'Count') 'Generic replacement exposes unexpected configuration fields'
Check ($replaceSource -notmatch '\b(CursorWorldUtility|CursorWorldPosition|ActionDistanceDefinition|FindAt|WorldItemMatchUtility)\b|\bDistance\s*\.|\bFind\s*\(') 'Generic replacement contains cursor/range/search logic'

if (!$SkipOutputName) {
    # Extract the actual method, not a reimplementation. Adapters replace Unity objects only.
    $method = [regex]::Match($enumSource, '(?s)public static string OutputName\(SkillChainNode node\)\s*\{.*?(?=\s*public static bool IsVisible)')
    Check $method.Success 'Cannot extract actual OutputName method'
    $adapter = @'
using System;
namespace SkillDataGraphHeadless {
public enum SkillChainNodeKind { ENUM_BODY }
public class ItemSkillAction {}
public class ReplaceWorldItemAction : ItemSkillAction {}
public class ReplaceEquippedItemAction : ItemSkillAction {}
public class TogglePlacementShapeAction : ItemSkillAction {}
public class SkillChainNode { public SkillChainNodeKind Kind; public SkillChainNode PositionSource; public ItemSkillAction Action; }
public static class Checks {
METHOD_BODY
static void Expect(SkillChainNode n, string expected) { if(OutputName(n)!=expected) throw new Exception("OutputName: expected "+expected+" for "+n.Kind); }
public static void Run() {
foreach(SkillChainNodeKind k in Enum.GetValues(typeof(SkillChainNodeKind))) {
 var n=new SkillChainNode{Kind=k};
 Expect(n,k==SkillChainNodeKind.GroundItemTarget||k==SkillChainNodeKind.ReplaceableWorldItemTarget?"World Item":k==SkillChainNodeKind.OtherHandItem?"Item":"Position");
}
foreach(var a in new ItemSkillAction[]{new ReplaceWorldItemAction(),new ReplaceEquippedItemAction(),new TogglePlacementShapeAction(),new ItemSkillAction()}) {
 var n=new SkillChainNode{Kind=SkillChainNodeKind.Action,Action=a};
 Expect(n,a is ReplaceWorldItemAction?"World Item":a is ReplaceEquippedItemAction||a is TogglePlacementShapeAction?"Item":"Position");
 var range=new SkillChainNode{Kind=SkillChainNodeKind.WithinDistance,PositionSource=n};
 Expect(range,a is ReplaceWorldItemAction?"World Item":"Position");
}
foreach(var k in new[]{SkillChainNodeKind.GroundItemTarget,SkillChainNodeKind.ReplaceableWorldItemTarget,SkillChainNodeKind.TopSurfacePosition}) {
 var n=new SkillChainNode{Kind=k};
 for(int i=0;i<63;i++) n=new SkillChainNode{Kind=SkillChainNodeKind.WithinDistance,PositionSource=n};
 Expect(n,k==SkillChainNodeKind.TopSurfacePosition?"Position":"World Item");
 n=new SkillChainNode{Kind=SkillChainNodeKind.WithinDistance,PositionSource=n}; Expect(n,"Position");
}
var cycle=new SkillChainNode{Kind=SkillChainNodeKind.WithinDistance};cycle.PositionSource=cycle;Expect(cycle,"Position");
var stop=new SkillChainNode{Kind=SkillChainNodeKind.TopSurfacePosition,PositionSource=new SkillChainNode{Kind=SkillChainNodeKind.GroundItemTarget}};Expect(stop,"Position");
}
}
}
'@
    $code = $adapter.Replace('ENUM_BODY', $enumMatch.Groups[1].Value).Replace('METHOD_BODY', $method.Value)
    # OutputName uses no mathematics, so its extracted compilation needs no Unity DLL.
    if (!('SkillDataGraphHeadless.Checks' -as [type])) { Add-Type -TypeDefinition $code }
    [SkillDataGraphHeadless.Checks]::Run()
    Write-Output 'PASS actual extracted OutputName: every kind, action labels, nested ranges, 64-node boundary, cycles, source-stop semantics (Unity-object adapters).'
} else { Write-Output 'SKIP OutputName: explicitly requested.' }
Write-Output "PASS static asset validation: $($skills.Count) skills, $($seen.Count) nodes, Next and ItemMatches.Output paths, references, cycles, position ancestry, shared Pick distance, range-before-match, generic replacement. Unity gameplay was not executed."
