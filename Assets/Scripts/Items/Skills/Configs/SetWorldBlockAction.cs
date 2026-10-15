using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Unity.Entities;
using Unity.Mathematics;
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

        public string GetConfigurationError(EntityManager manager)
        {
            if (BlockSource != BlockValueSource.Constant && BlockSource != BlockValueSource.ItemSelection)
                return "Block input is not connected.";
            if (BlockSource == BlockValueSource.Constant && !System.Enum.IsDefined(typeof(BlockId), ConstantBlock))
                return "Constant Block is invalid.";
            using var queue = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>());
            return queue.CalculateEntityCount() == 1 && manager.HasBuffer<BlockModificationRequest>(queue.GetSingletonEntity())
                ? null : "Expected one block modification queue with a request buffer.";
        }

        public bool CanSetAt(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            if (!manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) != item ||
                !CursorWorldUtility.TryGetBlock(manager, position, out _) ||
                !WithinDistance(manager, player, (float3)position + 0.5f)) return false;
            if (BlockSource == BlockValueSource.Constant) return true;
            var selection = PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot).PlacementSelection;
            return BlockSource == BlockValueSource.ItemSelection && selection.Kind == PlacementSelectionKind.Block &&
                selection.Id > 0 && selection.Id <= byte.MaxValue && System.Enum.IsDefined(typeof(BlockId), (BlockId)selection.Id);
        }

        public bool ExecuteAtPosition(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            if (GetConfigurationError(manager) != null || !CanSetAt(manager, player, slot, item, position)) return false;
            var instance = PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot);
            BlockId block = BlockSource == BlockValueSource.Constant ? ConstantBlock : (BlockId)instance.PlacementSelection.Id;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>());
            manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity()).Add(new BlockModificationRequest(position,
                new BlockData(block, BlockUtility.IsAir(block) ? (byte)0 : byte.MaxValue)));
            return true;
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!TryGetTarget(manager, player, slot, item, out var target) || !WithinDistance(manager, player, target)) return false;
            if (BlockSource == BlockValueSource.Constant) return true;
            if (BlockSource != BlockValueSource.ItemSelection || !manager.HasBuffer<PlayerEquipmentSlot>(player)) return false;
            var selection = PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot).PlacementSelection;
            return selection.Kind == PlacementSelectionKind.Block && selection.Id > 0 && selection.Id <= byte.MaxValue &&
                System.Enum.IsDefined(typeof(BlockId), (BlockId)selection.Id);
        }

        public override bool TryGetTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item,
            out Unity.Mathematics.float3 position)
        {
            position = default;
            if (!CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell)) return false;
            position = (Unity.Mathematics.float3)cell + 0.5f;
            return true;
        }

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
