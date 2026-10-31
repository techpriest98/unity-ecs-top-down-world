using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Refuel World Item")]
    public sealed class RefuelWorldItemCondition : ItemSkillCondition
    {
        public RefuelWorldItemAction Action;
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item) => false;
        public override bool EvaluateTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position) =>
            Action != null && Action.CanInspect(manager, player, slot, item, target, position);
    }
}
