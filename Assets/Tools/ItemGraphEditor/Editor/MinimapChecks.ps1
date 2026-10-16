# Coordinate checks compile the actual minimap metadata and transform methods.
# ECS attributes are adapters; this does not run Unity UI or GPU rendering.
$ErrorActionPreference = 'Stop'
$mapAssets = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mapMath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mapMath
$mapReferences = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mapMath
$mapSource = Get-Content -LiteralPath (Join-Path $mapAssets 'Scripts/World/Generation/WorldBiomeMap.cs') -Raw
$mapMetadata = ($mapSource -split '    public struct WorldBiomeMap : IDisposable', 2)[0] + "`n}"
$mapBiome = Get-Content -LiteralPath (Join-Path $mapAssets 'Scripts/World/Generation/WorldBiome.cs') -Raw
$mapChecks = @'
namespace Unity.Collections { }
namespace Unity.Entities {
 public interface IComponentData { }
 public interface IBufferElementData { }
 [System.AttributeUsage(System.AttributeTargets.Struct)]
 public class InternalBufferCapacityAttribute : System.Attribute {
  public InternalBufferCapacityAttribute(int count) { }
 }
}
public static class MinimapChecks {
 static void Check(bool ok,string message) { if(!ok) throw new System.Exception(message); }
 public static string Run() {
  foreach(var minimum in new[]{new Unity.Mathematics.int2(-8192,-8192),new Unity.Mathematics.int2(0,0)})
   foreach(var origin in new[]{new Unity.Mathematics.int2(1024,-2048),new Unity.Mathematics.int2(-3141,987)}) {
    var info=new Game.World.Generation.WorldMinimapInfo {
     Resolution=129,WorldMinimum=minimum,WorldSize=16384,MacroOrigin=origin,Revision=1
    };
    var centre=(Unity.Mathematics.float2)(minimum-origin)+info.WorldSize*.5f;
    Check(Unity.Mathematics.math.distance(info.ToWorldUv(centre),new Unity.Mathematics.float2(.5f))<.001f,"Biome map centre lost start offset");
    foreach(int x in new[]{0,1,64,127,128}) foreach(int z in new[]{0,64,128}) {
     var player=new Unity.Mathematics.float2(-200.5f,16.75f);
     var worldPosition=(Unity.Mathematics.float2)(minimum-origin)+new Unity.Mathematics.float2(x,z)*128f;
     Check(Unity.Mathematics.math.distance(info.ToWorldUv(worldPosition),new Unity.Mathematics.float2(x,z)/128f)<.0001f,"Biome sample does not match macro origin");
    }
    var outside=info.ToWorldUv(new Unity.Mathematics.float2(100000,-100000));
    Check(Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(outside)),"Outside-world position produced invalid offset");
   }
  Check(System.Runtime.InteropServices.Marshal.SizeOf<Game.World.Generation.WorldMinimapCell>()==1,"Biome buffer must stay one byte per sample");
  return "Minimap PASS: macro biome label coordinates, positive/negative origins, sample alignment, world edges and one-byte biome samples. UI/GPU not tested.";
 }
}
'@
Add-Type -TypeDefinition ($mapMetadata + "`n" + $mapBiome + "`n" + $mapChecks) -ReferencedAssemblies $mapReferences
[MinimapChecks]::Run()
