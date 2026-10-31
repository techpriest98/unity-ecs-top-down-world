$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$atlas = Get-Content (Join-Path $root 'Scripts/Items/WorldItemSpriteAtlas.cs') -Raw
$pack = [regex]::Match($atlas, '(?s)internal static bool TryPack\(.*?(?=        private void ReleaseTexture)').Value
if (!$pack) { throw 'Production atlas packer not found.' }
$adapter = @'
using System;
public struct Vector2Int {public int x,y; public Vector2Int(int a,int b){x=a;y=b;}}
public struct RectInt {public int x,y,width,height;public RectInt(int a,int b,int c,int d){x=a;y=b;width=c;height=d;}}
public static class Mathf {
 public static int Max(int a,int b)=>Math.Max(a,b);
 public static int NextPowerOfTwo(int x){int n=1;while(n<x)n*=2;return n;}
}
public static class AtlasChecks {
 private const int Padding=2;
'@
$tests = @'
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Verify(Vector2Int[] sizes,int limit){
  Check(TryPack(sizes,limit,out var slots,out int side),"Expected packing success");
  Check(side<=limit&&(side&(side-1))==0,"Atlas dimensions invalid");
  for(int i=0;i<sizes.Length;i++){
   var a=slots[i];
   Check(a.width==sizes[i].x&&a.height==sizes[i].y,"Packer rescaled a sprite");
   Check(a.x>=Padding&&a.y>=Padding&&a.x+a.width+Padding<=side&&a.y+a.height+Padding<=side,"Out of bounds or missing padding");
   for(int j=0;j<i;j++){
    var b=slots[j];
    Check(a.x>=b.x+b.width+2*Padding||b.x>=a.x+a.width+2*Padding||a.y>=b.y+b.height+2*Padding||b.y>=a.y+a.height+2*Padding,"Overlapping padded sprites");
   }
   float u=(float)a.x/side,v=(float)a.y/side,w=(float)a.width/side,h=(float)a.height/side;
   Check(u>=0&&v>=0&&u+w<=1&&v+h<=1,"Invalid shader UV rectangle");
  }
 }
 public static void Run(){
  Verify(new Vector2Int[0],4096);
  Verify(new[]{new Vector2Int(32,16)},4096);
  var items=new Vector2Int[512];for(int i=0;i<items.Length;i++)items[i]=new Vector2Int(32,16);Verify(items,4096);
  var random=new Random(42);
  for(int run=0;run<40;run++){
   var sizes=new Vector2Int[100];for(int i=0;i<sizes.Length;i++)sizes[i]=new Vector2Int(random.Next(1,129),random.Next(1,65));Verify(sizes,4096);
  }
  foreach(var invalid in new[]{new Vector2Int(0,16),new Vector2Int(-1,16),new Vector2Int(32,0),new Vector2Int(4096,16)})
   Check(!TryPack(new[]{invalid},4096,out _,out _),"Invalid or oversized sprite accepted");
  Check(!TryPack(new[]{new Vector2Int(60,60),new Vector2Int(60,60)},64,out _,out _),"Overflow accepted");
 }
}
'@
Add-Type -TypeDefinition ($adapter + $pack + $tests)
[AtlasChecks]::Run()
$render = Get-Content (Join-Path $root 'Scripts/Items/WorldItemRenderSystem.cs') -Raw
foreach ($gate in @('spriteAtlas.Ensure(references, savedAtlas)', 'WorldItemAtlasReference', 'spriteAtlas.TryGetSprite(', 'spriteAtlas.TryGet(sprite, out var atlasUv)', 'Uv = atlasUv', 'spriteAtlas.Dispose()')) {
    if (!$render.Contains($gate)) { throw "Missing atlas integration: $gate" }
}
foreach ($gate in @('Graphics.Blit(', 'Graphics.CopyTexture(', 'RenderTexture.ReleaseTemporary(tile)', 'RenderTexture.active = previous', 'GL.sRGBWrite = previousSrgb', 'next == signature', 'ownedTexture.IsCreated()', 'ReleaseTexture();', 'WorldSprite')) {
    if (!$atlas.Contains($gate)) { throw "Missing GPU build/cache/lifecycle guard: $gate" }
}
if ($atlas -match '\.(ReadPixels|GetPixels|GetPixels32|PackTextures)\(') { throw 'Atlas must not read back or rescale sprites.' }
$builder = Get-Content (Join-Path $root 'Tools/ItemGraphEditor/Editor/WorldItemAtlasBuilder.cs') -Raw
foreach ($gate in @('Assets/Game/Build World Item Atlas', 'CustomEditor(typeof(ItemDatabase))', 'Build World Item Atlas', 'AssetDatabase.GenerateUniqueAssetPath', 'atlas.SourceDatabase = database', 'AssetDatabase.AddObjectToAsset(texture, atlas)', 'texture.ReadPixels', 'packed.TryGet(item.WorldSprite', 'EditorApplication.isPlayingOrWillChangePlaymode')) {
    if (!$builder.Contains($gate)) { throw "Missing manual builder workflow: $gate" }
}
if ($atlas.IndexOf('if (saved != null)') -gt $atlas.IndexOf('using var marker = BuildMarker.Auto()')) { throw 'Saved atlases must bypass runtime GPU packing.' }
$baker = Get-Content (Join-Path $root 'Scripts/Items/Authoring/ItemLibraryAuthoring.cs') -Raw
foreach ($gate in @('private WorldItemAtlas atlas', 'DependsOn(atlas)', 'DependsOn(atlas.Texture)', 'ItemDatabase database = atlas.SourceDatabase', 'DependsOn(database)', 'new WorldItemAtlasReference { Atlas = atlas }')) {
    if (!$baker.Contains($gate)) { throw "Missing atlas-rooted authoring dependency: $gate" }
}
if ($baker.Contains('authoring.database') -or $builder.Contains('database.WorldItemAtlas')) { throw 'Database must not own the generated atlas.' }
'PASS production integer atlas packer: empty/single/512 sprites, randomized sizes, padding, no overlap/rescaling, UVs, invalid dimensions and overflow. GPU build/cache/fallback integration gates verified; native GPU pixels not executed.'
