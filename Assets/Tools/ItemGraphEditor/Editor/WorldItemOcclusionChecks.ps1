$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read([string]$p){Get-Content (Join-Path $root $p) -Raw}
function StripUsing([string]$s){$s -replace '(?m)^using [^;]+;\r?\n',''}
$new=StripUsing (Read 'Scripts/Items/WorldItemOcclusionCache.cs')
$old=(StripUsing (Read 'Tools/ItemGraphEditor/Editor/Fixtures/WorldItemOcclusionBaseline.cs.txt')).Replace('WorldItemOcclusionCache','BaselineOcclusionCache')
$adapter=@'
using System;
using System.Collections.Generic;
using Game.World.Chunks;
using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Unity.Entities {
 public struct Entity:IEquatable<Entity>{public int Index;public bool Equals(Entity e)=>Index==e.Index;public override bool Equals(object e)=>e is Entity x&&Equals(x);public override int GetHashCode()=>Index;}
 public class DynamicBuffer<T>:List<T>{}
}
namespace Unity.Profiling {
 public struct ProfilerMarker {public ProfilerMarker(string s){}public Scope Auto()=>default;public struct Scope:IDisposable{public void Dispose(){}}}
}
namespace UnityEngine {
 public class Object{public static void Destroy(Object o){}}
 public enum TextureFormat{R8} public enum FilterMode{Point}public enum TextureWrapMode{Clamp}
 public struct PixelData<T>{public T[] Values;public T this[int i]{get=>Values[i];set=>Values[i]=value;}}
 public class Texture2D:Object {public int width,height;public byte[] Pixels;public string name;public FilterMode filterMode;public TextureWrapMode wrapMode;
 public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear){width=w;height=h;Pixels=new byte[w*h];}
 public PixelData<T> GetRawTextureData<T>()=>new PixelData<T>{Values=(T[])(object)Pixels};public void Apply(bool m,bool r){} }
 public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
 public static bool operator==(Vector4 a,Vector4 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;
 public static bool operator!=(Vector4 a,Vector4 b)=>!(a==b);public override bool Equals(object o)=>o is Vector4 v&&this==v;public override int GetHashCode()=>x.GetHashCode();}
}
namespace Game.World.Chunks {public static class ChunkSettings{public const int SizeX=16,SizeZ=16,SizeY=256;}}
namespace Game.World.Rendering {
 public enum ProjectedFaceType {Top,SideUpper,SideLower}
 public struct ProjectedCellData {public uint Position,BlockData;public ushort SourceAirIndex;public int ProjectionX,ProjectionY;public ProjectedFaceType FaceType;public bool IsCutTop,IsCutSide,IsLowerHalf;}
 public struct ProjectedClipCell {public uint X,Y;}
}
'@
$local=[regex]::Match((Read 'Scripts/World/Chunks/ChunkUtility.cs'),'(?s)public static int3 ToLocalPosition\(.*?\n        \}').Value
$production='namespace Game.World.Chunks {public static class ChunkUtility {'+$local+'}}'
foreach($path in 'ViewDirection.cs','ViewDirectionUtility.cs','WorldPositionProjectionUtility.cs','ChunkRenderPositionUtility.cs') {
 $production+=StripUsing (Read ('Scripts/World/Rendering/'+$path))
}
$tests=@'
public static class OcclusionChecks {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 public static string Run(){
  var cells=new DynamicBuffer<ProjectedCellData>();var clipped=new DynamicBuffer<ProjectedClipCell>();
  var random=new System.Random(42);
  for(int i=0;i<6000;i++){
   int x=random.Next(16),z=random.Next(16),y=random.Next(55,75);
   cells.Add(new ProjectedCellData{Position=(uint)i,BlockData=2,SourceAirIndex=(ushort)(x+16*(z+16*y)),ProjectionX=random.Next(16),ProjectionY=random.Next(360,440),
    FaceType=(ProjectedFaceType)(i%3),IsCutTop=i%19==0,IsCutSide=i%23==0,IsLowerHalf=i%2==0});
  }
  for(uint i=0;i<16;i++)clipped.Add(new ProjectedClipCell{X=i,Y=400+i});
  var current=new Game.Items.WorldItemOcclusionCache();var baseline=new Game.Items.BaselineOcclusionCache();
  int hidden=0,shown=0;
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection)))foreach(bool clip in new[]{false,true}){
   Check(current.NeedsRebuild((ulong)(1+(int)view*2+(clip?1:0)),view),"Expected rebuild");
   baseline.NeedsRebuild((ulong)(1+(int)view*2+(clip?1:0)),view);
   foreach(var coordinate in new[]{new int2(-1,-1),new int2(0,0),new int2(1,0)}){
    current.AddClipped(coordinate,clipped);baseline.AddClipped(coordinate,clipped);
    current.AddFaces(coordinate,cells,clip);baseline.AddFaces(coordinate,cells,clip);
   }
   for(int i=0;i<40;i++){
    var p=new float3(random.Next(-16,32)+.5f,random.Next(56,73)+(i%2==0?.5f:0),random.Next(-16,16)+.5f);
    var centre=WorldPositionProjectionUtility.Project(p,view);centre.y+=.25f;
    var rect=new Vector4(centre.x-.5f,centre.y-.25f,1,.5f);var entity=new Entity{Index=i};
    var a=current.GetMask(entity,p,rect,32,16,out var av,false);var b=baseline.GetMask(entity,p,rect,32,16,out var bv,false);
    for(int pixel=0;pixel<a.Pixels.Length;pixel++){
     Check(a.Pixels[pixel]==b.Pixels[pixel],"Flat chains changed mask pixel");if(a.Pixels[pixel]==0)shown++;else hidden++;
    }
    Check(current.IsGroundTileClipped(p)==baseline.IsGroundTileClipped(p),"Ground tile visibility changed");
    current.GetMask(entity,p,rect,32,16,out var same,false);Check(av==same,"Unchanged mask regenerated");
   }
  }
  Check(hidden>0&&shown>0,"Fixture did not cover visible and occluded pixels");
  // Warm capacities before measuring managed rebuild allocations only.
  current.NeedsRebuild(100,ViewDirection.Front);current.AddClipped(int2.zero,clipped);current.AddFaces(int2.zero,cells,true);
  baseline.NeedsRebuild(100,ViewDirection.Front);baseline.AddClipped(int2.zero,clipped);baseline.AddFaces(int2.zero,cells,true);
  long before=GC.GetAllocatedBytesForCurrentThread();
  for(ulong i=101;i<111;i++){current.NeedsRebuild(i,ViewDirection.Front);current.AddClipped(int2.zero,clipped);current.AddFaces(int2.zero,cells,true);}
  long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
  before=GC.GetAllocatedBytesForCurrentThread();
  for(ulong i=101;i<111;i++){baseline.NeedsRebuild(i,ViewDirection.Front);baseline.AddClipped(int2.zero,clipped);baseline.AddFaces(int2.zero,cells,true);}
  long oldAllocated=GC.GetAllocatedBytesForCurrentThread()-before;
  Check(allocated==0,"Warmed flat rebuild allocated managed memory");Check(oldAllocated>0,"Baseline allocation fixture ineffective");
  current.Dispose();baseline.Dispose();
  return "PASS mask pixels vs previous implementation across 4 views/clipping/full-half-cut faces; warmed 10 rebuilds: flat="+allocated+" B, previous="+oldAllocated+" B. Terrain buffers, textures and markers adapted; real Unity.Mathematics/projection helpers. Unity FPS not measured.";
 }
}
'@
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -TypeDefinition ($adapter+$production+$old+$new+$tests) -ReferencedAssemblies $refs
[OcclusionChecks]::Run()
