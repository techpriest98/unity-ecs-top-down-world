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

        public bool CanPickAt(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            return ItemSelectionConnected && manager.Exists(player) && manager.HasBuffer<PlayerEquipmentSlot>(player) &&
                item != 0 && PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) == item &&
                CursorWorldUtility.TryGetBlock(manager, position, out var block) && !BlockUtility.IsAir(block) &&
                WithinDistance(manager, player, (float3)position + 0.5f);
        }

        public bool ExecuteAtPosition(EntityManager manager, Entity player, EquipmentSlot slot, int item, int3 position)
        {
            if (!CanPickAt(manager, player, slot, item, position) ||
                !CursorWorldUtility.TryGetBlock(manager, position, out var block)) return false;
            return PlayerEquipmentUtility.SetPlacementSelection(manager.GetBuffer<PlayerEquipmentSlot>(player), slot,
                new ItemPlacementSelection { Kind = PlacementSelectionKind.Block, Id = (int)block });
        }

        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => ItemSelectionConnected && CursorWorldUtility.TryGetPosition(manager, player, Position, out var cell) &&
                CursorWorldUtility.TryGetBlock(manager, cell, out var block) && !BlockUtility.IsAir(block) &&
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
            if (!IsAvailable(manager, player, sourceSlot, sourceItem)) return;
            if (Position == CursorWorldPosition.Disconnected || !ItemSelectionConnected)
                return;
            if (!manager.Exists(player) || !manager.HasComponent<SelectedProjectedCell>(player) ||
                !manager.HasBuffer<PlayerEquipmentSlot>(player))
                return;
            if (!CursorWorldUtility.TryGetPosition(manager, player, Position, out var position) ||
                !CursorWorldUtility.TryGetBlock(manager, position, out var block) || BlockUtility.IsAir(block))
                return;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            if (PlayerEquipmentUtility.GetItem(slots, sourceSlot) != sourceItem)
                return;
            PlayerEquipmentUtility.SetPlacementSelection(slots, sourceSlot, new ItemPlacementSelection
            {
                Kind = PlacementSelectionKind.Block,
                Id = (int)block
            });
        }
    }
}
