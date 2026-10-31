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
        public bool WorldSpriteUpright;
        public bool IsStructure;
        public bool PreserveWhenDepleted;
        public ItemDefinition DepletedReplacement;
        [Min(0.001f)] public float MaxDurability = 100f;
        public ItemDurabilityDecayDefinition DurabilityDecay;
        public EquipmentSlot AllowedSlots;

        public ItemLightDefinition Light;
        [Min(0f)] public float WorldLightHeight;
        public bool IgnitionSource;
        public ItemAnimationDefinition Animation;

        public ItemCollisionNode OnCollision;

        public ItemSkillDefinition[] Skills =
            System.Array.Empty<ItemSkillDefinition>();
    }
}
