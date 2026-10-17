param([string]$GeneratedImage)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$draftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$atlasPath=Join-Path $root 'Art/World/BlockAtlas.png'
$text=[IO.File]::ReadAllText($draftPath)
$match=[regex]::Match($text,'(?ms)^  - Id: 9\r?\n.*?(?=^  - Id:|\z)')
if(!$match.Success){throw 'Turf ID 9 missing.'}
$parts=$match.Value-split '    NormalPixels:'
[uint32[]]$pixels=@([regex]::Matches($parts[0],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
if($pixels.Length-ne 3072){throw 'Expected 64x48 block.'}
function PixelColor([uint32]$p){[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))}
if(!$GeneratedImage){
 $preview=[Drawing.Bitmap]::new(512,512)
 try{
  for($y=0;$y-lt 32;$y++){for($x=0;$x-lt 32;$x++){
   $c=PixelColor $pixels[($y+16)*64+$x]
   for($dy=0;$dy-lt 16;$dy++){for($dx=0;$dx-lt 16;$dx++){$preview.SetPixel($x*16+$dx,$y*16+$dy,$c)}}
  }}
  $preview.Save((Join-Path $root '.codex-turf-side-reference.png'),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$preview.Dispose()}
 Write-Output 'Prepared .codex-turf-side-reference.png: existing SideUpper above current SideLower.'
 exit
}
$backup=Join-Path $root 'Docs/Backups/TurfBeforeSideLowerColor'
if(Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')){throw 'Backup exists; refusing to overwrite subsequent painting.'}
$db=[IO.File]::ReadAllText((Join-Path $root 'Configs/BlockDatabase.asset'))
if($db-notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0'){throw 'Review changed atlas mapping.'}
# Only colors already painted into SideUpper; exclude green vegetation and straw
# from the deeper soil layer, but keep its soil and grey inclusion colors.
$palette=@{}
for($y=16;$y-lt 32;$y++){for($x=0;$x-lt 32;$x++){
 $p=$pixels[$y*64+$x];$c=PixelColor $p
 if($c.A-eq 255 -and $c.G-le $c.R -or ($c.R-eq 112-and $c.G-eq 120-and $c.B-eq 112)){
  if($c.R-lt 150){$palette[$p]=$c}
 }
}}
if($palette.Count-lt 4){throw 'Not enough painted soil colors.'}
$next=[uint32[]]$pixels.Clone()
$source=[Drawing.Bitmap]::new((Resolve-Path -LiteralPath $GeneratedImage).Path)
try{
 if($source.Width-ne $source.Height){throw 'Expected square generated study; review sampling coordinates.'}
 for($y=0;$y-lt 16;$y++){for($x=0;$x-lt 32;$x++){
  # Sample the center of each target texel in the bottom half of the generated study.
  $sx=[Math]::Min($source.Width-1,[int][Math]::Floor(($x+.5)*$source.Width/32))
  $sy=[Math]::Min($source.Height-1,[int][Math]::Floor((16+$y+.5)*$source.Height/32))
  $c=$source.GetPixel($sx,$sy);$best=[double]::PositiveInfinity;$chosen=0u
  foreach($p in $palette.Keys){
   $q=$palette[$p];$distance=($c.R-$q.R)*($c.R-$q.R)+($c.G-$q.G)*($c.G-$q.G)+($c.B-$q.B)*($c.B-$q.B)
   if($distance-lt $best){$best=$distance;$chosen=$p}
  }
  $next[(32+$y)*64+$x]=$chosen
 }}
}finally{$source.Dispose()}
# Keep the authored boundary's material distribution; extend one texel of soil/cracks
# at the join, avoiding a different generated seam being pasted over the upper half.
for($x=0;$x-lt 32;$x++){
 $p=$pixels[31*64+$x]
 if($palette.ContainsKey($p)){$next[32*64+$x]=$p}
}
for($i=0;$i-lt 3072;$i++){
 $x=$i%64;$y=[int][Math]::Floor($i/64)
 if($x-lt 32-and $y-ge 32){if(!$palette.ContainsKey($next[$i])){throw 'New palette color introduced.'}}
 elseif($next[$i]-ne $pixels[$i]){throw 'Unrelated element changed.'}
}
$script:pixelIndex=0
$newColor=[regex]::Replace($parts[0],'rgba: (\d+)',{param($m) $p=$next[$script:pixelIndex];$script:pixelIndex++;'rgba: '+$p})
$newEntry=$newColor+'    NormalPixels:'+$parts[1]
$updated=$text.Substring(0,$match.Index)+$newEntry+$text.Substring($match.Index+$match.Length)
if(($newEntry-split '    NormalPixels:')[1]-cne $parts[1]){throw 'Normals changed.'}
if($updated.Replace($newEntry,'')-cne $text.Replace($match.Value,'')){throw 'Other draft entry changed.'}
$bitmap=[Drawing.Bitmap]::new($atlasPath)
$before=[int[]]::new($bitmap.Width*$bitmap.Height)
try{
 for($y=0;$y-lt $bitmap.Height;$y++){for($x=0;$x-lt $bitmap.Width;$x++){$before[$y*$bitmap.Width+$x]=$bitmap.GetPixel($x,$y).ToArgb()}}
 for($y=32;$y-lt 48;$y++){for($x=0;$x-lt 32;$x++){$bitmap.SetPixel($x,$y,(PixelColor $next[$y*64+$x]))}}
 $stream=[IO.MemoryStream]::new()
 try{$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$png=$stream.ToArray()}finally{$stream.Dispose()}
}finally{$bitmap.Dispose()}
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $draftPath -Destination (Join-Path $backup 'BlockAtlasDraft.asset.bak')
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $backup 'BlockAtlas.png.bak')
try{
 [IO.File]::WriteAllText($draftPath,$updated,[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllBytes($atlasPath,$png)
 $check=[Drawing.Bitmap]::new($atlasPath)
 try{
  for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
   $expected=if($x-lt 32-and $y-ge 32-and $y-lt 48){(PixelColor $next[$y*64+$x]).ToArgb()}else{$before[$y*$check.Width+$x]}
   if($check.GetPixel($x,$y).ToArgb()-ne $expected){throw "Unexpected atlas pixel at $x,$y"}
  }}
 }finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas.png.bak') -Destination $atlasPath -Force
 throw
}
Write-Output "SideLower PASS: 32x16, palette subset of SideUpper ($($palette.Count) allowed colors), exact boundary soil row, normals/other draft elements/other atlas pixels unchanged; PNG/draft parity verified. Backup: $backup"
