using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(2)]
    public struct PlayerEquipmentSlot : IBufferElementData
    {
        public EquipmentSlot Slot;
        public int Item;
        public float Durability;
        public float MaxDurability;
        public float DurabilityElapsed;
    }
}
