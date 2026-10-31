using Game.Player;
using Unity.Entities;
using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(menuName = "Game/Items/Skills/Conditions/Tree Resin")]
    public sealed class TreeResinCondition : ItemSkillCondition
    {
        public TreeResinAction Action;

        public override bool Evaluate(EntityManager manager, Entity player, EquipmentSlot slot, int item)
        {
            if (Action == null || !Action.TryFindTarget(manager, player, out var owner, out int treeId, out var surface, out _))
                return false;
            return Action.CanApply(manager, player, slot, item, owner, treeId, surface);
        }
    }
}
