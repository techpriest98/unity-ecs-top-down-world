using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    public struct PlayerActiveHand : IComponentData
    {
        public EquipmentSlot Slot;
    }

    [InternalBufferCapacity(2)]
    public struct PlayerEquipmentSlot : IBufferElementData
    {
        public EquipmentSlot Slot;
        public ItemInstance Instance;
    }
}
