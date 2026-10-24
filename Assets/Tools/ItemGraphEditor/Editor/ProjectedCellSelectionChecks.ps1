$ErrorActionPreference='Stop'
# Local selection and GPU cut-volume regression checks; no CPU mask cache.
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
function Read([string]$p){Get-Content (Join-Path $root $p) -Raw}
function StripUsing([string]$s){$s -replace '(?m)^using [^;]+;\r?\n',''}
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
  public struct SelectionFace {public int2 Coordinate;public ProjectedCellData Cell;}
  public List<SelectionFace> SelectionFaces=new List<SelectionFace>();
  public List<SelectionFace> WaterSelectionFaces=new List<SelectionFace>();
  public HashSet<int2> Coverage=new HashSet<int2>();
  public bool CoversTile(int2 key)=>IsReady&&Coverage.Contains(key);
  public float3 ClippingFeet;public ViewDirection ClippingView;
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
 public void Select(LocalProjectionRenderer.SelectionFace face,float2 pointer,float3 player,ViewDirection view,
  ref float depth,ref SelectedProjectedCell result)=>TrySelectLocalCell(face,pointer,view,ref depth,ref result);
 public static void Run(){
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) foreach(float h in new[]{7f,7.5f,8f})
   foreach(int distance in new[]{-2,0,2}) foreach(float delta in new[]{-.5f,0f,.5f,1f}) {
    var player=new float3(-.25f,h,16.5f);var forward=ViewDirectionUtility.Forward(view);
    var receiver=player+new float3(forward.x*distance,delta,forward.y*distance);
    var plane=new float4(math.floor(player.x),math.floor(player.z),Game.World.Rendering.ChunkProjectionBuilder.GetClippingHeight(player),1);
    if(GpuTreeClipProbe.TreeReceiverInCutVolume(receiver,plane,(float2)forward)!=Game.World.Rendering.ChunkProjectionBuilder.ShouldClipLocalObject(receiver,player,view))
     throw new Exception("GPU local clip plane differs from geometry clipping");
   }
  foreach(ViewDirection view in Enum.GetValues(typeof(ViewDirection))) foreach(int c in new[]{-1,0,1}) {
   var coordinate=new int2(c,-c);
   var origin=ChunkRenderPositionUtility.GetPosition(coordinate,view,1,.5f,.01f);
   var pointer=new float2(origin.x-8+7.5f,origin.y+264-500.5f*.5f);
   var cell=new ProjectedCellData {Position=7u|(500u<<16),ProjectionX=7,ProjectionY=500,
    SourceAirIndex=(ushort)(7+16*(7+16*7)),FaceType=ProjectedFaceType.Top,BlockData=4};
   var face=new LocalProjectionRenderer.SelectionFace {Coordinate=coordinate,Cell=cell};
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
   face.Cell.IsCutTop=false;local.SelectionFaces.Add(face);selection=default;
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
$editorVersion=((Get-Content (Join-Path $root '../ProjectSettings/ProjectVersion.txt') | Select-String '^m_EditorVersion: ').Line -replace '^m_EditorVersion: ','').Trim()
$mathPath="C:/Program Files/Unity/Hub/Editor/$editorVersion/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll"
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
Add-Type -TypeDefinition ($adapter+$production) -ReferencedAssemblies $refs
[Game.World.Interaction.SelectionProbe]::Run()
$selectionRender=Read 'Scripts/World/Interaction/Systems/ProjectedCellSelectionRenderSystem.cs'
$treeRendering=Read 'Scripts/World/Trees/PineTreeTestSystem.cs'
$localRenderer=Read 'Scripts/World/Rendering/LocalProjectionRenderer.cs'
if ($treeShader -notmatch 'clip\(.5 - coverage\)' -or $treeRendering -notmatch 'ApplyTreeClipping\(properties\)') {
 throw 'GPU local tree clipping must remain active.'
}
if ($treeRendering -notmatch 'UpdateAfter\(typeof\(WorldProjectionDrawSystem\)\)' -or
    $treeRendering -match 'WorldItemOcclusionCache|GetTreeMaskPixels|UpdateOcclusion' -or
    $treeRendering -match 'GpuLocalClipping' -or
    $treeShader -notmatch '_TreeLocalOnly') {
 throw 'Tree cards must use current depth state, GPU local clipping and no CPU masks.'
}
if ($selectionSource -notmatch 'localProjection.CoversTile\(tileKey\)' -or
 $selectionSource -notmatch 'WaterSelectionFaces' -or
 $selectionSource -match 'MaximumInteractionDistance|math.distancesq|GetBufferLookup|WithAll<ChunkGenerated>' -or
 $selectionRender -notmatch 'UpdateAfter\(typeof\((LocalProjectionRenderSystem|WorldProjectionDrawSystem)\)\)' -or
 $selectionRender -notmatch 'selectionSystem.RefreshSelection\(\)') {
 throw 'Selection must use the completed local layer and exclude covered base geometry.'
}
'PASS actual local-only selection: four views/negative chunks, source metadata, water ties, no distance limit, coverage/readiness boundaries and selectable marked caps; camera/buffers adapted, Unity input not tested.'
