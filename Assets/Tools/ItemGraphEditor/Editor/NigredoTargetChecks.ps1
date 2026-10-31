param([string]$MathPath = 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/Managed/UnityEngine/UnityEngine.MathematicsModule.dll')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Get-Content (Join-Path $root 'Scripts/Items/Skills/Configs/SetWorldBlockAction.cs') -Raw
$method = [regex]::Match($source, '(?s)        internal static bool Covers\(.*?\n        \}')
if (!$method.Success) { throw 'Placement coverage helper missing.' }
Add-Type -Path $MathPath
$adapter = @'
using System;
using Unity.Mathematics;
namespace NigredoCoverageChecks {
public enum BlockId { Air, Stone }
public struct Part { public BlockId Id; }
public struct CellContents {
 public Part Lower, Upper;
 public static CellContents FromLegacy(BlockId id) => new CellContents { Lower=new Part{Id=id},Upper=new Part{Id=id} };
}
public struct BlockModificationRequest {public int3 WorldPosition;public bool HasContents;public CellContents Contents;public BlockId Value;}
public static class BlockUtility {public static bool IsAir(BlockId id)=>id==BlockId.Air;}
public static class Checks {
'@
$tests = @'
 public static void Run() {
  var p=new int3(-17,64,-33);
  var request=new BlockModificationRequest {WorldPosition=p,HasContents=true,
   Contents=new CellContents {Lower=new Part{Id=BlockId.Stone}}};
  if(!Covers(request,(float3)p+new float3(.5f,0,.5f)))throw new Exception("Item at base not removed");
  if(Covers(request,(float3)p+new float3(.5f,.5f,.5f)))throw new Exception("Item atop lower half removed");
  request.Contents.Upper.Id=BlockId.Stone;
  if(!Covers(request,(float3)p+new float3(.5f,.5f,.5f)))throw new Exception("Item in upper half retained");
  foreach(var offset in new[]{new float3(1,0,.5f),new float3(-.1f,0,.5f),new float3(.5f,1,.5f),new float3(.5f,-.01f,.5f)})
   if(Covers(request,(float3)p+offset))throw new Exception("Neighbour or surface-top item removed");
  request.Contents.Lower.Id=request.Contents.Upper.Id=BlockId.Air;
  if(Covers(request,(float3)p+new float3(.5f,0,.5f)))throw new Exception("Air removed item");
 }
}}
'@
$refs = @((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName) + $MathPath
Add-Type -TypeDefinition ($adapter + $method.Value + $tests) -ReferencedAssemblies $refs
[NigredoCoverageChecks.Checks]::Run()
$target = Get-Content (Join-Path $root 'Scripts/Items/Skills/Configs/DestroyWorldObjectAction.cs') -Raw
$resolve = [regex]::Match($target, '(?s)public static int Resolve\(.*?\n        \}').Value
if ($resolve -notmatch 'leftShiftKey.isPressed.*rightShiftKey.isPressed' -or
    $resolve -match 'PlacementSelection' -or
    $resolve.IndexOf('before, false') -gt $resolve.IndexOf('before, true')) { throw 'Automatic priority or Shift routing changed.' }
$graph = Get-Content (Join-Path $root 'Configs/Items/WorldShaper/Skills/NigredoSelectionKind.asset') -Raw
if ($graph -notmatch '(?m)^  Kind: 16\r?$') { throw 'Nigredo not wired to automatic target branch.' }
'PASS actual coverage helper with cell adapters: full/lower-half, negative coordinates, neighbouring cells, top surface, air; static automatic routing/Shift wiring. Unity gameplay not executed.'
