$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$shader=Get-Content (Join-Path $root 'Shaders/ChunkBorders.hlsl') -Raw
$function=[regex]::Match($shader,'(?s)float2 ChunkBorderCoordinates\(.*?\n\}').Value
if(!$function){throw 'Production border coordinate helper missing'}
$function=$function -replace '^float2 ChunkBorderCoordinates','static float2 ChunkBorderCoordinates'
$function=$function -replace '\bfloat2\(', 'new float2('
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$source=@'
using System;
using Unity.Mathematics;
using static Unity.Mathematics.math;
public static class ChunkBorderChecks {
 static float4 _ChunkBorderFacing;
'@+$function+@'
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static string Run(){
  foreach(var toward in new[]{new float2(0,1),new float2(-1,0),new float2(0,-1),new float2(1,0)}){
   _ChunkBorderFacing=new float4(toward.x,0,toward.y,0);
   var right=new float2(toward.y,-toward.x);
   for(int x=0;x<16;x++)for(int z=0;z<16;z++){
    uint packed=(uint)((73<<8)|(z<<4)|x)<<16;
    float expectedU=math.dot(new float2(x,z),right)+(math.cmin(right)<0?15:0);
    float expectedV=math.dot(new float2(x,z),toward)+(math.cmin(toward)<0?15:0);
    var top=ChunkBorderCoordinates(packed,new float2(0,1),0);
    var bottom=ChunkBorderCoordinates(packed,new float2(1,0),0);
    Check(math.distance(top,new float2(expectedU,expectedV))<.00001f,"View rotation misaligned top chunk border");
    Check(math.distance(bottom,new float2(expectedU+1,expectedV+1))<.00001f,"Corner orientation inconsistent");
    Check(math.all(top>=0)&&math.all(bottom<=16),"Border coordinates escaped chunk");
    var side=ChunkBorderCoordinates(packed,new float2(1,0),1);
    Check(side.x==expectedU+1&&side.y==expectedV,"Side stripes do not align with tops");
   }
  }
  return "PASS actual shader chunk coordinates: all 256 cells, four camera directions, corner orientation and side/top alignment.";
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source
[ChunkBorderChecks]::Run()
$ui=Get-Content (Join-Path $root 'Scripts/UI/FpsCounter.cs') -Raw
$renderer=Get-Content (Join-Path $root 'Scripts/World/Rendering/ChunkProceduralRenderer.cs') -Raw
if($ui -notmatch 'keyboard.f9Key.wasPressedThisFrame' -or
 $ui.IndexOf('keyboard.f9Key.wasPressedThisFrame') -gt $ui.IndexOf('if (overlay == null || !overlay.enabled) return;') -or
 $ui -notmatch 'Chunk Borders: On' -or $ui -notmatch 'Chunk Borders: Off' -or
 $renderer -notmatch 'SetFloat\("_ChunkBordersEnabled", ChunkBorderDebug.Enabled' -or
 $shader -notmatch 'if \(_ChunkBordersEnabled < .5\) return color'){
 throw 'Borders must toggle independently of overlay visibility and publish state without chunk rebuild'
}
foreach($name in 'ChunkProcedural','ChunkWaterProcedural'){
 $terrain=Get-Content (Join-Path $root "Shaders/$name.shader") -Raw
 if($terrain -notmatch 'ChunkBorderCoordinates\(cell.FaceData,corner,' -or $terrain -notmatch 'ApplyChunkBorders\(color.rgb,input.ChunkBorderUv,input.FaceIndex\)'){
  throw "Missing border integration in $name"
 }
}
'PASS F9 independent toggle, F3 state, shared terrain/water integration and disabled shader fast path. Unity visual/input checks remain manual.'
