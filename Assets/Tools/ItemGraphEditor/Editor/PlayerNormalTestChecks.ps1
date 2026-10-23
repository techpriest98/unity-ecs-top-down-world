$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
# Reuse the headless D3D compiler; its own item-shader checks run first.
. (Join-Path $PSScriptRoot 'WorldItemBatchShaderChecks.ps1')
$shader=Get-Content (Join-Path $root 'Shaders/PlayerNormalTest.shader') -Raw
if($shader -notmatch '"Queue"="AlphaTest"' -or $shader -notmatch 'Blend Off' -or
 $shader -notmatch 'ZWrite On' -or $shader -notmatch 'ZTest LEqual'){
 throw 'Player parts must use alpha-tested depth writes, not transparent sorting'
}
if([regex]::Matches($shader,'clip\(color.a - max\(_Cutoff, \.0001\)\)').Count -ne 1){
 throw 'Shared atlas sampling must reject transparent pixels before either lighting path'
}
'PASS player cutout/depth state and shared atlas alpha rejection.'
if($shader -match 'lerp\(1,\s*saturate\(dot' -or $shader -match '_BodyNormalLocalDirection'){
 throw 'Old isotropic-gradient fallback returned'
}
$pipeline=Get-Content (Join-Path $root 'Settings/UniversalRP.asset') -Raw
$rendererMeta=Get-Content (Join-Path $root 'Settings/Renderer2D.asset.meta') -Raw
$rendererGuid=[regex]::Match($rendererMeta,'guid: ([0-9a-f]+)').Groups[1].Value
if($pipeline -notmatch $rendererGuid){throw 'Active renderer changed; review player pass compatibility'}
$rendererSource=Get-ChildItem (Join-Path $root '../Library/PackageCache') -Filter 'DrawRenderer2DPass.cs' -Recurse | Select-Object -First 1
if(-not $rendererSource){throw 'Renderer2D pass source unavailable'}
$rendererText=Get-Content $rendererSource.FullName -Raw
$pass=[regex]::Match($shader,'"LightMode"\s*=\s*"([^"]+)"').Groups[1].Value
if($rendererText -notmatch ('new ShaderTagId\("'+[regex]::Escape($pass)+'"\)')){
 throw "Player shader pass '$pass' is not accepted by the installed Renderer2D"
}
"PASS player LightMode '$pass' accepted by installed active Renderer2D."
$program=[regex]::Match($shader,'(?s)HLSLPROGRAM(.*?)ENDHLSL').Groups[1].Value
$program=[regex]::Replace($program,'(?m)^\s*#include[^\r\n]*','')
$adapter=@'
#define TEXTURE2D(n) Texture2D<float4> n
#define TEXTURE2D_ARRAY(n) Texture2DArray<float4> n
#define SAMPLE_TEXTURE2D(t,s,u) t.Sample(s,u)
#define SAMPLE_TEXTURE2D_ARRAY(t,s,u,p) t.Sample(s,float3(u,p))
#define CBUFFER_START(n) cbuffer n {
#define CBUFFER_END };
#define UNITY_VERTEX_INPUT_INSTANCE_ID
#define UNITY_VERTEX_OUTPUT_STEREO
#define UNITY_SETUP_INSTANCE_ID(n)
#define UNITY_TRANSFER_INSTANCE_ID(a,b)
#define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(n)
#define UNITY_DOTS_INSTANCING_START(n)
#define UNITY_DOTS_INSTANCING_END(n)
#define UNITY_DOTS_INSTANCED_PROP(t,n)
#define UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(t,n) n
float4 TransformObjectToHClip(float3 p){return float4(p,1);}
'@
foreach($variant in @('',"#define UNITY_DOTS_INSTANCING_ENABLED 1`n")){
 [ItemBatchShaderCompile]::Compile(($variant+$adapter+$program),'Vert','vs_5_0')
 [ItemBatchShaderCompile]::Compile(($variant+$adapter+$program),'Frag','ps_5_0')
}
Add-Type -Path 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
$files=@('Scripts/Player/Visual/Components/PlayerNormalTest.cs','Scripts/World/Rendering/ViewDirection.cs','Scripts/World/Rendering/ViewDirectionUtility.cs',
 'Scripts/Player/Lighting/PlayerHeldLightPositionUtility.cs','Scripts/Player/Visual/PlayerSpriteDirectionUtility.cs','Scripts/Player/Visual/Components/PlayerFacing.cs','Scripts/Items/Configs/EquipmentSlot.cs')
