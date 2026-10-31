using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    public abstract class ItemSkillCondition : ScriptableObject
    {
        [TextArea] public string FailureMessage = "I can't do that.";
        public abstract bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item);
        public virtual bool EvaluateTarget(EntityManager manager, Entity player, EquipmentSlot slot, int item,
            Entity target, Unity.Mathematics.float3 position) => Evaluate(manager, player, slot, item);
    }
}
