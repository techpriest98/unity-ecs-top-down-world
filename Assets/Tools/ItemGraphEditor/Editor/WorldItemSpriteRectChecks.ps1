param([string]$MathPath='C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll')
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Add-Type -Path $MathPath
$source=Get-Content (Join-Path $root 'Scripts/Items/WorldItemRenderSystem.cs') -Raw
$rect=[regex]::Match($source,'(?s)internal static Vector4 GetSpriteRect\(.*?\n        \}').Value
$depth=[regex]::Match($source,'(?s)internal static float TrimDepthOffset\(.*?;').Value
if(!$rect -or !$depth){throw 'Production trim geometry helpers not found.'}
$code=@'
using System;
using Unity.Mathematics;
public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
public static class SpriteRectChecks{
RECT
DEPTH
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Run(){
  var anchor=new float2(-16.5f,63.75f);
  foreach(bool upright in new[]{false,true})foreach(var crop in new[]{new float2(32,64),new float2(10,58),new float2(12,50),new float2(7,48)}){
   var canvas=new float2(32,64);var offset=(canvas-crop)*.5f;
   var rect=GetSpriteRect(anchor,canvas,crop,offset,upright);
   Check(math.abs(rect.z*32-crop.x)<.00001f&&math.abs(rect.w*32-crop.y)<.00001f,"Trimmed texels stretched to full canvas");
   float baseLeft=anchor.x-.5f,baseBottom=upright?anchor.y:anchor.y-1;
   Check(math.abs(rect.x-(baseLeft+offset.x/32))<.00001f&&math.abs(rect.y-(baseBottom+offset.y/32))<.00001f,"Lost trim offset/foot registration");
   foreach(float sampleY in new[]{rect.y,rect.y+rect.w*.5f,rect.y+rect.w}){
    float anchorDepth=123.25f;
    float actual=anchorDepth+TrimDepthOffset(rect,anchor.y,!upright)+2f*(sampleY-(rect.y+rect.w*.5f))*(upright?0:1);
    float expected=anchorDepth+(upright?0:2f*(sampleY-anchor.y));
    Check(math.abs(actual-expected)<.0001f,"Cropped ground item left the original Top depth plane");
   }
  }
  var asym=GetSpriteRect(anchor,new float2(32,16),new float2(20,9),new float2(3,6),false);
  Check(math.abs(asym.x-(anchor.x-.5f+3f/32))<.0001f&&math.abs(asym.y-(anchor.y-.25f+6f/32))<.0001f,"Asymmetric trim offset lost");
  var full=GetSpriteRect(anchor,new float2(32,16),new float2(32,16),float2.zero,false);
  Check(full.x==anchor.x-.5f&&full.y==anchor.y-.25f&&full.z==1&&full.w==.5f&&TrimDepthOffset(full,anchor.y,true)==0,"Untrimmed items regressed");
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$MathPath
Add-Type -TypeDefinition $code.Replace('RECT',$rect).Replace('DEPTH',$depth) -ReferencedAssemblies $refs
[SpriteRectChecks]::Run()
if(!$source.Contains('sprite.textureRectOffset') -or !$source.Contains('TrimDepthOffset(rect, projected.y')){throw 'Trim geometry/depth not wired into renderer.'}
$atlas=Get-Content (Join-Path $root 'Scripts/Items/WorldItemSpriteAtlas.cs') -Raw
if(!$atlas.Contains('entry.Uv.z * Texture.width - crop.width') -or !$atlas.Contains('entry.Uv.w * Texture.height - crop.height')){throw 'Saved atlas crop-size validation missing.'}
'PASS production sprite geometry: 10x58/12x50/7x48 cropped torches, full/asymmetric trims, 32 pixels per unit, original foot/centre alignment, grounded depth plane, upright depth and stale-atlas guards. Native Unity visual verification pending.'
