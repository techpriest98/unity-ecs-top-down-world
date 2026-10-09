using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    public enum BlockValueSource { Disconnected = -1, Constant = 0, ItemSelection = 1 }

    [CreateAssetMenu(fileName = "SetWorldBlock", menuName = "Game/Items/Skills/Actions/Set World Block")]
    public sealed class SetWorldBlockAction : ItemSkillAction
    {
        public CursorWorldPosition Position = CursorWorldPosition.Before;
        public BlockValueSource BlockSource = BlockValueSource.ItemSelection;
        public BlockId ConstantBlock = BlockId.Air;

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => Distance == null || (CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell) &&
                WithinDistance(manager, player, (Unity.Mathematics.float3)cell + new Unity.Mathematics.float3(0.5f, 0.5f, 0.5f)));

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem)
        {
            if (!IsAvailable(manager, player, sourceSlot, sourceItem)) return;
            if (Position == CursorWorldPosition.Disconnected ||
                (BlockSource != BlockValueSource.Constant && BlockSource != BlockValueSource.ItemSelection))
                return;
            if (!manager.Exists(player) || !manager.HasComponent<SelectedProjectedCell>(player) ||
                !manager.HasBuffer<PlayerEquipmentSlot>(player))
                return;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player, true);
            var instance = PlayerEquipmentUtility.GetInstance(slots, sourceSlot);
            if (instance.DefinitionId != sourceItem)
                return;
            BlockId block = ConstantBlock;
            if (BlockSource == BlockValueSource.ItemSelection)
            {
                var selection = instance.PlacementSelection;
                if (selection.Kind != PlacementSelectionKind.Block || selection.Id <= 0 || selection.Id > byte.MaxValue ||
                    !System.Enum.IsDefined(typeof(BlockId), (BlockId)selection.Id))
                    return;
                block = (BlockId)selection.Id;
            }
            if (!CursorWorldUtility.TryGetPosition(manager, player, Position, out var position))
                return;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>());
            if (query.CalculateEntityCount() != 1)
                return;
            var queue = query.GetSingletonEntity();
            if (!manager.HasBuffer<BlockModificationRequest>(queue))
                return;
            manager.GetBuffer<BlockModificationRequest>(queue).Add(new BlockModificationRequest(position,
                new BlockData(block, BlockUtility.IsAir(block) ? (byte)0 : byte.MaxValue)));
        }
    }
}
