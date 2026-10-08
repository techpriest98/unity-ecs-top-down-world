using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(2)]
    public struct PlayerEquipmentChangeRequest : IBufferElementData
    {
        public EquipmentSlot Slot;
        public int ExpectedItem;
        public int ReplacementItem;
    }
}
