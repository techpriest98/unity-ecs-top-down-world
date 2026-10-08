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
        [TextArea(3, 8)]
        public string Description;
        public Sprite Icon;
        [Min(0.001f)] public float MaxDurability = 100f;
        [UnityEngine.Serialization.FormerlySerializedAs("DurabilityLossPerSecond")]
        [Min(0f)] public float DurabilityLossAmount;
        [Min(0.001f)] public float DurabilityLossInterval = 1f;
        public EquipmentSlot AllowedSlots;

        public ItemLightDefinition Light;
        public ItemAnimationDefinition Animation;

        public ItemSkillDefinition[] Skills =
            System.Array.Empty<ItemSkillDefinition>();
    }
}
