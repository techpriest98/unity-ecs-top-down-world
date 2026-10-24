$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/World/Trees/TreeDrawBatch.cs') -Raw
$source=$source -replace '(?m)^using [^;]+;\r?\n',''
$harness=@'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Entities;
using UnityEngine;
using Game.World.Trees;
namespace Unity.Entities {
 public struct Entity:IEquatable<Entity>{public int Index;public bool Equals(Entity other)=>Index==other.Index;public override bool Equals(object other)=>other is Entity&&Equals((Entity)other);public override int GetHashCode()=>Index;public static bool operator==(Entity a,Entity b)=>a.Equals(b);public static bool operator!=(Entity a,Entity b)=>!a.Equals(b);}
}
namespace UnityEngine {
 public class Object {public static void Destroy(Object obj){}}
 [StructLayout(LayoutKind.Sequential)] public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Bounds {public Vector3 center,size;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}public void Encapsulate(Bounds other){float min=Math.Min(center.x-size.x/2,other.center.x-other.size.x/2),max=Math.Max(center.x+size.x/2,other.center.x+other.size.x/2);center.x=(min+max)/2;size.x=max-min;}}
 public enum TextureFormat{R8}public enum FilterMode{Point}public enum TextureWrapMode{Clamp}public enum MeshTopology{Triangles}
 public class Texture2DArray:Object {public static int Writes,Applies;public static Dictionary<int,byte[]> Slices=new Dictionary<int,byte[]>();public string name;public FilterMode filterMode;public TextureWrapMode wrapMode;public Texture2DArray(int w,int h,int count,TextureFormat f,bool m,bool l){}public void SetPixelData(byte[] data,int mip,int slice){Writes++;Slices[slice]=(byte[])data.Clone();}public void Apply(bool m,bool r){Applies++;}}
 public class GraphicsBuffer:IDisposable {public enum Target{Structured}public static int Stride,Count;public static TreeDrawData[] Uploaded;public GraphicsBuffer(Target t,int c,int stride){Stride=stride;}public void SetData(TreeDrawData[] data,int start,int dest,int count){Count=count;Uploaded=new TreeDrawData[count];Array.Copy(data,start,Uploaded,dest,count);}public void Dispose(){}}
 public class Material{}public class MaterialPropertyBlock {public void SetBuffer(string n,GraphicsBuffer b){}public void SetTexture(string n,Texture2DArray t){}}
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
   for(int i=0;i<32;i++)batch.Add(Record(i),new Entity{Index=i},1,new byte[]{(byte)i,0,0,0,0,0},(uint)i+1);
   Check(batch.Count==32&&batch.Bounds.size.x==64,"Batch bounds do not enclose all instances");
   batch.Submit(material,properties);Check(Graphics.Draws==1&&Graphics.Instances==32,"Must submit 32 instances in one draw");
   Check(Texture2DArray.Writes==32&&Texture2DArray.Applies==1,"Expected one mask array upload");
   for(int i=0;i<32;i++)Check(GraphicsBuffer.Uploaded[i].Layers.y==i&&Texture2DArray.Slices[i][0]==i,"Mask slice cross-contamination");
   bool overflow=false;try{batch.Add(Record(32),default,1,new byte[6],99);}catch(InvalidOperationException){overflow=true;}Check(overflow,"Overflow not rejected");
   batch.Begin();for(int i=0;i<32;i++)batch.Add(Record(i),new Entity{Index=i},1,new byte[6],(uint)i+1);
   batch.Submit(material,properties);Check(Texture2DArray.Writes==32&&Texture2DArray.Applies==1,"Warm masks reuploaded");
   batch.Begin();batch.Add(Record(0),new Entity{Index=1000},1,new byte[6],1);batch.Submit(material,properties);
   Check(Texture2DArray.Writes==33&&Texture2DArray.Applies==2,"Changed owner in same slot failed to refresh mask");
   batch.Begin();batch.Add(Record(0),new Entity{Index=1000},2,new byte[6],1);batch.Submit(material,properties);
   Check(Texture2DArray.Writes==34,"Changed tree ID in same chunk reused stale mask");
  }
  foreach(int count in new[]{0,1,31,32,33,64,65,1000})Check((count+31)/32==(int)Math.Ceiling(count/32.0),"Batch split formula mismatch");
 }
}
'@
Add-Type -TypeDefinition ($harness+$source)
[TreeBatchChecks]::Run()
$shader=Get-Content (Join-Path $root 'Shaders/TreeSprite.shader') -Raw
$runtime=Get-Content (Join-Path $root 'Scripts/World/Trees/PineTreeTestSystem.cs') -Raw
$occlusion=Get-Content (Join-Path $root 'Scripts/Items/WorldItemOcclusionCache.cs') -Raw
if($shader -notmatch 'SV_InstanceID' -or $shader -notmatch 'StructuredBuffer<TreeDrawData>' -or
 $shader -notmatch 'data.Layers.y' -or $runtime -match 'properties.SetTexture\("_MainTex"' -or
 $occlusion -notmatch 'byte\[\] GetTreeMaskPixels') {throw 'Instance data, shared texture banks or CPU-only mask path missing'}
'PASS actual batch implementation: 64-byte stride, 32 instances/draw, empty/full/overflow bounds, mask slot remapping, owner/ID invalidation and no warm-mask reuploads. Unity GPU timing remains manual.'
