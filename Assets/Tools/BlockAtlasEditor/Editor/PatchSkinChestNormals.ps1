param([string]$GeneratedPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$reference=Join-Path $root 'Docs/References/SkinChestNormals'
$null=New-Item -ItemType Directory -Force -Path $reference
$target=Join-Path $folder 'body_idle_normal_skin_draft.png'
if(-not $GeneratedPath){
 foreach($pair in @(@('body_idle.png','chest_diffuse.png'),@('body_idle_normal_skin_draft.png','chest_normals_before.png'))){
  $source=[Drawing.Bitmap]::new((Join-Path $folder $pair[0]))
  $crop=[Drawing.Bitmap]::new(448,256)
  try{for($y=0;$y -lt 256;$y++){for($x=0;$x -lt 448;$x++){
   $crop.SetPixel($x,$y,$source.GetPixel(20+[int][Math]::Floor($x/16),32+[int][Math]::Floor($y/16)))
  }};$crop.Save((Join-Path $reference $pair[1]),[Drawing.Imaging.ImageFormat]::Png)}finally{$source.Dispose();$crop.Dispose()}
 }
 return
}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$before=[Drawing.Bitmap]::new($target);$proposal=[Drawing.Bitmap]::new($GeneratedPath)
$result=[Drawing.Bitmap]::new($before.Width,$before.Height)
$changes=0
try{
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $old=$before.GetPixel($x,$y);$next=$old
  # Only the existing exposed chest alpha, below the beard in front idle.
  # Second idle frame's chest is shifted one pixel left in the source sheet.
  $column=[int][Math]::Floor($x/64);$localX=$x%64
  if($y -ge 34 -and $y -le 47 -and $old.A -gt 0){
   $patchX=$localX-20+$column
   if($patchX -ge 0 -and $patchX -lt 28){
    $sx=[Math]::Clamp([int][Math]::Floor(($patchX+.5)*$proposal.Width/28),0,$proposal.Width-1)
    $sy=[Math]::Clamp([int][Math]::Floor(($y-32+.5)*$proposal.Height/16),0,$proposal.Height-1)
    $c=$proposal.GetPixel($sx,$sy)
    if($c.A -gt 127){
     [uint32]$rgb=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
     $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
     $next=[Drawing.Color]::FromArgb($old.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255))
    }
   }
  }
  if($next.ToArgb() -ne $old.ToArgb()){$changes++}
  $result.SetPixel($x,$y,$next)
 }}
 if($changes -eq 0){throw 'No chest changes produced'}
 $backup=Join-Path $reference 'body_idle_normal_skin_before_chest.png'
 if(Test-Path $backup){throw 'Backup exists; refusing to replace a subsequent art revision'}
 Copy-Item -LiteralPath $target -Destination $backup
 $memory=[IO.MemoryStream]::new()
 try{$result.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 # Verify whole-sheet invariants before replacing the requested file.
 for($y=0;$y -lt 512;$y++){for($x=0;$x -lt 128;$x++){
  $a=$before.GetPixel($x,$y);$b=$result.GetPixel($x,$y)
  if($a.A -ne $b.A){throw 'Alpha changed'}
  if(($y -lt 34 -or $y -gt 47 -or $a.A -eq 0) -and $a.ToArgb() -ne $b.ToArgb()){throw 'Changed outside front chest'}
 }}
 $before.Dispose()
 [IO.File]::WriteAllBytes($target,$bytes)
 "PASS: $changes changed chest pixels; all other RGBA and all alpha unchanged. Backup: $backup"
}finally{$before.Dispose();$proposal.Dispose();$result.Dispose()}
