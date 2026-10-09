using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    public abstract class ItemSkillCondition : ScriptableObject
    {
        [TextArea] public string FailureMessage = "I can't do that.";
        public abstract bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item);
    }
}
