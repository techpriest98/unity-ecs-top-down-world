# Deterministic normal-vector migration, not a color-image filter.
# Close/suspend painting in Block Atlas before running. Backup guard prevents double rotation.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$normalRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$normalDraftPath = Join-Path $normalRoot 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$normalAtlasPath = Join-Path $normalRoot 'Art/World/BlockAtlas_Normal.png'
$normalBackup = Join-Path $normalRoot 'Docs/Backups/StoneSandNormalsBeforeConversion'
if (Test-Path (Join-Path $normalBackup 'BlockAtlasDraft.asset.bak')) { throw 'Backup already exists: refusing to rotate normals twice.' }

function Convert-NormalVector([uint32] $packed) {
    $alpha = $packed -shr 24
    if ($alpha -eq 0) { return $packed }
    $vx = ($packed -band 255) / 127.5 - 1
    $vy = (($packed -shr 8) -band 255) / 127.5 - 1
    $vz = (($packed -shr 16) -band 255) / 127.5 - 1
    $nextY = ($vy + $vz) / [Math]::Sqrt(2)
    $nextZ = ($vz - $vy) / [Math]::Sqrt(2)
    $length = [Math]::Sqrt($vx*$vx + $nextY*$nextY + $nextZ*$nextZ)
    if ($length -lt .00001) { return $packed }
    [uint64] $red = [Math]::Clamp([Math]::Round(($vx/$length*.5+.5)*255), 0, 255)
    [uint64] $green = [Math]::Clamp([Math]::Round(($nextY/$length*.5+.5)*255), 0, 255)
    [uint64] $blue = [Math]::Clamp([Math]::Round(($nextZ/$length*.5+.5)*255), 0, 255)
    return [uint32]($red -bor ($green -shl 8) -bor ($blue -shl 16) -bor ([uint64]$alpha -shl 24))
}

$normalOriginalText = [IO.File]::ReadAllText($normalDraftPath)
$normalCounts = @{}
$normalConvertedText = [regex]::Replace($normalOriginalText, '(?ms)^  - Id: (\d+)\r?\n.*?(?=^  - Id:|\z)', {
    param($entry)
    $id = $entry.Groups[1].Value
    if ($id -notin @('2','3')) { return $entry.Value }
    return [regex]::Replace($entry.Value, '(?ms)^    NormalPixels:.*$', {
        param($normalPart)
        return [regex]::Replace($normalPart.Value, 'rgba: (\d+)', {
            param($pixel)
            [uint32] $old = $pixel.Groups[1].Value
            $converted = Convert-NormalVector $old
            if ($old -ne $converted) { $normalCounts[$id] = 1 + $normalCounts[$id] }
            return 'rgba: ' + $converted
        })
    })
})
if (!$normalCounts.ContainsKey('2') -or !$normalCounts.ContainsKey('3')) { throw 'Stone or sand normal pixels were not found.' }
# The exporter flips CPU rows before Texture2D.SetPixels32, so PNG rectangles
# are top-down just like the draft. Runtime DB maps Stone=(2,0), Sand=(3,0).
$normalDatabase = [IO.File]::ReadAllText((Join-Path $normalRoot 'Configs/BlockDatabase.asset'))
foreach ($id in @(2,3)) {
    if ($normalDatabase -notmatch "BlockId: $id\r?\n    AtlasColumn: $id\r?\n    AtlasRow: 0") { throw 'Runtime atlas mapping changed; review rectangles before conversion.' }
}
$normalBitmap = [Drawing.Bitmap]::new($normalAtlasPath)
$normalExpected = @{}
$normalPngBytes = $null
try {
    if ($normalBitmap.Width -lt 256 -or $normalBitmap.Height -lt 48) { throw 'Unexpected atlas dimensions.' }
    # Store every original pixel to verify unrelated blocks/transparent pixels.
    for ($y=0; $y -lt $normalBitmap.Height; $y++) {
        for ($x=0; $x -lt $normalBitmap.Width; $x++) {
            $color = $normalBitmap.GetPixel($x,$y)
            [uint32] $packed = [uint32]([uint64]$color.R -bor ([uint64]$color.G -shl 8) -bor ([uint64]$color.B -shl 16) -bor ([uint64]$color.A -shl 24))
            if ($y -lt 48 -and $x -ge 128 -and $x -lt 256) { $packed = Convert-NormalVector $packed }
            $normalExpected[$x+$y*$normalBitmap.Width] = $packed
            $normalBitmap.SetPixel($x,$y,[Drawing.Color]::FromArgb([int]($packed -shr 24),[int]($packed -band 255),[int](($packed -shr 8) -band 255),[int](($packed -shr 16) -band 255)))
        }
    }
    $normalMemory = [IO.MemoryStream]::new()
    try { $normalBitmap.Save($normalMemory,[Drawing.Imaging.ImageFormat]::Png); $normalPngBytes=$normalMemory.ToArray() }
    finally { $normalMemory.Dispose() }
} finally { $normalBitmap.Dispose() }

# Numeric sanity checks on the three central picker entries.
foreach ($pair in @(@(128,37,218,128,128,255),@(128,128,255,128,218,218),@(128,218,218,128,255,128))) {
    [uint32] $inputPixel = [uint32]([uint64]$pair[0] -bor ([uint64]$pair[1] -shl 8) -bor ([uint64]$pair[2] -shl 16) -bor (255L -shl 24))
    $outputPixel = Convert-NormalVector $inputPixel
    if ([Math]::Abs([int]($outputPixel-band 255)-$pair[3]) -gt 1 -or
        [Math]::Abs([int](($outputPixel-shr 8)-band 255)-$pair[4]) -gt 1 -or
        [Math]::Abs([int](($outputPixel-shr 16)-band 255)-$pair[5]) -gt 1) { throw 'Vector conversion failed sanity check.' }
}
New-Item -ItemType Directory -Path $normalBackup -Force | Out-Null
Copy-Item -LiteralPath $normalDraftPath -Destination (Join-Path $normalBackup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $normalAtlasPath -Destination (Join-Path $normalBackup 'BlockAtlas_Normal.png.bak')
try {
    [IO.File]::WriteAllText($normalDraftPath,$normalConvertedText,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes($normalAtlasPath,$normalPngBytes)
    $normalCheck = [Drawing.Bitmap]::new($normalAtlasPath)
    try {
        for ($y=0; $y -lt $normalCheck.Height; $y++) { for ($x=0; $x -lt $normalCheck.Width; $x++) {
            $c=$normalCheck.GetPixel($x,$y)
            [uint32] $actual=[uint32]([uint64]$c.R -bor ([uint64]$c.G -shl 8) -bor ([uint64]$c.B -shl 16) -bor ([uint64]$c.A -shl 24))
            if ($actual -ne $normalExpected[$x+$y*$normalCheck.Width]) { throw 'PNG round-trip changed an unexpected pixel.' }
        }}
    } finally { $normalCheck.Dispose() }
} catch {
    Copy-Item -LiteralPath (Join-Path $normalBackup 'BlockAtlasDraft.asset.bak') -Destination $normalDraftPath -Force
    Copy-Item -LiteralPath (Join-Path $normalBackup 'BlockAtlas_Normal.png.bak') -Destination $normalAtlasPath -Force
    throw
}
Write-Output "Converted draft normals: Stone=$($normalCounts['2']), Sand=$($normalCounts['3']). Runtime normal atlas verified pixel-for-pixel. Backups: $normalBackup"
