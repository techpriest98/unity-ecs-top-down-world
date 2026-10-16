using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Toggle Placement Shape")]
    public sealed class TogglePlacementShapeAction : ItemSkillAction
    {
        public bool ShapeStateConnected = true;
        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item) =>
            ShapeStateConnected && item != 0 && manager.Exists(player) && manager.HasBuffer<PlayerEquipmentSlot>(player) &&
            PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) == item;

        public bool Toggle(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!IsAvailable(manager, player, slot, item)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player);
            for (int i = 0; i < slots.Length; i++)
            {
                var entry = slots[i];
                if (entry.Slot != slot) continue;
                entry.Instance.PlacementMode = entry.Instance.PlacementMode == BlockPlacementMode.Full
                    ? BlockPlacementMode.Half : BlockPlacementMode.Full;
                slots[i] = entry;
                return true;
            }
            return false;
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem) =>
            Toggle(manager, player, sourceSlot, sourceItem);
    }
}
