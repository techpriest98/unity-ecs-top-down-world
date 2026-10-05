using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(10)]
    public struct PlayerAvailableSkill : IBufferElementData
    {
        public EquipmentSlot SourceSlot;
        public ItemID SourceItem;
        public int SkillIndex;
    }
}