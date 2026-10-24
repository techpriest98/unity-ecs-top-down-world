$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$math='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $math
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$math
$trace=Get-Content (Join-Path $root 'Shaders/TreeShadowTrace.hlsl') -Raw
$function=[regex]::Match($trace,'(?s)bool TreeVolumeHit\(.*?\n\}').Value
$function=$function.Replace('bool TreeVolumeHit','static bool TreeVolumeHit')
$function=$function -replace '\bfloat3\(', 'new float3('
$function=$function -replace '(?<![\w.])(\d*\.\d+)(?![\w.])','${1}f'
$localTrace=Get-Content (Join-Path $root 'Resources/PlayerLocalVisibility.compute') -Raw
$dda=[regex]::Match($localTrace,'(?s)float Trace\(float3 origin, float3 target\)\s*\{(.*?)\n\}').Groups[1].Value
$dda=$dda.Replace('[loop]','') -replace '\b(float3|int3)\(', 'new $1('
$dda=$dda -replace '(?<![\w.])(\d*\.\d+|\d+e\d+)(?![\w.])','${1}f'
$model=(Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeModel.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$harness=@'
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using static Unity.Mathematics.math;
using Game.World.Trees;
public static class TreeShadowChecks {
 static uint[] _PlayerLightOccupancy=new uint[40*80*40];
 static float4 _PlayerLightGridOrigin=new float4(0,0,0,40);
 static PineShadowVolume testVolume;
 static float TreeCellVisibility(int3 cell,int size,float3 origin,float3 target)=>TreeVolumeHit(origin,target,testVolume)?0:1;
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(){
  var trunk=new PineShadowVolume{A=new float4(0,0,0,.2f),B=new float4(0,4,0,1)};
  Check(TreeVolumeHit(new float3(-1,1,0),new float3(1,1,0),trunk),"Thin trunk missed");
  Check(TreeVolumeHit(new float3(1,1,0),new float3(-1,1,0),trunk),"Reverse ray missed");
  Check(!TreeVolumeHit(new float3(-1,1,.3f),new float3(1,1,.3f),trunk),"Trunk widened to full grid cell");
  Check(!TreeVolumeHit(new float3(0,1,0),new float3(1,1,0),trunk),"Self proxy blacked out bark");
  Check(!TreeVolumeHit(new float3(-1,1,0),new float3(-.5f,1,0),trunk),"Occluder beyond local source blocked light");
  Check(TreeVolumeHit(new float3(.1f,-1,0),new float3(.1f,5,0),trunk),"Parallel ray missed");
  var tuft=new PineShadowVolume{A=new float4(0,3,0,0),B=new float4(1,.5f,1,0)};
  Check(TreeVolumeHit(new float3(-2,3,0),new float3(2,3,0),tuft),"Foliage proxy missed");
  Check(!TreeVolumeHit(new float3(-2,4,0),new float3(2,4,0),tuft),"Gap above foliage blocked");
  testVolume=new PineShadowVolume{A=new float4(.5f,1,.5f,0),B=new float4(.1f,.1f,.1f,0)};
  Check(TraceLocal(new float3(.2f,1,.5f),new float3(.8f,1,.5f))==0,"A tree between receiver and source in the SAME air cell was skipped");
  Check(TraceLocal(new float3(.2f,1,.5f),new float3(.3f,1,.5f))==1,"A tree beyond the source blocked the same-cell ray");
  var settings=new PineSettings();var full=new PineTreeModel(settings);var shadow=new PineTreeModel(settings,true);
  Check(shadow.Vertices.Count==0&&shadow.ShadowVolumes.Count>100,"Shadow path built render triangles or lost foliage");
  Check(full.ShadowVolumes.Count==shadow.ShadowVolumes.Count,"Proxy-only path changed structure");
  for(int i=0;i<full.ShadowVolumes.Count;i++)Check(all(full.ShadowVolumes[i].A==shadow.ShadowVolumes[i].A)&&all(full.ShadowVolumes[i].B==shadow.ShadowVolumes[i].B),"Shadow-only seed diverged from rendered geometry");
 }
'@
$source=$harness+$function+"`n static float TraceLocal(float3 origin,float3 target) {`n"+$dda+"`n}`n}`n"+$model
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[TreeShadowChecks]::Run()
$grid=Get-Content (Join-Path $root 'Scripts/Player/Visual/PlayerLocalLightGrid.cs') -Raw
$spatial=$grid
if($grid -notmatch 'hash = HashTreeCasters' -or $grid -notmatch 'BuildTreeShadowGrid\(origin\)' -or
 $spatial -notmatch 'treeProxyCache.TryGetValue' -or $spatial -match 'GetBuffer<BlockData>|GetBuffer<SplitCellData>') {throw 'Tree invalidation/proxy cache missing or world block mutation introduced'}
'PASS actual HLSL capsule/foliage intersections, thin silhouettes, gaps, self-surface safety, finite segments, parallel rays and exact render/proxy seed parity without triangle generation.'
