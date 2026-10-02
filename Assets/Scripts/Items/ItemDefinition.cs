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
        public EquipmentSlot AllowedSlots;

        public ItemLightDefinition Light;
        public ItemAnimationDefinition Animation;
    }
}