using Game.World.Blocks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    public static class ChunkUtility
    {
        public static bool IsInside(int x, int y, int z)
        {
            return
                x >= 0 && x < ChunkSettings.SizeX &&
                y >= 0 && y < ChunkSettings.SizeY &&
                z >= 0 && z < ChunkSettings.SizeZ;
        }

        public static int ToIndex(int x, int y, int z)
        {
            return x + ChunkSettings.SizeX * (z + ChunkSettings.SizeZ * y);
        }

        public static int ToIndex(int3 position)
        {
            return ToIndex(position.x, position.y, position.z);
        }

        public static int3 ToLocalPosition(int index)
        {
            int y = index / (ChunkSettings.SizeX * ChunkSettings.SizeZ);
            int remainder = index - y * ChunkSettings.SizeX * ChunkSettings.SizeZ;

            int z = remainder / ChunkSettings.SizeX;
            int x = remainder - z * ChunkSettings.SizeX;

            return new int3(x, y, z);
        }

        public static BlockData GetBlock(
            DynamicBuffer<BlockData> blocks,
            int x,
            int y,
            int z)
        {
            return blocks[ToIndex(x, y, z)];
        }

        public static void SetBlock(
            DynamicBuffer<BlockData> blocks,
            int x,
            int y,
            int z,
            BlockData block)
        {
            blocks[ToIndex(x, y, z)] = block;
        }
    }
}