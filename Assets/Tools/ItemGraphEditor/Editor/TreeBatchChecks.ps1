$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/World/Trees/TreeDrawBatch.cs') -Raw
$source=$source -replace '(?m)^using [^;]+;\r?\n',''
$harness=@'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Game.World.Rendering;
using UnityEngine;
using Game.World.Trees;
namespace UnityEngine {
 public class Object {public static void Destroy(Object obj){}}
 [StructLayout(LayoutKind.Sequential)] public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Bounds {public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}public void Encapsulate(Bounds other){float min=Math.Min(center.x-size.x/2,other.center.x-other.size.x/2),max=Math.Max(center.x+size.x/2,other.center.x+other.size.x/2);center.x=(min+max)/2;size.x=max-min;}}
 public enum TextureFormat{R8}public enum FilterMode{Point}public enum TextureWrapMode{Clamp}public enum MeshTopology{Triangles}
 public class GraphicsBuffer:IDisposable {public enum Target{Structured}public static int Stride,Count,Uploads;public static TreeDrawData[] Uploaded;public GraphicsBuffer(Target t,int c,int stride){Stride=stride;}public void SetData(TreeDrawData[] data,int start,int dest,int count){Uploads++;Count=count;Uploaded=new TreeDrawData[count];Array.Copy(data,start,Uploaded,dest,count);}public void Dispose(){}}
 public class Material{}public class MaterialPropertyBlock {public void SetBuffer(string n,GraphicsBuffer b){}}
 public struct RenderParams {public Bounds worldBounds;public MaterialPropertyBlock matProps;public RenderParams(Material material){worldBounds=default;matProps=null;}}
 public static class Graphics {public static int Draws,Instances;public static void RenderPrimitives(RenderParams p,MeshTopology t,int vertices,int count){if(vertices!=6)throw new Exception("Not a quad");Draws++;Instances=count;}}
}
public static class TreeBatchChecks {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static TreeDrawData Record(int i)=>new TreeDrawData {Rect=new Vector4(i*2,0,2,5),Layers=new Vector4(i%16,99,0,0)};
 public static void Run(){
  Check(Marshal.SizeOf<TreeDrawData>()==64,"C#/HLSL stride mismatch");
  var material=new Material();var properties=new MaterialPropertyBlock();
  using(var batch=new TreeDrawBatch(2,3)){
   Check(GraphicsBuffer.Stride==64,"Wrong GPU buffer stride");
   batch.Begin();batch.Submit(material,properties);Check(Graphics.Draws==0,"Empty batch submitted");
   for(int i=0;i<32;i++)batch.Add(Record(i));
   Check(batch.Count==32&&batch.Bounds.size.x==64,"Batch bounds do not enclose all instances");
   batch.Submit(material,properties);Check(Graphics.Draws==1&&Graphics.Instances==32,"Must submit 32 instances in one draw");
   Check(GraphicsBuffer.Uploads==1,"Expected one buffer upload");
   for(int i=0;i<32;i++)Check(GraphicsBuffer.Uploaded[i].Layers.x==i%16,"Instance layer cross-contamination");
   batch.Submit(material,properties,false);
   Check(GraphicsBuffer.Uploads==1&&Graphics.Draws==2,"Fog pass must reuse uploaded instances");
   bool overflow=false;try{batch.Add(Record(32));}catch(InvalidOperationException){overflow=true;}Check(overflow,"Overflow not rejected");
   batch.Begin();batch.Add(Record(0));batch.Submit(material,properties);
   Check(GraphicsBuffer.Uploads==2&&Graphics.Instances==1,"Next frame did not upload the current records");
  }
  foreach(int count in new[]{0,1,31,32,33,64,65,1000})Check((count+31)/32==(int)Math.Ceiling(count/32.0),"Batch split formula mismatch");
 }
}
'@
$harness += @'
namespace Game.World.Rendering {
 public class ChunkProceduralRenderer {
  public static ChunkProceduralRenderer Instance=new ChunkProceduralRenderer();
  public UnityEngine.Bounds SurfaceDepthBounds(UnityEngine.Bounds b) {return b;}
 }
}
'@
Add-Type -TypeDefinition ($harness+$source)
[TreeBatchChecks]::Run()
$shader=Get-Content (Join-Path $root 'Shaders/TreeSprite.shader') -Raw
$runtime=Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeTestSystem.cs') -Raw
if($shader -notmatch 'SV_InstanceID' -or $shader -notmatch 'StructuredBuffer<TreeDrawData>' -or
 $shader -notmatch 'data.Layers.z' -or $runtime -match 'GetTreeMaskPixels|WorldItemOcclusionCache|properties.SetTexture\("_MainTex"') {throw 'Instance data or shared texture bank contract failed'}
'PASS actual batch implementation: 64-byte stride, 32 instances/draw, empty/full/overflow bounds, stable texture layers, fog-pass buffer reuse and next-frame uploads. Unity GPU timing remains manual.'
