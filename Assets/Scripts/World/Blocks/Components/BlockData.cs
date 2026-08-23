using Unity.Entities;

namespace Game.World.Blocks
{
    public struct BlockData : IBufferElementData
    {
        public BlockId BlockId;
        public byte Durability;

        public BlockData(
            BlockId blockId,
            byte durability)
        {
            BlockId = blockId;
            Durability = durability;
        }
    }
}