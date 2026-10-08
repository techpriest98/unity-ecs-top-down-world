using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(2)]
    public struct PlayerEquipmentSlot : IBufferElementData
    {
        public EquipmentSlot Slot;
        public ItemInstance Instance;
    }
}
