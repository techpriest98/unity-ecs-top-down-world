using UnityEngine;
using Unity.Entities;

namespace Game.Items.Skills
{
    public abstract class ItemSkillAction : ScriptableObject
    {
        public ActionDistanceDefinition Distance;

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
