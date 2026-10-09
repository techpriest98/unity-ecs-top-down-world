using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Within Distance")]
    public sealed class WithinDistanceCondition : ItemSkillCondition
    {
        public ActionDistanceDefinition Distance;
        public ItemSkillAction Target;
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item)
            => Distance != null && Target != null && Target.TryGetTarget(manager, player, slot, item, out var position)
                && Distance.Allows(manager, player, position);
    }
}
