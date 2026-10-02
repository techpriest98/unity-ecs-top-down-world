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
    }

    public struct ItemData
    {
        public ItemID Id;
        public EquipmentSlot AllowedSlots;

        public bool HasLight;
        public DynamicLightSource Light;

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