$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$batch=Get-Content (Join-Path $root 'Scripts/Items/WorldItemDrawBatch.cs') -Raw
# Production batching logic; adapters implement only native Unity graphics/storage calls.
$adapter=@'
using System;
namespace Unity.Collections {
 public struct NativeArray<T> {public T[] Values;public NativeArray(T[] a){Values=a;} public T this[int i]{get=>Values[i];set=>Values[i]=value;}
 public static void Copy(NativeArray<T> a,int ai,NativeArray<T> b,int bi,int count)=>Array.Copy(a.Values,ai,b.Values,bi,count);}
}
namespace UnityEngine {
 public class Object {public bool Destroyed;public static void Destroy(Object o){if(o!=null)o.Destroyed=true;}}
 public class Texture:Object {}
 public enum TextureFormat {R8} public enum FilterMode {Point} public enum TextureWrapMode {Clamp}
 public class Texture2D:Texture {public int width,height;public byte[] Pixels;public int Applies;public string name;public FilterMode filterMode;public TextureWrapMode wrapMode;
 public Texture2D(int w,int h,TextureFormat f,bool m,bool l){width=w;height=h;Pixels=new byte[w*h];}
 public Unity.Collections.NativeArray<T> GetRawTextureData<T>()=>new Unity.Collections.NativeArray<T>((T[])(object)Pixels);
 public void Apply(bool m,bool r){Applies++;}}
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
 public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
 public struct Bounds {public void Encapsulate(Bounds b){}}
 public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);public static int Min(int a,int b)=>Math.Min(a,b);
 public static int NextPowerOfTwo(int n){int p=1;while(p<n)p*=2;return p;}}
 public class Material:Object {}
 public class GraphicsBuffer {public enum Target {Structured} public bool Disposed;public int Stride;public Array Data;
 public GraphicsBuffer(Target t,int count,int stride){Stride=stride;}
 public void SetData<T>(T[] src,int a,int b,int count){var data=new T[count];Array.Copy(src,a,data,0,count);Data=data;}
 public void Dispose(){Disposed=true;}}
 public class MaterialPropertyBlock {public Texture2D Atlas;public GraphicsBuffer Buffer;public Texture Main;
 public void SetBuffer(string n,GraphicsBuffer b){Buffer=b;}
 public void SetTexture(string n,Texture t){if(n=="_OcclusionMask")Atlas=(Texture2D)t;else Main=t;}}
 public struct RenderParams {public MaterialPropertyBlock matProps;public Bounds worldBounds;public RenderParams(Material m){matProps=null;worldBounds=default;}}
 public enum MeshTopology {Triangles}
 public static class Graphics {public static RenderParams Last;public static int Calls,Instances;
 public static void RenderPrimitives(RenderParams p,MeshTopology t,int vertices,int instances){Last=p;Calls++;Instances=instances;}}
}
'@
$tests=@'
public static class BatchChecks {
 static void Check(bool b,string m){if(!b)throw new System.Exception(m);}
 public static void Run(){
  var material=new UnityEngine.Material();var texture=new UnityEngine.Texture();var other=new UnityEngine.Texture();
  var mask=new UnityEngine.Texture2D(2,2,UnityEngine.TextureFormat.R8,false,true);mask.Pixels=new byte[]{0,255,255,0};
  var small=new UnityEngine.Texture2D(1,2,UnityEngine.TextureFormat.R8,false,true);small.Pixels=new byte[]{23,42};
  var b=new Game.Items.WorldItemDrawBatch();b.Begin(texture);
  Check(b.CanAppend(texture)&&!b.CanAppend(other),"Texture compatibility");
  b.Add(default,mask,1,default);b.Add(default,small,2,default);b.Draw(material);
  Check(UnityEngine.Graphics.Calls==1&&UnityEngine.Graphics.Instances==2,"Expected one instanced submission");
  var p=UnityEngine.Graphics.Last.matProps;var atlas=p.Atlas;var buffer=p.Buffer;
  Check(buffer.Stride==176,"C#/HLSL stride mismatch");
  var records=(Game.Items.WorldItemDrawData[])buffer.Data;
  Check(records[0].MaskPixels.z==2&&records[1].MaskPixels.x==2&&records[1].MaskPixels.z==1,"Mask rects");
  Check(atlas.Pixels[0]==0&&atlas.Pixels[1]==255&&atlas.Pixels[2]==23&&atlas.Pixels[atlas.width+2]==42,"Atlas slot copy");
  int applies=atlas.Applies;b.Begin(texture);b.Add(default,mask,1,default);b.Add(default,small,2,default);b.Draw(material);
  Check(atlas.Applies==applies,"Unchanged atlas uploaded again");
  mask.Pixels[0]=77;b.Begin(texture);b.Add(default,mask,3,default);b.Draw(material);
  Check(atlas.Applies==applies+1&&atlas.Pixels[0]==77,"Changed mask not refreshed");
  b.Begin(texture);b.Add(default,small,2,default);b.Add(default,mask,3,default);b.Draw(material);
  Check(atlas.Pixels[0]==23&&atlas.Pixels[2]==77,"Reordered slots stale");
  var large=new UnityEngine.Texture2D(4,3,UnityEngine.TextureFormat.R8,false,true);large.Pixels[0]=91;
  b.Begin(texture);b.Add(default,small,2,default);b.Add(default,large,4,default);b.Draw(material);
  var grown=UnityEngine.Graphics.Last.matProps.Atlas;
  Check(atlas.Destroyed&&grown.Pixels[0]==23&&grown.Pixels[4]==91,"Atlas growth lost earlier slot");
  b.Begin(texture);for(int i=0;i<Game.Items.WorldItemDrawBatch.Capacity;i++)b.Add(default,null,0,default);
  Check(!b.CanAppend(texture),"Capacity boundary");b.Draw(material);
  Check(UnityEngine.Graphics.Instances==128,"Full page instance count");
  b.Dispose();Check(buffer.Disposed&&grown.Destroyed,"GPU resource leak");b.Dispose();
 }
}
'@
# Usings must precede the adapter namespace declarations.
$batch=$batch -replace '(?m)^using [^;]+;\r?\n',''
Add-Type -TypeDefinition ('using System.Collections.Generic; using System.Runtime.InteropServices; using UnityEngine; using Object = UnityEngine.Object;'+$adapter+$batch+$tests)
[BatchChecks]::Run()
'PASS production batch code with graphics adapters: 176-byte layout, texture/capacity splits, instanced draw counts, atlas slot copy, no unchanged upload, mask revision/reorder/growth and resource disposal. Actual GPU rendering not executed.'
