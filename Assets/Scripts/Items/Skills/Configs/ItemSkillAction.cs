using UnityEngine;
using Unity.Entities;

namespace Game.Items.Skills
{
    public abstract class ItemSkillAction : ScriptableObject
    {
        public ActionDistanceDefinition Distance;

        public virtual bool TryGetTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item,
            out Unity.Mathematics.float3 position)
        {
            position = default;
            return false;
        }

        protected bool WithinDistance(EntityManager manager, Entity player, Unity.Mathematics.float3 target)
            => Distance == null || Distance.Allows(manager, player, target);

        public virtual bool IsAvailable(EntityManager manager, Entity player,
            EquipmentSlot sourceSlot, int sourceItem) => true;

        public abstract void Execute(
            EntityManager entityManager,
            Entity player,
            EquipmentSlot sourceSlot,
            int sourceItem);
    }
}
