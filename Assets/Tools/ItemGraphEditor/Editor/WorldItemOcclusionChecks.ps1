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
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public class Camera {public Vector3 WorldToScreenPoint(Vector3 p)=>new Vector3(p.x,p.y,100+p.z);}
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
 public class LocalProjectionRenderer {
  public static LocalProjectionRenderer Instance;
  public bool IsReady=true;
  public struct OcclusionFace {public int2 Coordinate;public ProjectedCellData Cell;}
  public List<OcclusionFace> OcclusionFaces=new List<OcclusionFace>();
  public List<OcclusionFace> WaterSelectionFaces=new List<OcclusionFace>();
  public HashSet<int2> Coverage=new HashSet<int2>();
  public bool CoversTile(int2 key)=>IsReady&&Coverage.Contains(key);
  public float3 ClippingFeet;public ViewDirection ClippingView;
  public bool IntersectsCoverage(Vector4 rect){if(!IsReady)return false;foreach(var key in Coverage) if(rect.x<key.x+1&&rect.x+rect.z>key.x&&rect.y<key.y*.5f+.5f&&rect.y+rect.w>key.y*.5f)return true;return false;}
 }
 public enum ProjectedFaceType {Top,SideUpper,SideLower}
 public struct ProjectedCellData {public uint Position,BlockData;public ushort SourceAirIndex;public int ProjectionX,ProjectionY;public ProjectedFaceType FaceType;public bool IsCutTop,IsCutSide,IsLowerHalf;}
 public struct ProjectedClipCell {public uint X,Y;}
}
namespace Game.World.Interaction {
 public struct SelectedProjectedCell {public bool IsValid,IsCutSurface;public int2 ChunkCoordinate;public uint ProjectionPosition,BlockData;public ushort SourceAirIndex;public Game.World.Rendering.ProjectedFaceType FaceType;}
}
'@
$local=[regex]::Match((Read 'Scripts/World/Chunks/ChunkUtility.cs'),'(?s)public static int3 ToLocalPosition\(.*?\n        \}').Value
$production='namespace Game.World.Chunks {public static class ChunkUtility {'+$local+'}}'
foreach($path in 'ViewDirection.cs','ViewDirectionUtility.cs','WorldPositionProjectionUtility.cs','ChunkRenderPositionUtility.cs') {
 $production+=StripUsing (Read ('Scripts/World/Rendering/'+$path))
}
$selectionSource=Read 'Scripts/World/Interaction/Systems/ProjectedCellSelectionSystem.cs'
function ExtractMethod([string]$source,[string]$signature) {
 $start=$source.IndexOf($signature);if($start -lt 0){throw "Missing selection method: $signature"}
 $brace=$source.IndexOf('{',$start);$depth=1
 for($i=$brace+1;$i -lt $source.Length;$i++){
  if($source[$i] -eq '{'){$depth++};if($source[$i] -eq '}'){$depth--}
  if($depth -eq 0){return $source.Substring($start,$i-$start+1)}
 }
 throw 'Unclosed selection method'
}
$builder=Read 'Scripts/World/Rendering/ChunkProjectionBuilder.cs'
$production+='namespace Game.World.Rendering {public static class ChunkProjectionBuilder {'+
 [regex]::Match($builder,'(?s)public static float GetClippingHeight\(.*?;').Value+
 [regex]::Match($builder,'(?s)public static bool ShouldClipLocalObject\(.*?;').Value+
 (ExtractMethod $builder 'private static int GetDepthDistance(')+'}}'
