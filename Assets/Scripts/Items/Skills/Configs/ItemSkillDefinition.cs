using UnityEngine;

namespace Game.Items.Skills
{
    [CreateAssetMenu(
        fileName = "ItemSkill",
        menuName = "Game/Items/Skills/Definition")]
    public sealed class ItemSkillDefinition : ScriptableObject
    {
        public ItemSkillID Id;
        public string DisplayName;
        public string Description;
        public Sprite Icon;
        public ItemSkillAction Action;
    }
}