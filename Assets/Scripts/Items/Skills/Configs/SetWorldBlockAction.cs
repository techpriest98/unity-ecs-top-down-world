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
        public bool PlacementShapeConnected = true;

        public string GetConfigurationError(EntityManager manager)
        {
            if (BlockSource == BlockValueSource.ItemSelection && !PlacementShapeConnected)
                return "Connect the Shape input to Item.Shape.";
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
                selection.Id > 0 && selection.Id <= byte.MaxValue && System.Enum.IsDefined(typeof(BlockId), (BlockId)selection.Id) &&
                TryPlanPlacement(manager, player, slot, position, out _, out _, out _);
        }

        public bool ExecuteAtPosition(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            if (GetConfigurationError(manager) != null || !CanSetAt(manager, player, slot, item, position)) return false;
            var instance = PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot);
            BlockId block = BlockSource == BlockValueSource.Constant ? ConstantBlock : (BlockId)instance.PlacementSelection.Id;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>());
            if (BlockSource == BlockValueSource.ItemSelection)
            {
                if (!TryPlanPlacement(manager, player, slot, position, out var first, out var second, out bool spills)) return false;
                var requests = manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity());
                requests.Add(first);
                if (spills) requests.Add(second);
            }
            else if (block == BlockId.Air && manager.HasComponent<SelectedProjectedCell>(player) &&
                CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Behind, out var behind) && math.all(behind == position))
            {
                var pending = manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity(), true);
                if (!ReadPending(manager, pending, position, out var contents) ||
                    !ProjectedCellInteractionUtility.TryRemoveSelectedPart(manager.GetComponentData<SelectedProjectedCell>(player),
                        contents, out var removed)) return false;
                manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity()).Add(new BlockModificationRequest(position, removed));
            }
            else manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity()).Add(new BlockModificationRequest(position,
                new BlockData(block, BlockUtility.IsAir(block) ? (byte)0 : byte.MaxValue)));
            return true;
        }

        private bool TryPlanPlacement(EntityManager manager, Entity player, EquipmentSlot slot, int3 position,
            out BlockModificationRequest first, out BlockModificationRequest second, out bool spills)
        {
            first = second = default;
            spills = false;
            if (!PlacementShapeConnected) return false;
            var instance = PlayerEquipmentUtility.GetInstance(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot);
            int startHalf = 0;
            if (manager.HasComponent<SelectedProjectedCell>(player))
            {
                var selected = manager.GetComponentData<SelectedProjectedCell>(player);
                ProjectedCellInteractionUtility.GetBlockPlacementStart(selected, position, out var origin, out startHalf);
                position = origin;
            }
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<BlockModificationQueue>());
            if (query.CalculateEntityCount() != 1) return false;
            if (!manager.HasBuffer<BlockModificationRequest>(query.GetSingletonEntity())) return false;
            var queue = manager.GetBuffer<BlockModificationRequest>(query.GetSingletonEntity(), true);
            if (!ReadPending(manager, queue, position, out CellContents current)) return false;
            int3 above = position + new int3(0, 1, 0);
            bool aboveAvailable = ReadPending(manager, queue, above, out CellContents upper);
            if (!BlockPlacementUtility.TryFill(new BlockPart((BlockId)instance.PlacementSelection.Id, byte.MaxValue),
                    instance.PlacementMode, startHalf, current, upper, aboveAvailable,
                    out var placed, out var placedAbove, out spills)) return false;
            first = new BlockModificationRequest(position, placed);
            if (spills) second = new BlockModificationRequest(above, placedAbove);
            return true;
        }

        private static bool ReadPending(EntityManager manager, DynamicBuffer<BlockModificationRequest> queue,
            int3 position, out CellContents contents)
        {
            if (!CursorWorldUtility.TryGetContents(manager, position, out contents)) return false;
            for (int i = queue.Length - 1; i >= 0; i--)
            {
                var pending = queue[i];
                if (!math.all(pending.WorldPosition == position)) continue;
                contents = pending.HasContents ? pending.Contents : CellContents.FromLegacy(pending.Value);
                break;
            }
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
            if (CursorWorldUtility.TryGetPosition(manager, player, Position, out var targetPosition))
                ExecuteAtPosition(manager, player, sourceSlot, sourceItem, targetPosition);
        }

    }
}
