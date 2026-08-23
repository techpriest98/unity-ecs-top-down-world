using Unity.Entities;

namespace Game.World.Blocks
{
    public struct BlockDatabaseComponent : IComponentData
    {
        public BlobAssetReference<BlockDatabaseBlob> Value;

        public BlockData CreateBlock(BlockId blockId)
        {
            if (!Value.IsCreated)
                return new BlockData(blockId, 0);

            int index = (int)blockId;
            ref BlockDatabaseBlob database = ref Value.Value;

            byte maxDurability =
                index >= 0 && index < database.MaxDurability.Length
                    ? database.MaxDurability[index]
                    : (byte)0;

            return new BlockData(blockId, maxDurability);
        }
    }
}