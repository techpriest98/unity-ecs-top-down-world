$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source=Get-Content (Join-Path $root 'Scripts/Items/Configs/ItemCollisionNode.cs') -Raw
$method=[regex]::Match($source,'(?s)public static bool TryResolve\(.*?\n        \}').Value
if (!$method) {throw 'Collision resolver missing'}
$adapter=@'
public enum BlockId {Stone,Sand}
public enum ItemCollisionNodeKind {BlockIs,ReplaceWorldItem}
namespace Game.Items.Skills { public class ReplaceWorldItemAction { public object ReplacementItem; public int Count; public bool IsConfigured=>ReplacementItem!=null && Count>0; } }
public class ItemDefinition {}
public struct ItemCollisionContext {public BlockId Block; public float ImpactSpeed;}
public class ItemCollisionNode {
 public ItemCollisionNodeKind Kind;
 public BlockId[] Blocks;
 public float MinImpactSpeed;
 public Game.Items.Skills.ReplaceWorldItemAction Action;
 public ItemCollisionNode Next;
'@
$tests=@'
public static void Run(){
 var result=new ItemDefinition();
 var action=new ItemCollisionNode{Kind=ItemCollisionNodeKind.ReplaceWorldItem,Action=new Game.Items.Skills.ReplaceWorldItemAction{ReplacementItem=result,Count=2}};
 var condition=new ItemCollisionNode{Blocks=new[]{BlockId.Stone},Next=action};
 var hit=new ItemCollisionContext{Block=BlockId.Stone};
 if(!TryResolve(condition,hit,out var r)||r!=action.Action||r.ReplacementItem!=result||r.Count!=2)throw new System.Exception("Stone -> two shards failed");
 if(TryResolve(condition,new ItemCollisionContext{Block=BlockId.Sand},out _))throw new System.Exception("Sand accepted");
 if(TryResolve(null,hit,out _))throw new System.Exception("Unconnected event accepted");
 condition.MinImpactSpeed=10.5f;
 hit.ImpactSpeed=10f;
 if(TryResolve(condition,hit,out _))throw new System.Exception("Soft table impact accepted");
 hit.ImpactSpeed=11.5f;
 if(!TryResolve(condition,hit,out _))throw new System.Exception("Hard floor impact rejected");
 hit.ImpactSpeed=10.5f;
 if(!TryResolve(condition,hit,out _))throw new System.Exception("Threshold boundary rejected");
 condition.MinImpactSpeed=float.NaN;
 if(TryResolve(condition,hit,out _))throw new System.Exception("Invalid threshold accepted");
 condition.MinImpactSpeed=0;
 action.Next=condition;
 if(TryResolve(condition,hit,out _))throw new System.Exception("Cycle accepted");
 action.Next=null;action.Action.Count=0;
 if(TryResolve(condition,hit,out _))throw new System.Exception("Zero count accepted");
 action.Action.Count=2;action.Action.ReplacementItem=null;
 if(TryResolve(condition,hit,out _))throw new System.Exception("Missing result accepted");
}
}
'@
Add-Type -TypeDefinition ($adapter+$method+$tests)
[ItemCollisionNode]::Run()
'PASS extracted collision resolver: stone, sand, two results, unconnected event, cycle, invalid count and missing result. Unity graph UI/physics integration not executed.'
