$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/Items/WorldItemRenderSystem.cs') -Raw
$build=[regex]::Match($source,'(?s)private static void BuildLightingIndex\(.*?\n        \}').Value
$visible=[regex]::Match($source,'(?s)private bool IsVisible\(.*?\n        \}').Value
$localSource=Get-Content (Join-Path $root 'Scripts/World/Chunks/ChunkUtility.cs') -Raw
$local=[regex]::Match($localSource,'(?s)public static int3 ToLocalPosition\(.*?\n        \}').Value
if (!$build -or !$visible -or !$local) {throw 'Production methods missing'}
$editorVersion=((Get-Content (Join-Path $root '../ProjectSettings/ProjectVersion.txt') | Select-String '^m_EditorVersion: ').Line -replace '^m_EditorVersion: ','').Trim()
$mathPath="C:/Program Files/Unity/Hub/Editor/$editorVersion/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll"
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$adapter=@'
using System.Collections.Generic;
using Unity.Mathematics;
public enum ProjectedFaceType {Top,Side}
public struct ProjectedCellData {public ProjectedFaceType FaceType; public bool IsCutTop,IsLowerHalf;public int SourceAirIndex,Light;}
public class DynamicBuffer<T>:List<T>{}
public static class ChunkSettings {public const int SizeX=16,SizeZ=16;}
public static class ChunkUtility {
'@
$adapter+=$local+@'
}
public struct Bounds {public float X,Y,Width,Height;}
public struct Plane {public float Left,Right,Bottom,Top;}
// Camera-plane adapter tests early-rejection control flow, not Unity's native frustum implementation.
public static class GeometryUtility {
 public static bool TestPlanesAABB(Plane[] p,Bounds b)=>b.X+b.Width>=p[0].Left&&b.X<=p[0].Right&&b.Y+b.Height>=p[0].Bottom&&b.Y<=p[0].Top;
}
public class RenderChecks {
private List<Plane[]> cameraPlanes=new List<Plane[]>(); private int activePlaneCount;
'@
$tests=@'
public static void Run(){
 var cells=new DynamicBuffer<ProjectedCellData>();
 for(int y=1;y<40;y++)for(int x=0;x<16;x++)for(int z=0;z<16;z++){
  cells.Add(new ProjectedCellData{SourceAirIndex=x+16*(z+16*y),IsLowerHalf=(x%2==0),FaceType=(z%3==0?ProjectedFaceType.Side:ProjectedFaceType.Top),IsCutTop=z%5==0,Light=y});
 }
 cells.Add(new ProjectedCellData{SourceAirIndex=cells[1].SourceAirIndex,FaceType=ProjectedFaceType.Top,IsLowerHalf=cells[1].IsLowerHalf,Light=999});
 var tiles=new Dictionary<int3,ProjectedCellData>();BuildLightingIndex(tiles,cells);
 for(int y=0;y<81;y++)for(int x=0;x<16;x++)for(int z=0;z<16;z++){
  bool found=false;ProjectedCellData old=default;
  foreach(var cell in cells){var p=ChunkUtility.ToLocalPosition(cell.SourceAirIndex);
   if(cell.FaceType!=ProjectedFaceType.Top||cell.IsCutTop||p.x!=x||p.z!=z||p.y*2-(cell.IsLowerHalf?1:0)!=y)continue;
   found=true;old=cell;break;
  }
  if(tiles.TryGetValue(new int3(x,y,z),out var tile)!=found||(found&&tile.Light!=old.Light))throw new System.Exception("Index differs from previous scan");
 }
 cells.Clear();cells.Add(new ProjectedCellData{SourceAirIndex=256,Light=23});BuildLightingIndex(tiles,cells);
 if(tiles.Count!=1||tiles[new int3(0,2,0)].Light!=23)throw new System.Exception("Rebuild retained stale geometry/light");
 var test=new RenderChecks();
 if(!test.IsVisible(new Bounds{X=100,Y=100}))throw new System.Exception("No camera should fail open");
 test.cameraPlanes.Add(new[]{new Plane{Left=0,Right=10,Bottom=0,Top=10}});test.activePlaneCount=1;
 if(test.IsVisible(new Bounds{X=20,Y=20,Width=1,Height=1}))throw new System.Exception("Offscreen accepted");
 if(!test.IsVisible(new Bounds{X=-.5f,Y=2,Width=1,Height=1}))throw new System.Exception("Edge-overlapping sprite culled");
 test.cameraPlanes.Add(new[]{new Plane{Left=20,Right=30,Bottom=20,Top=30}});test.activePlaneCount=2;
 if(!test.IsVisible(new Bounds{X=22,Y=22,Width=1,Height=1}))throw new System.Exception("Second camera ignored");
}
}
'@
Add-Type -TypeDefinition ($adapter+$build+$visible+$tests) -ReferencedAssemblies $refs
[RenderChecks]::Run()
if ($source.IndexOf('if (!IsVisible(bounds)) continue;') -gt $source.IndexOf('ApplyGroundLighting(ref data')) {throw 'Culling must precede lighting lookup'}
if (!$source.Contains('cellsLookup.DidChange(entity, LastSystemVersion)') -or !$source.Contains('index.Entity != entity') -or !$source.Contains('lightingChunks.Remove(coordinate)')) {throw 'Cache invalidation/unload gates missing'}
'PASS production index vs old scan: full/half tops, cut/side exclusion, duplicate first-match, absent tiles and rebuild; camera adapter: offscreen, edge overlap, multi-camera, no camera. Unity visual/FPS verification pending.'
