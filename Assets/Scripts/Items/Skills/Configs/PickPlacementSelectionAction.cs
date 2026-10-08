using Game.Player;
using Game.World.Blocks;
using Game.World.Interaction;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(fileName = "PickSelection", menuName = "Game/Items/Skills/Actions/Pick Selection")]
    public sealed class PickPlacementSelectionAction : ItemSkillAction
    {
        public CursorWorldPosition Position = CursorWorldPosition.Behind;
        public bool ItemSelectionConnected = true;

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem)
        {
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
