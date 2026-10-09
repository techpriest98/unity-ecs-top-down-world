using Game.Items.Skills;
using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "Item",
        menuName = "Game/Items/Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public int Id;
        public string DisplayName;
        [TextArea(3, 8)]
        public string Description;
        public Sprite Icon;
        public Sprite WorldSprite;
        [Min(0.001f)] public float MaxDurability = 100f;
        public ItemDurabilityDecayDefinition DurabilityDecay;
        public EquipmentSlot AllowedSlots;

        public ItemLightDefinition Light;
        public ItemAnimationDefinition Animation;

        public ItemSkillDefinition[] Skills =
            System.Array.Empty<ItemSkillDefinition>();
    }
}
