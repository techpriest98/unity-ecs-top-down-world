using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Place World Item")]
    public sealed class PlaceWorldItemCondition : ItemSkillCondition
    {
        public PlaceWorldItemAction Action;
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item) =>
            Action != null && Action.TryGetTarget(manager, player, slot, item, out _);
        public override bool EvaluateTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position) =>
            Action != null && Action.CanPlace(manager, player, slot, item, position);
    }
}
