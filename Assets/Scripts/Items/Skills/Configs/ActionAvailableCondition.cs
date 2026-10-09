using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    // Reuses the action's non-mutating validation; the action still validates
    // again at execution to protect against state changes in a sequence.
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Action Available")]
    public sealed class ActionAvailableCondition : ItemSkillCondition
    {
        public ItemSkillAction Action;
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => Action != null && Action.IsAvailable(manager, player, slot, item);
    }
}
