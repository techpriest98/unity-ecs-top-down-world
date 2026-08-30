using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Blocks
{
    [InternalBufferCapacity(0)]
    public struct BlockModificationRequest : IBufferElementData
    {
        public int3 WorldPosition;
        public BlockData Value;

        public BlockModificationRequest(
            int3 worldPosition,
            BlockData value)
        {
            WorldPosition = worldPosition;
            Value = value;
        }
    }
}