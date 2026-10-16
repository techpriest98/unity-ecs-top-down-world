using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Blocks
{
    [InternalBufferCapacity(0)]
    public struct BlockModificationRequest : IBufferElementData
    {
        public int3 WorldPosition;
        // Carries material, durability and shape together; never infer shape from BlockId.
        public BlockData Value;
        public bool HasContents;
        public CellContents Contents;

        public BlockModificationRequest(
            int3 worldPosition,
            BlockData value)
        {
            WorldPosition = worldPosition;
            Value = value;
            HasContents = false;
            Contents = default;
        }

        public BlockModificationRequest(int3 worldPosition, CellContents contents)
        {
            WorldPosition = worldPosition;
            Value = default;
            HasContents = true;
            Contents = new CellContents(contents.Lower, contents.Upper);
        }
    }
}
