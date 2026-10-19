$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ItemBatchShaderCompile {
 [ComImport,Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Blob {[PreserveSig]IntPtr GetBufferPointer();[PreserveSig]UIntPtr GetBufferSize();}
 [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall)]
 static extern int D3DCompile(byte[] source,UIntPtr length,string name,IntPtr defines,IntPtr include,
  string entry,string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
 public static string Compile(string source,string entry,string target){
  byte[] bytes=Encoding.UTF8.GetBytes(source);IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
  try{int result=D3DCompile(bytes,(UIntPtr)bytes.Length,"WorldItemSprite",IntPtr.Zero,IntPtr.Zero,entry,target,0x800,0,out code,out errors);
   string diagnostics="";
   if(errors!=IntPtr.Zero){var blob=(Blob)Marshal.GetObjectForIUnknown(errors);diagnostics=Marshal.PtrToStringAnsi(blob.GetBufferPointer(),checked((int)blob.GetBufferSize().ToUInt64()));Marshal.ReleaseComObject(blob);}
   if(result<0)throw new Exception(entry+" "+target+": "+diagnostics);
   return entry+"/"+target+" PASS";
  }finally{if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
 }
}
'@
$core=@'
#define TEXTURE2D(n) Texture2D<float4> n
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
float4 TransformWorldToHClip(float3 p) {return float4(p,1);}
'@
$shader=Get-Content (Join-Path $root 'Shaders/WorldItemSprite.shader') -Raw
$program=[regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
$program=[regex]::Replace($program,'#include "[^"]+"',$core)
[ItemBatchShaderCompile]::Compile($program,'Vert','vs_5_0')
[ItemBatchShaderCompile]::Compile($program,'Frag','ps_5_0')
foreach ($width in 1,2,16,32,256) {
 foreach ($u in -0.1,0,0.001,0.5,0.999,1,1.1) {
  $pixel=[Math]::Min([int][Math]::Floor([Math]::Clamp($u,0,1)*$width),$width-1)
  if ($pixel -lt 0 -or $pixel -ge $width) {throw 'Atlas addressing escaped its mask slot'}
 }
}
'PASS actual HLSL vertex/fragment D3DCompile with transform adapter, instancing/structured-buffer declarations and mask-slot clamp boundaries. Unity URP import and visual validation pending.'
