param([string[]]$Names=@('body_idle','body_idle_normals','body_walk','body_walk_normals'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$folder=Join-Path $root 'Art/Player/Animations/Body'
$destination=Join-Path $root 'Docs/References/WalkTorsoNormals'
$null=New-Item -ItemType Directory -Force -Path $destination
foreach($name in $Names){
 $source=[Drawing.Bitmap]::new((Join-Path $folder ($name+'.png')))
 $columns=[int]($source.Width/64)
 $output=[Drawing.Bitmap]::new($columns*32*8,4*20*8)
 try{
  for($row=0;$row -lt 4;$row++){for($col=0;$col -lt $columns;$col++){
   for($y=0;$y -lt 160;$y++){for($x=0;$x -lt 256;$x++){
    $output.SetPixel($col*256+$x,$row*160+$y,$source.GetPixel($col*64+16+[int][Math]::Floor($x/8),$row*128+28+[int][Math]::Floor($y/8)))
   }}
  }}
  $output.Save((Join-Path $destination ($name+'_torso.png')),[Drawing.Imaging.ImageFormat]::Png)
 }finally{$source.Dispose();$output.Dispose()}
}
