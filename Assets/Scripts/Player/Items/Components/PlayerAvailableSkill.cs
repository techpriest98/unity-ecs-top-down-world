using Game.Items;
using Unity.Entities;
using Unity.Mathematics;

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
        public Entity Target;
        public float3 TargetPosition;
    }
}
