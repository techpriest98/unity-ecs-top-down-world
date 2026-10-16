using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Actions/Select Block")]
    public sealed class TogglePlacementShapeAction : ItemSkillAction
    {
        public bool ShapeStateConnected = true;
        public bool ItemSelectionConnected = true;
        public override bool IsAvailable(EntityManager manager, Entity player, EquipmentSlot slot, int item) =>
            ShapeStateConnected && ItemSelectionConnected && item != 0 && manager.Exists(player) && manager.HasBuffer<PlayerEquipmentSlot>(player) &&
            PlayerEquipmentUtility.GetItem(manager.GetBuffer<PlayerEquipmentSlot>(player, true), slot) == item;

        public bool OpenPicker(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (!IsAvailable(manager, player, slot, item)) return false;
            return Game.UI.EquipmentHud.OpenBlockPicker(manager, player, slot, item);
        }

        public override void Execute(EntityManager manager, Entity player, EquipmentSlot sourceSlot, int sourceItem) =>
            OpenPicker(manager, player, sourceSlot, sourceItem);
    }
}