$usings=@();$bodies=foreach($f in $files){$s=Get-Content (Join-Path $root $f) -Raw;$usings += [regex]::Matches($s,'(?m)^using [^;]+;')|ForEach-Object{$_.Value};[regex]::Replace($s,'(?m)^using [^;]+;\r?\n','')}
$harness=@'
namespace Unity.Entities {public interface IComponentData{} public interface IBufferElementData{}
 public class InternalBufferCapacityAttribute:System.Attribute{public InternalBufferCapacityAttribute(int capacity){}}}
namespace Unity.Rendering {public class MaterialPropertyAttribute:System.Attribute{public MaterialPropertyAttribute(string name){}}}
public static class PlayerNormalChecks {
 static void Check(bool value,string message){if(!value)throw new System.Exception(message);}
 public static string Run(){
  foreach(float depth in new[]{-.65f,-.35f,0f,.35f,.65f}) {
   float plane=Game.Player.PlayerNormalTestUtility.EmitterSurfaceDepth(depth,2);
   Check(math.abs((depth-plane)-2f/98)<.00001,"Emitter-to-surface spacing depends on draw order/depth sign");
   var emissionPoint=new float4(0,1.5f,depth,.1f);
   var lit=EvaluatePointLight(new float3(0,0,1),new float3(0,1.5f,plane),emissionPoint,new float3(1));
   Check(math.cmin(lit)>.5f,"Small-radius emitter misses its own anchored sprite plane");
  }
  var cleanAlbedo=new float3(.8f,.7f,.6f);var paintedAlbedo=new float3(.3f,.4f,.5f);
  Check(math.distance(Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,paintedAlbedo,float3.zero),paintedAlbedo)<.00001,"Painted map missing in shade");
  foreach(float blend in new[]{-1f,0f,.25f,.5f,1f,2f})
   Check(math.distance(Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,paintedAlbedo,float3.zero,blend),math.lerp(cleanAlbedo,paintedAlbedo,math.saturate(blend)))<.00001,"Painted blend strength/clamping incorrect");
  Check(math.distance(Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,paintedAlbedo,new float3(.5f)),cleanAlbedo)<.00001,"Painted map remains under strong direct light");
  Check(math.distance(Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,cleanAlbedo,new float3(.1f)),cleanAlbedo)<.00001,"Absent painted map changes diffuse");
  Check(math.length(Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,paintedAlbedo,float3.zero)*float3.zero)==0,"Painted map emits light in complete darkness");
  foreach(float amount in new[]{0f,.05f,.175f,.35f,1f})
   Check(math.distance(ShaderShadeAlbedo(cleanAlbedo,paintedAlbedo,new float3(amount)),Game.Player.PlayerNormalTestUtility.ShadeAlbedo(cleanAlbedo,paintedAlbedo,new float3(amount)))<.00001,"Runtime shader and Preview painted blend differ");
  var surface=new Unity.Mathematics.float3(0,1,0);
  var below=new Unity.Mathematics.float4(0,0,0,8);
  Check(Unity.Mathematics.math.length(EvaluatePointLight(new Unity.Mathematics.float3(0,1,0),surface,below,new Unity.Mathematics.float3(1)))==0,"Light below illuminates upward-facing surface");
  Check(EvaluatePointLight(new Unity.Mathematics.float3(0,-1,0),surface,below,new Unity.Mathematics.float3(1)).x>.8f,"Light below fails to illuminate downward-facing surface");
  Check(Unity.Mathematics.math.length(EvaluatePointLight(new Unity.Mathematics.float3(0,1,0),surface,new Unity.Mathematics.float4(surface,8),new Unity.Mathematics.float3(1)))==0,"Coincident light causes full-bright fallback");
  Check(Unity.Mathematics.math.length(EvaluatePointLight(new Unity.Mathematics.float3(0,-1,0),surface,new Unity.Mathematics.float4(0,-20,0,8),new Unity.Mathematics.float3(1)))==0,"Point light exceeds radius");
  var feet=new Unity.Mathematics.float3(-3.25f,7.5f,12.8f);
  foreach(Game.Player.PlayerSpriteDirection d in System.Enum.GetValues(typeof(Game.Player.PlayerSpriteDirection))){
   var raw=new float3(12,90,.65f);
   var converted=Game.Player.PlayerHeldLightPositionUtility.RuntimeAnchor(raw,d);
   Check(converted.x==raw.x && converted.y==raw.y && converted.z==raw.z,"Runtime anchor must preserve stored XYZ in every direction");
   Check(math.distance(Game.Player.PlayerHeldLightPositionUtility.RuntimeAnchor(converted,d),raw)==0,"Automatic override initialization is not inverse of runtime Z conversion");
  }
  Check(math.distance(Game.Player.PlayerHeldLightPositionUtility.FromSpriteAnchor(feet,2,new float3(32,25,0),Game.World.Rendering.ViewDirection.Front),feet)<.00001,"Anchor feet baseline mismatch");
  foreach(Game.World.Rendering.ViewDirection v in System.Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))){
   var anchor=new float3(45,94,.4f);
   var world=Game.Player.PlayerHeldLightPositionUtility.FromSpriteAnchor(feet,2,anchor,v);
   var local=Game.Player.PlayerNormalTestUtility.SpriteDirection(world-feet,v);
   Check(math.distance(local,new float3(13f*2/98,69f*2/98,.4f))<.00001,"Anchor camera-basis or depth mismatch");
  }
  var expectedLeft=new[]{new Unity.Mathematics.float3(.65f,1.5f,.35f),new Unity.Mathematics.float3(-.65f,1.5f,-.35f),
   new Unity.Mathematics.float3(.35f,1.5f,.65f),new Unity.Mathematics.float3(-.35f,1.5f,-.65f)};
  foreach(Game.Player.PlayerFacingDirection facing in System.Enum.GetValues(typeof(Game.Player.PlayerFacingDirection))){
   var left=Game.Player.PlayerHeldLightPositionUtility.Position(feet,2,facing,Game.Items.EquipmentSlot.LeftHand);
   var right=Game.Player.PlayerHeldLightPositionUtility.Position(feet,2,facing,Game.Items.EquipmentSlot.RightHand);
   Check(Unity.Mathematics.math.distance(left-feet,expectedLeft[(int)facing])<.00001,"Held light on wrong world side");
   Check(System.Math.Abs(Unity.Mathematics.math.distance(left,right)-1.3f)<.00001,"Hand emitters overlap or change separation");
   var moved=Game.Player.PlayerHeldLightPositionUtility.Position(feet+new Unity.Mathematics.float3(2,3,-4),2,facing,Game.Items.EquipmentSlot.LeftHand);
   Check(Unity.Mathematics.math.distance(moved-left,new Unity.Mathematics.float3(2,3,-4))<.00001,"Emitter did not follow world movement");
  }
  foreach(Game.World.Rendering.ViewDirection view in System.Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))){
   foreach(Game.Player.PlayerFacingDirection facing in System.Enum.GetValues(typeof(Game.Player.PlayerFacingDirection))){
    var d=Game.Player.PlayerSpriteDirectionUtility.WorldToScreen(facing,view);
    var l=Game.Player.PlayerNormalTestUtility.SpriteDirection(Game.Player.PlayerHeldLightPositionUtility.Position(feet,2,facing,Game.Items.EquipmentSlot.LeftHand,view)-feet,view);
    var r=Game.Player.PlayerNormalTestUtility.SpriteDirection(Game.Player.PlayerHeldLightPositionUtility.Position(feet,2,facing,Game.Items.EquipmentSlot.RightHand,view)-feet,view);
    if(d==Game.Player.PlayerSpriteDirection.Right)Check(l.z>0 && r.z<0,"Facing right must put left hand nearer camera");
    if(d==Game.Player.PlayerSpriteDirection.Left)Check(l.z<0 && r.z>0,"Facing left must put right hand nearer camera");
   }
   var right=Game.World.Rendering.ViewDirectionUtility.Right(view);
   var toward=Game.World.Rendering.ViewDirectionUtility.Forward(view);
   var x=Game.Player.PlayerNormalTestUtility.SpriteDirection(new Unity.Mathematics.float3(right.x,0,right.y),view);
   var z=Game.Player.PlayerNormalTestUtility.SpriteDirection(new Unity.Mathematics.float3(toward.x,0,toward.y),view);
   Check(Unity.Mathematics.math.distance(x,new Unity.Mathematics.float3(1,0,0))<.0001,"Wrong right basis");
   Check(Unity.Mathematics.math.distance(z,new Unity.Mathematics.float3(0,0,1))<.0001,"Wrong toward-camera basis");
  }
  for(int direction=0;direction<4;direction++)for(int frame=0;frame<2;frame++){
   var size=new Unity.Mathematics.float2(31f/64,98f/128);
   var offset=new Unity.Mathematics.float2((18f+15.5f)/64-.5f,(25f+49f)/128-.5f);
   var uv=Game.Player.PlayerNormalTestUtility.SheetUv(size,offset,frame,direction);
   var expected=new Unity.Mathematics.float4(31f/128,98f/512,(frame*64+18f)/128,((3-direction)*128+25f)/512);
   Check(Unity.Mathematics.math.distance(uv,expected)<.00001,"Source-sheet UV differs from atlas trimmed rectangle");
  }
  return "PASS extracted point-light math: below/up/down response, coincidence and radius. Utilities: hand positions, movement, 8 frame UVs and 4 camera bases.";
 }
}
'@
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
$pointFunction=[regex]::Match($shader,'(?s)float3 EvaluatePointLight\([^}]+\}').Value
if(-not $pointFunction){throw 'Point light function not found'}
$pointFunction=$pointFunction.Replace('float3 EvaluatePointLight','static float3 EvaluatePointLight')
$pointFunction=[regex]::Replace($pointFunction,'\b(length|max|saturate|dot)\(', 'math.$1(')
$pointFunction=[regex]::Replace($pointFunction,'(?<![\w.])(\.\d+)(?![\w.])','$1f')
$harness=$harness.Replace('public static class PlayerNormalChecks {',"public static class PlayerNormalChecks {`n"+$pointFunction)
$sourceText=(($usings|Select-Object -Unique)-join "`n")+"`n"+($bodies -join "`n")+"`n"+$harness
$shadeFunction=[regex]::Match($shader,'(?s)float3 ShadeAlbedo\([^}]+\}').Value
if(-not $shadeFunction){throw 'Shader shade blend not found'}
$shadeFunction=$shadeFunction.Replace('float3 ShadeAlbedo','static float3 ShaderShadeAlbedo')
$shadeFunction=$shadeFunction.Replace('_PaintedBlend', '1f')
$shadeFunction=[regex]::Replace($shadeFunction,'\b(dot|smoothstep|lerp|saturate)\(', 'math.$1(')
$shadeFunction=[regex]::Replace($shadeFunction,'\bfloat3\(', 'new float3(')
$shadeFunction=[regex]::Replace($shadeFunction,'(?<![\w.])(\.\d+)(?![\w.])','$1f')
$sourceText=$sourceText.Replace('public static class PlayerNormalChecks {',"public static class PlayerNormalChecks {`n"+$shadeFunction)
Add-Type -ReferencedAssemblies $refs -TypeDefinition $sourceText
[PlayerNormalChecks]::Run()
'HLSL compiled with transform/instancing adapters; actual Unity DOTS variant import and visual lighting still require Play Mode.'
$depthBridge = Get-Content (Join-Path $root 'Scripts/Player/Lighting/PlayerNormalTestRenderSystem.cs') -Raw
$visualBaker = Get-Content (Join-Path $root 'Scripts/Player/Visual/PlayerVisualAuthoring.cs') -Raw
$previewDepth = Get-Content (Join-Path $root 'Tools/CharacterAnimationEditor/Editor/CharacterAnimationNormalPreview.cs') -Raw
if ($shader -notmatch 'UNITY_DOTS_INSTANCED_PROP\(float, _SpriteLightingDepth\)' -or
    $shader -notmatch 'pixelScale, PLAYER_PROP\(float, _SpriteLightingDepth\)' -or
    $depthBridge -notmatch 'position.ValueRO.Value - light.Feet' -or
    $depthBridge -notmatch 'parts\[p\].Part == held.ValueRO.VisualPart' -or
    $depthBridge -notmatch 'new PlayerSpriteLightingDepth\(\)' -or
    $visualBaker -notmatch 'AddComponent\(entity, new PlayerSpriteLightingDepth\(\)\)' -or
    $previewDepth -notmatch 'PlayerNormalTestUtility.EmitterSurfaceDepth\(offset.z,previewHeight\)' -or
    $previewDepth -notmatch 'previewLightingDepths\[index\]') {
    throw 'Emitter-backed item lighting depth must share the current light anchor, reset when unequipped and match preview without modifying draw order'
}
'PASS item lighting-depth bridge, instanced property, stale-depth reset and preview parity contracts.'
