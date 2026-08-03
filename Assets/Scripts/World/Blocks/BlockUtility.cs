using Game.World.Blocks;

namespace Game.World.Blocks
{
    public static class BlockUtility
    {
        public static bool IsAir(BlockId blockId)
        {
            return blockId == BlockId.Air;
        }

        public static bool IsSolid(BlockId blockId)
        {
            return !IsAir(blockId);
        }
    }
}