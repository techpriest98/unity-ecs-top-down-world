param(
 [string]$SourceName='sprite_n.png',
 [string]$OutputName='sprite_n_palette.png'
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$destination=Join-Path $folder $OutputName
if(Test-Path $destination){throw 'Output already exists; refusing to overwrite artwork'}
[uint32[]]$palette=& (Join-Path $PSScriptRoot 'NormalizeTurfNormalPalette.ps1') -PaletteOnly
$source=[Drawing.Bitmap]::new((Join-Path $folder $SourceName))
$output=[Drawing.Bitmap]::new($source.Width,$source.Height)
$used=[Collections.Generic.HashSet[uint32]]::new()
try{
 for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
  $c=$source.GetPixel($x,$y)
  [uint32]$rgb=[uint32]$c.R -bor ([uint32]$c.G -shl 8) -bor ([uint32]$c.B -shl 16)
  # Choose by decoded normal angular distance, not RGB brightness.
  $rgb=[TurfPaletteRestriction]::Snap($rgb,$palette)
  if($rgb -notin $palette){throw 'Non-palette output'}
  $null=$used.Add($rgb)
  $output.SetPixel($x,$y,[Drawing.Color]::FromArgb($c.A,($rgb-band 255),(($rgb-shr 8)-band 255),(($rgb-shr 16)-band 255)))
 }}
 $output.Save($destination,[Drawing.Imaging.ImageFormat]::Png)
 $saved=[Drawing.Bitmap]::new($destination)
 try{
  if($saved.Size -ne $source.Size){throw 'Dimensions changed'}
  for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){
   $a=$source.GetPixel($x,$y);$b=$saved.GetPixel($x,$y)
   if($a.A -ne $b.A){throw 'Alpha changed'}
   [uint32]$rgb=[uint32]$b.R -bor ([uint32]$b.G -shl 8) -bor ([uint32]$b.B -shl 16)
   if($rgb -notin $palette){throw 'Saved PNG contains non-palette RGB'}
  }}
 }finally{$saved.Dispose()}
 "PASS saved PNG: $($source.Width)x$($source.Height), $($used.Count) exact picker colours, original alpha at every pixel. Source unchanged."
}finally{$source.Dispose();$output.Dispose()}
