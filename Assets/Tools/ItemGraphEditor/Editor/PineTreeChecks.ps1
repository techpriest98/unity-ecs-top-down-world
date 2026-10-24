param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$math = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $math
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $math
$source = Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeModel.cs') -Raw
$source = $source -replace '(?m)^using [^;]+;\r?\n',''
$harness = @'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Game.World.Trees;
public static class PineTreeChecks {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static string Run(){
  var settings=new PineSettings();var model=new PineTreeModel(settings);var same=new PineTreeModel(settings);
  Check(model.Vertices.Count>300 && model.Vertices.Count%3==0,"Incomplete mesh");
  Check(model.Vertices.Count==same.Vertices.Count,"Seed changed topology");
  for(int i=0;i<model.Vertices.Count;i++){
   Check(math.all(math.isfinite(model.Vertices[i]))&&math.all(math.isfinite(model.Normals[i])),"Nonfinite geometry");
   Check(math.distance(model.Vertices[i],same.Vertices[i])==0,"Non deterministic seed");
   Check(math.abs(math.length(model.Normals[i])-1)<.001f,"Invalid normal");
  }
  settings.Seed++;var different=new PineTreeModel(settings);
  bool changed=model.Vertices.Count!=different.Vertices.Count;
  for(int i=0;i<Math.Min(model.Vertices.Count,different.Vertices.Count);i++)changed|=math.distance(model.Vertices[i],different.Vertices[i])>0;
  Check(changed,"Seed has no effect");
  var sparse=new PineTreeModel(new PineSettings{NeedlePairs=6});
  var dense=new PineTreeModel(new PineSettings{NeedlePairs=24});
  Check(dense.Vertices.Count>sparse.Vertices.Count,"Needle density does not affect planar geometry");
  var plain=new PineTreeModel(new PineSettings{BarkStrength=0});
  bool normalChanged=false,colorChanged=false;
  for(int sample=0;sample<100;sample++){
   float angle=sample*.71f;var n=new float3(math.cos(angle),0,math.sin(angle));
   var p=new float3(n.x*.23f,1+sample*.03f,n.z*.23f);var baseColor=new float3(.3f,.2f,.1f);
   float3 albedo,detail;model.SampleBark(p,n,baseColor,out albedo,out detail);
   Check(math.all(math.isfinite(detail))&&math.abs(math.length(detail)-1)<.001f,"Invalid bark normal");
   Check(math.all(albedo>=0)&&math.all(albedo<=1),"Invalid bark albedo");
   normalChanged|=math.distance(detail,n)>.01f;colorChanged|=math.distance(albedo,baseColor)>.01f;
   plain.SampleBark(p,n,baseColor,out albedo,out detail);
   Check(math.distance(albedo,baseColor)<.00001f&&math.distance(detail,n)<.00001f,"Zero bark strength changes the baseline");
  }
  Check(normalChanged&&colorChanged,"Bark must affect both diffuse and normals");
  float pivot;float scale=PineTreeRasterizer.Fit(model,256,384,out pivot);
  for(int view=0;view<4;view++){
   var pixels=PineTreeRasterizer.Render(model,view,256,384,scale,pivot,false,false);
   var normals=PineTreeRasterizer.Render(model,view,256,384,scale,pivot,true,false);
   int count=0;
   for(int i=0;i<256*384;i++){
    Check(pixels[i*4+3]==normals[i*4+3],"Color/normal silhouette mismatch");
    if(pixels[i*4+3]>0){count++;Check(i%256>0&&i%256<255&&i/256>0&&i/256<383,"Clipped export");}
   }
   Check(count>1000,"Empty view");
   foreach(var p in model.Vertices){var q=PineTreeRasterizer.Project(p,view)*scale+new float2(128,pivot);Check(q.x>=7&&q.x<=249&&q.y>=7&&q.y<=377,"Shared frame bounds invalid");}
  }
  return "PASS actual 3D generator/rasterizer: deterministic seed, finite/unit geometry, four matching color/normal silhouettes, shared pivot/scale and unclipped bounds.";
 }
 public static byte[] Preview(){
  var model=new PineTreeModel(new PineSettings());float pivot;float scale=PineTreeRasterizer.Fit(model,256,384,out pivot);
  var output=new byte[1024*384*4];
  for(int view=0;view<4;view++){
   var image=PineTreeRasterizer.Render(model,view,256,384,scale,pivot,false,false);
   for(int y=0;y<384;y++)Array.Copy(image,y*256*4,output,(y*1024+view*256)*4,256*4);
  }
  return output;
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition ($harness + $source)
[PineTreeChecks]::Run()
if ($PreviewPath) {
    Add-Type -AssemblyName System.Drawing
    $pixels = [PineTreeChecks]::Preview()
    $bitmap = [Drawing.Bitmap]::new(1024,384)
    try {
        for ($y=0; $y -lt 384; $y++) { for ($x=0; $x -lt 1024; $x++) {
            $i=($y*1024+$x)*4
            $color=if($pixels[$i+3]){[Drawing.Color]::FromArgb(255,$pixels[$i],$pixels[$i+1],$pixels[$i+2])}else{[Drawing.Color]::FromArgb(255,75,80,83)}
            $bitmap.SetPixel($x,383-$y,$color)
        }}
        $bitmap.Save($PreviewPath,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}
