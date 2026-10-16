using Unity.Entities;

namespace Game.World.Blocks
{
    public struct BlockData : IBufferElementData
    {
        // Three-byte storage contract. When Shape is ReferenceShape these two
        // bytes hold a chunk-local split index, not material/durability.
        // Use SplitCellUtility.Get before interpreting such a cell.
        public BlockId BlockId;
        public byte Durability;
        public BlockShape Shape;

        public BlockData(
            BlockId blockId,
            byte durability,
            BlockShape shape = BlockShape.Full)
        {
            BlockId = blockId;
            Durability = durability;
            Shape = blockId == BlockId.Air ? BlockShape.Full : shape;
        }
    }
}
