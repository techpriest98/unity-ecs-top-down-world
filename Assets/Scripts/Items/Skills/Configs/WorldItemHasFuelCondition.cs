using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/World Item Has Fuel")]
    public sealed class WorldItemHasFuelCondition : ItemSkillCondition
    {
        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item) => false;
        public override bool EvaluateTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item, Entity target, float3 position) =>
            manager.Exists(target) && manager.HasComponent<WorldItem>(target) && manager.GetComponentData<WorldItem>(target).Instance.Durability > 0f;
    }
}
