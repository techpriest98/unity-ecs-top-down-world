$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$draftPath=Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset'
$atlasPath=Join-Path $root 'Art/World/BlockAtlas.png'
$original=[IO.File]::ReadAllText($draftPath)
$match=[regex]::Match($original,'(?ms)^  - Id: 9\r?\n.*?(?=^  - Id:|\z)')
if(!$match.Success){throw 'Turf ID 9 missing.'}
$parts=$match.Value-split '    NormalPixels:'
if($parts.Count-ne 2){throw 'Unexpected draft serialization.'}
[uint32[]]$old=@([regex]::Matches($parts[0],'rgba: (\d+)')|ForEach-Object{[uint32]$_.Groups[1].Value})
if($old.Length-ne 3072){throw 'Expected 64x48 block.'}
$layout=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.cs') -Raw
$elements=@{}
foreach($m in [regex]::Matches($layout,'new AtlasElement\("([^"]+)",\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)')){
 $elements[$m.Groups[1].Value]=@{X=[int]$m.Groups[2].Value;Y=[int]$m.Groups[3].Value;W=[int]$m.Groups[4].Value;H=[int]$m.Groups[5].Value;DX=[int]$m.Groups[6].Value;DY=[int]$m.Groups[7].Value}
}
$top=$elements['Top'];$foot=$elements['Side / Bottom']
if($top.W-ne 32-or $top.H-ne 16-or $foot.W-ne 32-or $foot.H-ne 4){throw 'Review changed layout.'}
$db=Get-Content (Join-Path $root 'Configs/BlockDatabase.asset') -Raw
if($db-notmatch 'BlockId: 9\r?\n    AtlasColumn: 0\r?\n    AtlasRow: 0'){throw 'Review atlas mapping.'}
$backup=Join-Path $root 'Docs/Backups/TurfBeforeGrassFootOverlay'
if(Test-Path (Join-Path $backup 'BlockAtlasDraft.asset.bak')){throw 'Backup exists; refusing to overwrite later painting.'}
$next=[uint32[]]$old.Clone();$target=[bool[]]::new(3072);$grass=0
function FootColor([int]$x,[int]$y,[uint32]$alpha){
 # A foreground Top continues upward onto the rear wall through its BACK rows.
 [uint32](($old[($top.Y+$y)*64+$top.X+$x]-band 16777215)-bor ([uint64]$alpha-shl 24))
}
for($y=0;$y-lt 4;$y++){for($x=0;$x-lt 32;$x++){
 $i=($foot.Y+$y)*64+$foot.X+$x;$target[$i]=$true
 if(($old[$i]-shr 24)-ne 0){$next[$i]=FootColor $x $y ($old[$i]-shr 24);$grass++}
}}
foreach($name in 'Side / Bottom Left','Side / Bottom Right'){
 $corner=$elements[$name]
 for($y=0;$y-lt $corner.H;$y++){for($x=0;$x-lt $corner.W;$x++){
  $i=($corner.Y+$y)*64+$corner.X+$x;$mx=$corner.DX+$x-$foot.DX;$my=$corner.DY+$y-$foot.DY
  $edge=($foot.Y+$my)*64+$foot.X+$mx
  # Grass takes precedence only where the foot strip exists; lateral dirt
  # outside that strip remains the original side-edge art.
  if(($old[$edge]-shr 24)-ne 0-and ($old[$i]-shr 24)-ne 0){
   $target[$i]=$true;$next[$i]=FootColor $mx $my ($old[$i]-shr 24)
  }
 }}
}
for($i=0;$i-lt 3072;$i++){
 if(($old[$i]-shr 24)-ne ($next[$i]-shr 24)){throw 'Alpha changed.'}
 if(!$target[$i]-and $old[$i]-ne $next[$i]){throw 'Unrelated texture element changed.'}
}
$script:pixelIndex=0
$colorText=[regex]::Replace($parts[0],'rgba: (\d+)',{param($m) $p=$next[$script:pixelIndex];$script:pixelIndex++;'rgba: '+$p})
$newEntry=$colorText+'    NormalPixels:'+$parts[1]
$updated=$original.Substring(0,$match.Index)+$newEntry+$original.Substring($match.Index+$match.Length)
if(($newEntry-split '    NormalPixels:')[1]-cne $parts[1]){throw 'Normals changed.'}
if($updated.Replace($newEntry,'')-cne $original.Replace($match.Value,'')){throw 'Other block entries changed.'}
function PixelColor([uint32]$p){[Drawing.Color]::FromArgb([int]($p-shr 24),[int]($p-band 255),[int](($p-shr 8)-band 255),[int](($p-shr 16)-band 255))}
$bitmap=[Drawing.Bitmap]::new($atlasPath);$before=[int[]]::new($bitmap.Width*$bitmap.Height)
try{
 for($y=0;$y-lt $bitmap.Height;$y++){for($x=0;$x-lt $bitmap.Width;$x++){$before[$y*$bitmap.Width+$x]=$bitmap.GetPixel($x,$y).ToArgb()}}
 for($y=0;$y-lt 48;$y++){for($x=0;$x-lt 64;$x++){if($target[$y*64+$x]){$bitmap.SetPixel($x,$y,(PixelColor $next[$y*64+$x]))}}}
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
 try{for($y=0;$y-lt $check.Height;$y++){for($x=0;$x-lt $check.Width;$x++){
  $inEdit=$x-lt 64-and $y-lt 48-and $target[$y*64+$x]
  $expected=if($inEdit){(PixelColor $next[$y*64+$x]).ToArgb()}else{$before[$y*$check.Width+$x]}
  if($check.GetPixel($x,$y).ToArgb()-ne $expected){throw "PNG mismatch at $x,$y"}
 }}}finally{$check.Dispose()}
}catch{
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlasDraft.asset.bak') -Destination $draftPath -Force
 Copy-Item -LiteralPath (Join-Path $backup 'BlockAtlas.png.bak') -Destination $atlasPath -Force
 throw
}
Write-Output "Grass foot PASS: $grass visible strip pixels copy Top back rows exactly, two corner intersections follow; all alpha, normals, base art and other overlays/blocks preserved; runtime/draft parity verified."