$treeShader=Read 'Shaders/TreeSprite.shader'
$gpuCut=(ExtractMethod $treeShader 'bool TreeReceiverInCutVolume(').Replace('bool TreeReceiver','public static bool TreeReceiver').Replace('dot(','math.dot(').Replace('floor(','math.floor(')
$production+='public static class GpuTreeClipProbe {'+$gpuCut+'}'
$selectionMethods=(ExtractMethod $selectionSource 'private void FindSelection(')+(ExtractMethod $selectionSource 'private void TrySelectLocalCell(')+(ExtractMethod $selectionSource 'private void TrySelectCell(')
$production+='namespace Game.World.Interaction {public class SelectionProbe {'+
 'private const float CellWidth=1f,CellHeight=.5f,DepthStep=.01f;private Camera playerCamera=new Camera();'+$selectionMethods+@'
 public void Select(LocalProjectionRenderer.OcclusionFace face,float2 pointer,float3 player,ViewDirection view,
  ref float depth,ref SelectedProjectedCell result)=>TrySelectLocalCell(face,pointer,view,ref depth,ref result);
 public static void Run(){
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) foreach(int c in new[]{-1,0,1}) {
   var coordinate=new int2(c,-c);
   var origin=ChunkRenderPositionUtility.GetPosition(coordinate,view,1,.5f,.01f);
   var pointer=new float2(origin.x-8+7.5f,origin.y+264-500.5f*.5f);
   var cell=new ProjectedCellData {Position=7u|(500u<<16),ProjectionX=7,ProjectionY=500,
    SourceAirIndex=(ushort)(7+16*(7+16*7)),FaceType=ProjectedFaceType.Top,BlockData=4};
   var face=new LocalProjectionRenderer.OcclusionFace {Coordinate=coordinate,Cell=cell};
   var player=new float3(c*16+7.5f,7.5f,-c*16+7.5f);
   var probe=new SelectionProbe();var selection=new SelectedProjectedCell();float depth=float.MaxValue;
   probe.Select(face,pointer,player,view,ref depth,ref selection);
   if(!selection.IsValid||!math.all(selection.ChunkCoordinate==coordinate)||selection.SourceAirIndex!=cell.SourceAirIndex)
    throw new Exception("Local selection lost position/source metadata");
   face.Cell.BlockData=2;probe.Select(face,pointer,player,view,ref depth,ref selection);
   if(selection.BlockData!=4)throw new Exception("Equal-depth solid replaced water");
   depth=float.MaxValue;selection=default;
   probe.Select(face,pointer+new float2(1,0),player,view,ref depth,ref selection);
   if(selection.IsValid)throw new Exception("Pointer selected a different local tile");
   probe.Select(face,pointer,player+new float3(10,0,0),view,ref depth,ref selection);
   if(!selection.IsValid)throw new Exception("Local selection retained a world-distance limit");
   depth=float.MaxValue;selection=default;
   face.Cell.IsCutTop=true;probe.Select(face,pointer,player,view,ref depth,ref selection);
   if(!selection.IsValid||!selection.IsCutSurface||depth==float.MaxValue)throw new Exception("Cut cap must remain selectable and marked for excavation-only interaction");
   var local=new LocalProjectionRenderer();LocalProjectionRenderer.Instance=local;
   int2 tileKey=new int2((int)math.floor(pointer.x),(int)math.floor(pointer.y*2));
   face.Cell.IsCutTop=false;local.OcclusionFaces.Add(face);selection=default;
   probe.FindSelection(pointer,view,ref selection);
   if(selection.IsValid)throw new Exception("Selected a surface outside local coverage");
   local.Coverage.Add(tileKey);probe.FindSelection(pointer,view,ref selection);
   if(!selection.IsValid)throw new Exception("Covered local tile was not selected");
   local.IsReady=false;selection=default;probe.FindSelection(pointer,view,ref selection);
   if(selection.IsValid)throw new Exception("Unready local projection remained interactive");
   LocalProjectionRenderer.Instance=null;probe.FindSelection(pointer,view,ref selection);
   if(selection.IsValid)throw new Exception("Missing local projection fell back to world selection");
  }
 }
}}
'@
$tests=@'
public static class OcclusionChecks {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void CheckLocalProjection() {
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) {
   var p=new float3(7.5f,7,7.5f);
   var screen=WorldPositionProjectionUtility.Project(p,view); screen.y+=.25f;
   var key=new int2((int)math.floor(screen.x),(int)math.floor(screen.y*2));
   var rect=new Vector4(screen.x-.01f,screen.y-.01f,.02f,.02f);
   int2 forward=ViewDirectionUtility.Forward(view);
   int sx=7+forward.x*4,sz=7+forward.y*4;
   var wall=new ProjectedCellData {FaceType=ProjectedFaceType.SideUpper,
    SourceAirIndex=(ushort)(sx+16*(sz+16*7)),ProjectionX=(int)math.floor(screen.x+8),
    ProjectionY=(int)math.floor((264-screen.y)*2)};
   var cells=new DynamicBuffer<ProjectedCellData>{wall};
   var cache=new Game.Items.WorldItemOcclusionCache();
   var treeSurface=new float[]{0,0,0,1};
   cache.NeedsRebuild(1,view);cache.AddFaces(int2.zero,cells,false);
   Check(cache.GetMask(new Entity{Index=1},p,rect,1,1).Pixels[0]==255,"World wall did not hide item");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},1,p,rect,1,1,treeSurface,1,out _)[0]==255,"World wall did not hide tree pixel");
   var local=new LocalProjectionRenderer {ClippingFeet=p,ClippingView=view};local.Coverage.Add(key);
   var floor=wall;floor.FaceType=ProjectedFaceType.Top;floor.SourceAirIndex=(ushort)(7+16*(7+16*7));
   local.OcclusionFaces.Add(new LocalProjectionRenderer.OcclusionFace{Coordinate=int2.zero,Cell=floor});
   cache.SetProjectionLayers(local,true);cache.NeedsRebuild(2,view);
   cache.AddFaces(int2.zero,cells,false);cache.AddLocalFaces();
   Check(cache.GetMask(new Entity{Index=1},p,rect,1,1).Pixels[0]==0,"Removed world wall still hides local item");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},1,p,rect,1,1,treeSurface,1,out _)[0]==0,"Removed world wall still hides local tree pixel");
   Check(!cache.IsGroundTileClipped(p),"Local supporting Top was ignored");
   var plane=new float4(math.floor(p.x),math.floor(p.z),ChunkProjectionBuilder.GetClippingHeight(p),1);
   foreach(float h in new[]{-1f,0f,.5f,3f}) foreach(float d in new[]{-3f,.1f,3f}) {
    var receiver=p+new float3(forward.x*d,h,forward.y*d);
    Check(GpuTreeClipProbe.TreeReceiverInCutVolume(receiver,plane,(float2)forward)==
     ChunkProjectionBuilder.ShouldClipLocalObject(receiver,p,view),"GPU tree cut disagrees with block cut plane");
   }
   var cap=wall;cap.IsCutSide=true;
   local.OcclusionFaces.Add(new LocalProjectionRenderer.OcclusionFace{Coordinate=int2.zero,Cell=cap});
   cache.NeedsRebuild(3,view);cache.AddFaces(int2.zero,cells,false);cache.AddLocalFaces();
   Check(cache.GetMask(new Entity{Index=1},p,rect,1,1).Pixels[0]==255,"Local cut cap failed to hide item");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},1,p,rect,1,1,treeSurface,1,out _)[0]==255,"Local cap failed to hide tree pixel");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},2,p,rect,1,1,new float[]{0,10,0,1},1,out _)[0]==0,"Local cap hid a tree pixel physically in front of it");
   local.OcclusionFaces.Clear();cache.SetProjectionLayers(local,false);cache.NeedsRebuild(4,view);
   cache.AddFaces(int2.zero,cells,false);cache.AddLocalFaces();
   Check(cache.GetMask(new Entity{Index=1},p,rect,1,1).Pixels[0]==0,"F10-off local area retained world wall");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},1,p,rect,1,1,treeSurface,1,out _)[0]==0,"F10-off local tree retained world wall");
   Check(cache.IsGroundTileClipped(p),"Empty replacement area retained nonexistent ground");
   var outside=new Vector4(screen.x+2,screen.y-.01f,.02f,.02f);
   Check(cache.GetMask(new Entity{Index=2},p,outside,1,1).Pixels[0]==255,"F10-off leaked item outside local area");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},3,p,outside,1,1,treeSurface,1,out _)[0]==255,"F10-off leaked tree pixel outside local area");
   cache.SetProjectionLayers(null,true);cache.NeedsRebuild(5,view);cache.AddFaces(int2.zero,cells,false);
   Check(cache.GetMask(new Entity{Index=1},p,rect,1,1).Pixels[0]==255,"Leaving local mode failed to restore world occlusion");
   Check(cache.GetTreeMaskPixels(new Entity{Index=1},1,p,rect,1,1,treeSurface,1,out _)[0]==255,"Leaving local mode failed to restore tree occlusion");
   cache.Dispose();
  }
 }
 static void CheckTreeMaskReuse(){
  var cache=new Game.Items.WorldItemOcclusionCache();var local=new LocalProjectionRenderer();
  local.Coverage.Add(int2.zero);cache.SetProjectionLayers(local,true);
  var owner=new Entity{Index=500};var position=new float3(7,7,7);var surface=new float[]{0,0,0,1};
  var far=new Vector4(100,100,1,1);
  cache.NeedsRebuild(1,ViewDirection.Front,100);
  cache.GetTreeMaskPixels(owner,1,position,far,1,1,surface,1,out uint first);
  local.Coverage.Clear();local.Coverage.Add(new int2(2,2));cache.NeedsRebuild(2,ViewDirection.Front,100);
  cache.GetTreeMaskPixels(owner,1,position,far,1,1,surface,1,out uint second);
  Check(first==second,"Moving local region rebuilt unaffected far-tree pixels");
  var near=new Vector4(2,1,1,.5f);
  cache.GetTreeMaskPixels(owner,2,position,near,1,1,surface,1,out uint nearBefore);
  local.Coverage.Clear();local.Coverage.Add(new int2(4,4));cache.NeedsRebuild(3,ViewDirection.Front,100);
  cache.GetTreeMaskPixels(owner,2,position,near,1,1,surface,1,out uint nearAfter);
  Check(nearBefore!=nearAfter,"Tree leaving local coverage kept stale overlap mask");
  cache.NeedsRebuild(4,ViewDirection.Front,101);
  cache.GetTreeMaskPixels(owner,1,position,far,1,1,surface,1,out uint changedWorld);
  Check(changedWorld!=first,"World geometry change reused stale far-tree mask");
  cache.Dispose();
 }
 public static string Run(){
  CheckTreeMaskReuse();
  CheckLocalProjection();
  Console.WriteLine("PASS local item/tree occlusion: four views, GPU cut-plane parity, far-tree mask reuse, local ground/caps, F10 and world layers");
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
$editorVersion=((Get-Content (Join-Path $root '../ProjectSettings/ProjectVersion.txt') | Select-String '^m_EditorVersion: ').Line -replace '^m_EditorVersion: ','').Trim()
$mathPath="C:/Program Files/Unity/Hub/Editor/$editorVersion/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll"
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -TypeDefinition ($adapter+$production+$old+$new+$tests) -ReferencedAssemblies $refs
[OcclusionChecks]::Run()
[Game.World.Interaction.SelectionProbe]::Run()
$selectionRender=Read 'Scripts/World/Interaction/Systems/ProjectedCellSelectionRenderSystem.cs'
$treeRendering=Read 'Scripts/World/Trees/PineTreeTestSystem.cs'
$localRenderer=Read 'Scripts/World/Rendering/LocalProjectionRenderer.cs'
if ($new -match 'ShouldClipTreePixel|float3 worldPixel' -or
 $treeShader -notmatch 'clip\(.5 - SAMPLE_TEXTURE2D_LOD\(_TreeLocalCoverage' -or
 $treeRendering -notmatch 'ApplyTreeClipping\(properties\)' -or
 $treeRendering -notmatch 'NeedsRebuild\(signature, view, worldSignature\)' -or
 $localRenderer -notmatch 'publishedOcclusionHash != nextOcclusionHash' -or
 $localRenderer -match 'HashOcclusion\(cell\.(LightData|LocalLight)') {
 throw 'Tree clipping must run on GPU; mask revisions must depend on geometry/coverage, not lighting uploads.'
}
if ($treeRendering -notmatch 'UpdateAfter\(typeof\(LocalProjectionRenderSystem\)\)' -or
    $treeRendering -notmatch 'occlusion.SetProjectionLayers\(hasLocal \? local : null, worldVisible\)' -or
    $treeRendering -notmatch 'local.OcclusionRevision' -or
    $treeRendering -notmatch 'occlusion.AddLocalFaces\(\)') {
 throw 'Tree masks must follow the completed local snapshot and invalidate when it changes.'
}
if ($selectionSource -notmatch 'localProjection.CoversTile\(tileKey\)' -or
 $selectionSource -notmatch 'WaterSelectionFaces' -or
 $selectionSource -match 'MaximumInteractionDistance|math.distancesq|GetBufferLookup|WithAll<ChunkGenerated>' -or
 $selectionRender -notmatch 'UpdateAfter\(typeof\(LocalProjectionRenderSystem\)\)' -or
 $selectionRender -notmatch 'selectionSystem.RefreshSelection\(\)') {
 throw 'Selection must use the completed local layer and exclude covered base geometry.'
}
'PASS actual local-only selection: four views/negative chunks, source metadata, water ties, no distance limit, coverage/readiness boundaries and selectable marked caps; camera/buffers adapted, Unity input not tested.'
