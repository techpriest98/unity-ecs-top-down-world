using Game.Items;
using Unity.Entities;

namespace Game.Player
{
    [InternalBufferCapacity(5)]
    public struct PlayerAvailableSkill : IBufferElementData
    {
        public const int SkillsPerHand = 5;
        public const int SlotCount = SkillsPerHand;

        public EquipmentSlot SourceSlot;
        public int SourceItem;
        public int SkillIndex;
    }
}
