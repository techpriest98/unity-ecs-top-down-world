$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$mathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll'
Add-Type -Path $mathPath
$references = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $mathPath
$shader = Get-Content (Join-Path $root 'Shaders/ChunkProcedural.shader') -Raw
$function = [regex]::Match($shader, '(?s)float3 GetWorldNormal\(float3 normalOS\)\s*\{.*?\n\s*\}')
if (!$function.Success) { throw 'Shader normal transform not found.' }
# Execute the actual shader expression with Unity.Mathematics, not a duplicate basis formula.
$method = $function.Value -replace '\bnormalize\(', 'math.normalize(' -replace '\bcross\(', 'math.cross(' -replace '\bfloat3\(', 'new float3('
$method = $method -replace '\b(\d+\.\d+)\b', '$1f'
$view = Get-Content (Join-Path $root 'Scripts/World/Rendering/ViewDirection.cs') -Raw
$utility = (Get-Content (Join-Path $root 'Scripts/World/Rendering/ViewDirectionUtility.cs') -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$harness = @'
public static class NormalBasisChecks {
 static float3 _SideFaceNormal;
 TRANSFORM
 static void Check(bool ok,string message) {if(!ok)throw new System.Exception(message);}
 public static string Run() {
  foreach(Game.World.Rendering.ViewDirection view in System.Enum.GetValues(typeof(Game.World.Rendering.ViewDirection))) {
   int2 f=Game.World.Rendering.ViewDirectionUtility.Forward(view);
   int2 r=Game.World.Rendering.ViewDirectionUtility.Right(view);
   _SideFaceNormal=new float3(f.x,0,f.y);
   float3 right=new float3(r.x,0,r.y), up=new float3(0,1,0);
   Check(math.dot(GetWorldNormal(new float3(1,0,0)),right)>.999f,view+": atlas +X points screen-left");
   Check(math.dot(GetWorldNormal(up),up)>.999f,view+": vertical direction changed");
   Check(math.dot(GetWorldNormal(new float3(0,0,1)),_SideFaceNormal)>.999f,view+": outward direction changed");
   // Actual picker colors: left/right slopes of a raised stone, not a depression.
   float3 leftNormal=math.normalize(new float3(37,128,218)/127.5f-1);
   float3 rightNormal=math.normalize(new float3(218,128,218)/127.5f-1);
   foreach(int sign in new[]{-1,1}) {
    float3 light=math.normalize(_SideFaceNormal+right*sign);
    float left=math.dot(GetWorldNormal(leftNormal),light);
    float rightValue=math.dot(GetWorldNormal(rightNormal),light);
    Check(sign<0?left>rightValue:rightValue>left,view+": near slope darker than far slope");
    // Local lighting subtracts the same reference dot product; ordering must survive.
    float reference=math.dot(GetWorldNormal(new float3(0,0,1)),light);
    float a=math.clamp(1+(left-reference)*.8f,.35f,1.65f);
    float b=math.clamp(1+(rightValue-reference)*.8f,.35f,1.65f);
    Check(sign<0?a>b:b>a,view+": local normal response mirrored");
   }
  }
  return "Normal basis PASS: actual shader transform, all four views, left/right raised-stone slopes, directional/local response, unchanged up/outward axes. GPU rendering not tested.";
 }
}
'@
$harness = $harness.Replace('TRANSFORM','static '+$method)
Add-Type -TypeDefinition ("using Unity.Mathematics;`n"+$view+"`n"+$utility+"`n"+$harness) -ReferencedAssemblies $references
[NormalBasisChecks]::Run()
