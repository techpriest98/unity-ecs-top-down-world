$ErrorActionPreference = 'Stop'
$streamAssets = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$streamMath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $streamMath
$streamReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $streamMath
$streamSource = Get-Content (Join-Path $streamAssets 'Scripts/World/Chunks/Components/ChunkStreamingRuntime.cs') -Raw
$streamSettings = Get-Content (Join-Path $streamAssets 'Scripts/World/Chunks/ChunkSettings.cs') -Raw
$streamHarness = @'
namespace Unity.Entities { public interface IComponentData {} }
public static class CameraStreamingChecks {
 static void Check(bool ok,string message) { if(!ok) throw new System.Exception(message); }
 public static string Run() {
  var offset=new Unity.Mathematics.float2(0,-2);
  int previous=0;
  for(int i=20;i<=100;i++) {
   float size=i*.1f;
   int radius=Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(size,16f/9f,offset,4);
   Check(radius>=previous,"Zooming out reduced radius");previous=radius;
   Check(radius*16>=size*16f/9f && radius*16>=(size+2+4)*2-.0001f,"Visible extent outside radius");
  }
  Check(Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(2,16f/9f,offset,4)==1,"Near zoom not reduced");
  Check(Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(10,16f/9f,offset,4)==2,"Far zoom incorrect");
  Check(Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(10,6,offset,4)==4,"Ultrawide viewport ignored");
  Check(Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(10,1,new Unity.Mathematics.float2(-60,0),0)==5,"Camera offset ignored");
  Check(Game.World.Chunks.CameraStreamingRadiusUtility.Calculate(10,1,offset,20)==4,"Relief allowance ignored");
  float delay=3;int current=3;
  current=Game.World.Chunks.CameraStreamingRadiusUtility.Update(current,1,2,ref delay);
  Check(current==3,"Chunks shrank immediately");
  current=Game.World.Chunks.CameraStreamingRadiusUtility.Update(current,1,0,ref delay);
  Check(current==3 && delay==1,"Paused delay advanced");
  current=Game.World.Chunks.CameraStreamingRadiusUtility.Update(current,1,1.1f,ref delay);
  Check(current==1,"Stable zoom did not shrink");
  current=Game.World.Chunks.CameraStreamingRadiusUtility.Update(current,4,0,ref delay);
  Check(current==4 && delay==3,"Zoom-out did not grow immediately");
  return "Camera streaming PASS: zoom extents, aspect, offset, relief, immediate growth, delayed shrink and paused timer. Unity lifecycle/visuals not tested.";
 }
}
'@
Add-Type -TypeDefinition ($streamSource + "`n" + $streamSettings + "`n" + $streamHarness) -ReferencedAssemblies $streamReferences
[CameraStreamingChecks]::Run()
