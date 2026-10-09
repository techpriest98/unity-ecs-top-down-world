using Game.Player;
using Game.World.Lighting;
using Unity.Entities;

namespace Game.Items
{
    public struct ItemLibrary : IComponentData
    {
        public BlobAssetReference<ItemLibraryBlob> Value;
    }

    public struct ItemLibraryBlob
    {
        public BlobArray<ItemData> Items;
        public int DebugLeftItemId;
        public int DebugRightItemId;
    }

    public struct ItemData
    {
        public int Id;
        public EquipmentSlot AllowedSlots;
        public float MaxDurability;
        public float DurabilityLossAmount;
        public float DurabilityLossInterval;

        public bool HasLight;
        public bool IgnitionSource;
        public DynamicLightSource Light;
        public bool LightRadiusFromDurability;
        public float LightRadiusAtZeroDurability;

        public bool HasAnimation;
        public ItemHandAnimationData LeftHand;
        public ItemHandAnimationData RightHand;
    }

    public struct ItemHandAnimationData
    {
        public AnimationID ArmIdle;
        public AnimationID ArmWalk;

        public bool HasVisual;
        public CharacterPart VisualPart;

        public AnimationID ItemIdle;
        public AnimationID ItemWalk;
    }
}
