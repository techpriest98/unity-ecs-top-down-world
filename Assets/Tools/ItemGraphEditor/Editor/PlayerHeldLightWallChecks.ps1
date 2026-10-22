$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$production = (Get-Content (Join-Path $root 'Scripts/Player/Lighting/PlayerHeldLightWallUtility.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$fixtures = @'
using System;
using Unity.Mathematics;
using Game.World.Blocks;
using Game.World.Chunks;
namespace Game.World.Blocks {
 public enum BlockId {Air,Stone,OceanWater,RiverWater}
 public struct BlockPart { public BlockId Id; }
 public struct CellContents { public BlockPart Lower,Upper; }
 public static class BlockUtility { public static bool IsSolid(BlockId id)=>id!=BlockId.Air; }
}
namespace Game.World.Chunks {
 public static class ChunkSettings { public const int SizeY=256; }
 public struct ChunkBlockAccessor {
  public int WallX, Mode;
  public bool TryGetRawBlock(int2 c,int x,int y,int z,out int value) {value=0;return Mode!=5;}
  public CellContents GetContentsOrAir(int2 c,int x,int y,int z) {
   if(x!=WallX||y!=1||z!=0)return default;
   var id=Mode==4?BlockId.OceanWater:BlockId.Stone;
   return new CellContents {Lower=new BlockPart{Id=Mode==0||Mode==3?BlockId.Air:id},Upper=new BlockPart{Id=Mode==0||Mode==2?BlockId.Air:id}};
  }
 }
}
public static class HeldWallChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static string Run(){
  var a=new ChunkBlockAccessor{WallX=1,Mode=0};var feet=new float3(.5f,0,.5f);var desired=new float3(1.3f,1.25f,.5f);
  Check(math.distance(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a),desired)<.00001f,"Unobstructed anchor changed");
  a.Mode=1;var clamped=Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a);
  Check(math.abs(clamped.x-.96f)<.0001f&&clamped.y==desired.y,"Full wall margin incorrect");
  Check(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,new float3(1,1.25f,.5f),a).x<1,"Exact endpoint on wall not protected");
  a.Mode=2;
  Check(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a).x<1,"Lower half missed");
  var high=new float3(1.3f,1.75f,.5f);
  Check(math.distance(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,high,a),high)<.0001f,"Empty upper half blocked");
  a.Mode=3;
  Check(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,high,a).x<1,"Upper half missed");
  Check(math.distance(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a),desired)<.0001f,"Empty lower half blocked");
  a.Mode=4;Check(math.distance(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a),desired)<.0001f,"Water blocks light");
  a.Mode=1;a.WallX=-16;
  Check(Game.Player.PlayerHeldLightWallUtility.Clamp(new float3(-16.5f,0,.5f),2,new float3(-15.7f,1.25f,.5f),a).x < -16,"Negative boundary missed");
  Check(Game.Player.PlayerHeldLightWallUtility.Clamp(new float3(-14.5f,0,.5f),2,new float3(-15.5f,1.25f,.5f),a).x > -15,"Reverse ray missed");
  a.Mode=5;Check(Game.Player.PlayerHeldLightWallUtility.Clamp(feet,2,desired,a).x==feet.x,"Unloaded geometry did not fail closed");
  return "PASS actual held-light clamp: clear anchors, full/half walls, exact endpoints, water, reverse/negative boundaries and missing chunks.";
 }
}
'@
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
Add-Type -ReferencedAssemblies $refs -TypeDefinition ($fixtures + $production)
[HeldWallChecks]::Run()
