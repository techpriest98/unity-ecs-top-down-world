using Game.Items.Skills;
using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "Item",
        menuName = "Game/Items/Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public ItemID Id;
        public string DisplayName;
        public Sprite Icon;
        public EquipmentSlot AllowedSlots;

        public ItemLightDefinition Light;
        public ItemAnimationDefinition Animation;

        public ItemSkillDefinition[] Skills =
            System.Array.Empty<ItemSkillDefinition>();
    }
}