$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$text=Get-Content (Join-Path $root 'Scripts/Player/Lighting/PlayerLightingSystem.cs') -Raw
$start=$text.IndexOf('        private static bool IsPlayerLightVisible(')
$end=$text.IndexOf('        private void UpdateChunkLookup', $start)
if($start -lt 0 -or $end -lt 0){throw 'Production visibility helper not found'}
$method=$text.Substring($start,$end-$start)
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$source=@'
using System;
using Unity.Mathematics;
struct ChunkBlockAccessor {}
static class DynamicLightVisibilityUtility {
 public static int Mode,Calls;public static int3 Last;
 public static bool IsVisible(int2 chunk,int3 source,int3 target,ChunkBlockAccessor accessor){
  Calls++;Last=target;
  return Mode==1 || (Mode==2 && target.y==11);
 }
}
public static class PlayerVisibilityChecks {
 METHOD
 static void Check(bool v,string m){if(!v)throw new Exception(m);}
 static bool RunCase(int mode,float feet,float height){
  DynamicLightVisibilityUtility.Mode=mode;DynamicLightVisibilityUtility.Calls=0;
  return IsPlayerLightVisible(new int2(-1,2),new int3(1,2,3),4,5,feet,height,default);
 }
 public static void Run(){
  Check(RunCase(1,10,2),"Visible legs rejected");
  Check(DynamicLightVisibilityUtility.Calls==1,"Visible lower sample should short-circuit");
  Check(DynamicLightVisibilityUtility.Last.y==10,"Lower sample in wrong voxel");
  Check(RunCase(2,10,2),"Visible head rejected when legs blocked");
  Check(DynamicLightVisibilityUtility.Calls==2,"Expected head retry");
  Check(DynamicLightVisibilityUtility.Last.Equals(new int3(4,11,5)),"Head sample has wrong coordinates");
  Check(!RunCase(0,10,2),"Both blocked must reject light");
  Check(DynamicLightVisibilityUtility.Calls==2,"Both distinct samples must be tested");
  Check(!RunCase(0,10,.1f),"Blocked short body must reject light");
  Check(DynamicLightVisibilityUtility.Calls==1,"Duplicate voxel tested twice");
  Check(!RunCase(0,-2,2),"Blocked negative-height location accepted");
  Check(DynamicLightVisibilityUtility.Last.y==-1,"Negative world Y must use floor");
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition $source.Replace('METHOD',$method)
[PlayerVisibilityChecks]::Run()
'PASS production two-sample selection: visible legs/head, both blocked, duplicate voxel, negative world Y. Ray occlusion traversal is unchanged; scene validation remains in Unity.'
