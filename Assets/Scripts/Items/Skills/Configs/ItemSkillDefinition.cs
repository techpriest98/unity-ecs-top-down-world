using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(
        fileName = "ItemSkill",
        menuName = "Game/Items/Skills/Definition")]
    public sealed class ItemSkillDefinition : ScriptableObject
    {
        public int Id;
        public string DisplayName;
        public string Description;
        public Sprite Icon;
        public ItemSkillAction[] Actions = System.Array.Empty<ItemSkillAction>();

        public bool HasActions
        {
            get
            {
                if (Actions != null)
                    foreach (var action in Actions)
                        if (action != null)
                            return true;
                return false;
            }
        }
    }
}
