using Game.World.Blocks;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
namespace Game.Items
{
    public readonly struct ItemCollisionContext
    {
        public readonly Entity WorldItem;
        public readonly BlockId Block;
        public readonly float3 Position, Normal;
        public readonly float ImpactSpeed;
        public ItemCollisionContext(Entity item, BlockId block, float3 position, float3 normal, float3 velocity)
        {
            WorldItem = item; Block = block; Position = position; Normal = normal;
            ImpactSpeed = math.max(0f, -math.dot(velocity, normal));
        }
    }
    public enum ItemCollisionNodeKind { BlockIs, ReplaceWorldItem }
    [CreateAssetMenu(menuName = "Game/Items/Collision Node")]
    public sealed class ItemCollisionNode : ScriptableObject
    {
        public ItemCollisionNodeKind Kind;
        public BlockId[] Blocks = System.Array.Empty<BlockId>();
        [Min(0)] public float MinImpactSpeed;
        public Game.Items.Skills.ReplaceWorldItemAction Action;
        [HideInInspector] public ItemCollisionNode Next;
        public static bool TryResolve(ItemCollisionNode first, in ItemCollisionContext context,
            out Game.Items.Skills.ReplaceWorldItemAction replacement)
        {
            replacement = null;
            int steps = 0;
            ItemCollisionNode action = null;
            for (var node = first; node != null; node = node.Next)
            {
                if (++steps > 64) return false;
                switch (node.Kind)
                {
                    case ItemCollisionNodeKind.BlockIs:
                        if (float.IsNaN(node.MinImpactSpeed) || float.IsInfinity(node.MinImpactSpeed) || node.MinImpactSpeed < 0 ||
                            float.IsNaN(context.ImpactSpeed) || float.IsInfinity(context.ImpactSpeed) || context.ImpactSpeed < node.MinImpactSpeed) return false;
                        if (action != null || node.Blocks == null || System.Array.IndexOf(node.Blocks, context.Block) < 0) return false;
                        break;
                    case ItemCollisionNodeKind.ReplaceWorldItem:
                        if (action != null || node.Action == null || !node.Action.IsConfigured) return false;
                        action = node;
                        break;
                    default: return false;
                }
            }
            if (action == null) return false;
            replacement = action.Action;
            return true;
        }
    }
}
