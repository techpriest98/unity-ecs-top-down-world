using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Other Hand Item")]
    public sealed class OtherHandItemCondition : ItemSkillCondition
    {
        public ItemDefinition RequiredItem;
        public override bool ControlsVisibility => true;
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (RequiredItem == null || !manager.Exists(player) || !manager.HasBuffer<PlayerEquipmentSlot>(player) ||
                (slot != EquipmentSlot.LeftHand && slot != EquipmentSlot.RightHand)) return false;
            var slots = manager.GetBuffer<PlayerEquipmentSlot>(player, true);
            var other = slot == EquipmentSlot.LeftHand ? EquipmentSlot.RightHand : EquipmentSlot.LeftHand;
            return PlayerEquipmentUtility.GetItem(slots, slot) == item &&
                PlayerEquipmentUtility.GetItem(slots, other) == RequiredItem.Id;
        }
    }
}
