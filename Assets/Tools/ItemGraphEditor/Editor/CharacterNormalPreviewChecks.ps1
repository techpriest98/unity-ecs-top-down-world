$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$paletteScript=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/NormalizeTurfNormalPalette.ps1') -Raw
$shim=[regex]::Match($paletteScript,"(?s)\`$shim=@'\r?\n(.*?)\r?\n'@").Groups[1].Value
if(-not $shim){throw 'Palette shim unavailable'}
$picker=Get-Content (Join-Path $root 'Tools/BlockAtlasEditor/Editor/BlockAtlasEditorWindow.cs') -Raw
$body=[regex]::Match($picker,'(?s)private void DrawNormalPicker\(\).*?Color32 encoded;(?<body>.*?)\s*DrawNormalSwatch\(encoded,').Groups['body'].Value
$shim=$shim.Replace('PICKER_BODY',$body).Replace('public static class Mathf {','public static class Mathf { public static int FloorToInt(float f)=>(int)MathF.Floor(f);')
$shim=[regex]::Replace($shim,'(?m)^using System;\r?\n','')
$utility=Get-Content (Join-Path $root 'Tools/CharacterAnimationEditor/Editor/CharacterNormalPreviewUtility.cs') -Raw
$utility=[regex]::Replace($utility,'(?m)^using [^;]+;\r?\n','')
$mathPath='C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$refs=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+$mathPath
$tests=@'
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
public struct Rect {public float x,y,width,height;public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}}
public static class PreviewChecks {
 static void Check(bool v,string s){if(!v)throw new Exception(s);}
 public static void Run(){
  for(int row=0;row<4;row++)for(int col=0;col<3;col++){
   var a=CharacterNormalPreviewUtility.Palette(row,col);var b=TurfPaletteRestriction.Get(row,col);
   Check(a.r==b.r && a.g==b.g && a.b==b.b,"Palette differs from current block picker");
  }
  for(int d=0;d<4;d++)for(int f=0;f<4;f++){
   var rect=new Rect(10,20,256,512);
   int p=CharacterNormalPreviewUtility.PixelIndex(new Vector2(10.5f,20.5f),rect,f,d,256);
   Check(p==((3-d)*128+127)*256+f*64,"Top-left pen maps to wrong frame/direction/row");
   p=CharacterNormalPreviewUtility.PixelIndex(new Vector2(265.5f,531.5f),rect,f,d,256);
   Check(p==((3-d)*128)*256+f*64+63,"Bottom-right pen maps to wrong texel");
  }
  Check(CharacterNormalPreviewUtility.PixelIndex(new Vector2(9,20),new Rect(10,20,256,512),0,0,256)==-1,"Outside pen accepted");
  var white=new float3(1);var up=new float3(0,1,0);var at=new float3(0,1,0);var below=new float3(0,0,0);
  Check(math.length(CharacterNormalPreviewUtility.Point(up,at,below,5,white,true))==0,"Light below hits upward normal");
  Check(CharacterNormalPreviewUtility.Point(-up,at,below,5,white,true).x>.7f,"Light below misses downward normal");
  Check(math.length(CharacterNormalPreviewUtility.Point(up,at,at,5,white,true))==0,"Coincidence becomes full light");
  Check(math.length(CharacterNormalPreviewUtility.Point(-up,at,below,.5f,white,true))==0,"Light exceeds radius");
 }
}
'@
Add-Type -ReferencedAssemblies $refs -TypeDefinition ("using System; using Unity.Mathematics;`n"+$shim+"`n"+$utility+"`n"+$tests)
[PreviewChecks]::Run()
'PASS actual preview palette equals all 12 current block swatches; brush frame/Y coordinates; below-light response, coincidence, radius.'
