$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/World/Blocks/SplitCellUtility.cs') -Raw
$start = $source.IndexOf('    public static class WorldItemCollisionUtility')
$end = $source.IndexOf('    public static class SplitCellUtility', $start)
$collision = $source.Substring($start, $end-$start)
# Test-only counter around the extracted production RayBox body.
$collision = [regex]::Replace($collision, '(public static class WorldItemCollisionUtility\s*\{)', '$1 public static int BoxTests;')
$collision = [regex]::Replace($collision, '(private static bool RayBox\([^\{]+\{)', '$1 BoxTests++;')
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$adapter = @'
using Unity.Mathematics;
public enum BlockId { Air, Stone, Sand }
public struct BlockPart { public BlockId Id; public BlockPart(BlockId id,int d){Id=id;} }
public struct BlockData { public BlockId Id; }
public struct CellContents {
 public BlockPart Lower,Upper;
 public CellContents(BlockPart l,BlockPart u){Lower=l;Upper=u;}
 public static CellContents FromLegacy(BlockData b)=>new CellContents(new BlockPart(b.Id,1),new BlockPart(b.Id,1));
}
public static class BlockUtility { public static bool IsSolid(BlockId b)=>b!=BlockId.Air; }
public static class ChunkSettings { public const int SizeY=128; }
public static class SplitCellUtility { public static bool IsSplit(BlockData b)=>true; }
public struct ChunkBlockAccessor {
 public int3 Cell; public CellContents Contents; public bool Unloaded;
 public bool TryGetRawBlock(int2 c,int x,int y,int z,out BlockData b){b=default;return !Unloaded;}
 public CellContents GetContentsOrAir(int2 c,int x,int y,int z)=>math.all(Cell==new int3(x,y,z))?Contents:default;
}
'@
$tests = @'
public static class ImpactChecks {
 static void Check(bool ok,string s){if(!ok)throw new System.Exception(s);}
 public static void Run(){
  var origin=new int3(-17,40,-33);
  var a=new ChunkBlockAccessor {Cell=origin,Contents=new CellContents(new BlockPart(BlockId.Stone,1),new BlockPart(BlockId.Stone,1))};
  foreach(var axis in new[]{new float3(1,0,0),new float3(0,1,0),new float3(0,0,1)})
  foreach(int sign in new[]{-1,1}){
   float3 n=axis*sign, center=(float3)origin+.5f;
   Check(WorldItemCollisionUtility.Sweep(a,center+n*2,center,.04f,out var f,out var normal,out var id),"Loaded sweep failed");
   Check(f<1 && id==BlockId.Stone && math.all(normal==n),"Wrong material/normal for floor, ceiling or wall");
   float3 safe=math.lerp(center+n*2,center,f)+normal*.0002f;
   Check(math.dot(safe-center,n)>.54f,"Contact centre penetrates surface");
  }
  a.Contents=new CellContents(new BlockPart(BlockId.Stone,1),new BlockPart(BlockId.Sand,1));
  float3 above=(float3)origin+new float3(.5f,1.0402f,.5f);
  WorldItemCollisionUtility.BoxTests=0;
  Check(WorldItemCollisionUtility.ProbeSupport(a,above,.04f,out bool supported)&&supported,"Mixed support lost");
  Check(WorldItemCollisionUtility.BoxTests==1,"Support must use one geometry box, not material halves");
  foreach(int half in new[]{0,1}){
   float3 p=(float3)origin+new float3(.5f,half*.5f+.25f,.5f);
   Check(WorldItemCollisionUtility.Sweep(a,p+new float3(2,0,0),p,.04f,out var f,out var n,out var id)&&f<1,"Half collision missing");
   Check(id==(half==0?BlockId.Stone:BlockId.Sand),"Wrong mixed-half material");
  }
  a.Contents=new CellContents(new BlockPart(BlockId.Stone,1),new BlockPart(BlockId.Stone,1));
  WorldItemCollisionUtility.BoxTests=0;
  Check(WorldItemCollisionUtility.Sweep(a,above,above-new float3(0,.02f,0),.04f,out _,out _,out var stone)&&stone==BlockId.Stone,"Uniform material lost");
  Check(WorldItemCollisionUtility.BoxTests==1,"Uniform collision must use one box");
  a.Unloaded=true;
  Check(!WorldItemCollisionUtility.Sweep(a,(float3)origin+2,(float3)origin,.04f,out _,out _,out _),"Unloaded terrain must pause");
 }
}
'@
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
Add-Type -TypeDefinition ($adapter+$collision+$tests) -ReferencedAssemblies $refs
[ImpactChecks]::Run()
'PASS actual Sweep/RayBox with terrain adapters: all six impact directions, contact clearance, negative coordinates, mixed-half material, unloaded terrain. ECS spawning requires Play Mode.'
