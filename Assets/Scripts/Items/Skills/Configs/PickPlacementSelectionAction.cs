using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(fileName = "PickSelection", menuName = "Game/Items/Skills/Actions/Pick Selection")]
    public sealed class PickPlacementSelectionAction : ItemSkillAction
    {
        public CursorWorldPosition Position = CursorWorldPosition.Behind;
        public bool ItemSelectionConnected = true;
        public bool ShapeStateConnected = true;

        public bool CanPickAt(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            return ItemSelectionConnected && ShapeStateConnected && manager.Exists(player) && manager.HasBuffer<PlayerEquipmentSlot>(player) &&
                item != 0 && PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) == item &&
                TryGetMaterial(manager, player, position, out var block) && !BlockUtility.IsAir(block) &&
                WithinDistance(manager, player, (float3)position + 0.5f);
        }

        public bool ExecuteAtPosition(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            if (!CanPickAt(manager, player, slot, item, position) ||
                !TryGetMaterial(manager, player, position, out var block) ||
                !CursorWorldUtility.TryGetContents(manager, position, out var contents)) return false;
            return PlayerEquipmentUtility.SetPlacementSelection(manager.GetBuffer<PlayerEquipmentSlot>(player), slot,
                new ItemPlacementSelection { Kind = PlacementSelectionKind.Block, Id = (int)block }, contents.PlacementMode);
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => ItemSelectionConnected && ShapeStateConnected && CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell) &&
                TryGetMaterial(manager, player, cell, out var block) && !BlockUtility.IsAir(block) &&
                WithinDistance(manager, player, (Unity.Mathematics.float3)cell + new Unity.Mathematics.float3(0.5f, 0.5f, 0.5f));

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
            if (CursorWorldUtility.TryGetPosition(manager, player, Position, out var position))
                ExecuteAtPosition(manager, player, sourceSlot, sourceItem, position);
        }

        private static bool TryGetMaterial(EntityManager manager, Entity player, int3 position, out BlockId block)
        {
            if (!CursorWorldUtility.TryGetBlock(manager, position, out block)) return false;
            if (manager.HasComponent<SelectedProjectedCell>(player) &&
                CursorWorldUtility.TryGetPosition(manager, player, CursorWorldPosition.Behind, out var behind) && math.all(behind == position))
            {
                var selected = manager.GetComponentData<SelectedProjectedCell>(player);
                if (selected.IsValid) block = selected.BlockId;
            }
            return SplitCellUtility.IsKnownMaterial(block);
        }
    }
}
