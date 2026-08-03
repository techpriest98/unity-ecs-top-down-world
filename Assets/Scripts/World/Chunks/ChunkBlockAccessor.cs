using Game.World.Blocks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    public readonly struct ChunkBlockAccessor
    {
        private readonly NativeParallelHashMap<int2, Entity>
            chunkEntities;

        private readonly BufferLookup<BlockData>
            blockLookup;

        public ChunkBlockAccessor(
            NativeParallelHashMap<int2, Entity> chunkEntities,
            BufferLookup<BlockData> blockLookup)
        {
            this.chunkEntities =
                chunkEntities;

            this.blockLookup =
                blockLookup;
        }

        public bool TryGetBlock(
            int2 sourceChunkCoordinate,
            int localX,
            int localY,
            int localZ,
            out BlockData block)
        {
            block =
                CreateAir();

            // Чанки по вертикалі поки не існують.
            if (localY < 0 ||
                localY >= ChunkSettings.SizeY)
            {
                return false;
            }

            int chunkOffsetX =
                FloorDiv(
                    localX,
                    ChunkSettings.SizeX);

            int chunkOffsetZ =
                FloorDiv(
                    localZ,
                    ChunkSettings.SizeZ);

            int normalizedX =
                localX -
                chunkOffsetX *
                ChunkSettings.SizeX;

            int normalizedZ =
                localZ -
                chunkOffsetZ *
                ChunkSettings.SizeZ;

            int2 targetChunkCoordinate =
                sourceChunkCoordinate +
                new int2(
                    chunkOffsetX,
                    chunkOffsetZ);

            if (!chunkEntities.TryGetValue(
                    targetChunkCoordinate,
                    out Entity targetChunkEntity))
            {
                return false;
            }

            if (!blockLookup.HasBuffer(
                    targetChunkEntity))
            {
                return false;
            }

            DynamicBuffer<BlockData> blocks =
                blockLookup[
                    targetChunkEntity];

            int blockIndex =
                ChunkUtility.ToIndex(
                    normalizedX,
                    localY,
                    normalizedZ);

            if ((uint)blockIndex >=
                (uint)blocks.Length)
            {
                return false;
            }

            block =
                blocks[blockIndex];

            return true;
        }

        public BlockData GetBlockOrAir(
            int2 sourceChunkCoordinate,
            int localX,
            int localY,
            int localZ)
        {
            return TryGetBlock(
                sourceChunkCoordinate,
                localX,
                localY,
                localZ,
                out BlockData block)
                    ? block
                    : CreateAir();
        }

        private static BlockData CreateAir()
        {
            return new BlockData(
                BlockId.Air,
                0);
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient =
                value / divisor;

            int remainder =
                value % divisor;

            if (remainder != 0 &&
                value < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}