using UnityEngine;
using Unity.Entities;

namespace Game.Items.Skills
{
    public abstract class ItemSkillAction : ScriptableObject
    {
        public abstract void Execute(
            EntityManager entityManager,
            Entity player,
            EquipmentSlot sourceSlot,
            int sourceItem);
    }
}
