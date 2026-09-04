using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public struct ChunkVoxelLightAccessor
    {
        private readonly NativeParallelHashMap<int2, Entity> chunkEntities;
        private BufferLookup<VoxelLightData> lightLookup;

        public ChunkVoxelLightAccessor(
            NativeParallelHashMap<int2, Entity> chunkEntities,
            BufferLookup<VoxelLightData> lightLookup)
        {
            this.chunkEntities = chunkEntities;
            this.lightLookup = lightLookup;
        }

        public bool TryGet(
            int2 sourceChunkCoordinate,
            int localX,
            int localY,
            int localZ,
            out VoxelLightData light)
        {
            light = default;

            if (!TryResolve(
                    sourceChunkCoordinate,
                    localX,
                    localY,
                    localZ,
                    out Entity entity,
                    out int index,
                    out _))
            {
                return false;
            }

            light = lightLookup[entity][index];
            return true;
        }

        public bool TrySet(
            int2 sourceChunkCoordinate,
            int localX,
            int localY,
            int localZ,
            VoxelLightData light,
            out Entity chunkEntity,
            out int2 chunkCoordinate)
        {
            chunkEntity = Entity.Null;
            chunkCoordinate = default;

            if (!TryResolve(
                    sourceChunkCoordinate,
                    localX,
                    localY,
                    localZ,
                    out chunkEntity,
                    out int index,
                    out chunkCoordinate))
            {
                return false;
            }

            DynamicBuffer<VoxelLightData> buffer = lightLookup[chunkEntity];
            buffer[index] = light;
            return true;
        }

        private bool TryResolve(
            int2 sourceChunkCoordinate,
            int localX,
            int localY,
            int localZ,
            out Entity entity,
            out int index,
            out int2 chunkCoordinate)
        {
            entity = Entity.Null;
            index = -1;
            chunkCoordinate = default;

            if (localY < 0 || localY >= ChunkSettings.SizeY)
            {
                return false;
            }

            int chunkOffsetX = FloorDiv(localX, ChunkSettings.SizeX);
            int chunkOffsetZ = FloorDiv(localZ, ChunkSettings.SizeZ);
            int normalizedX = localX - chunkOffsetX * ChunkSettings.SizeX;
            int normalizedZ = localZ - chunkOffsetZ * ChunkSettings.SizeZ;
            chunkCoordinate = sourceChunkCoordinate + new int2(chunkOffsetX, chunkOffsetZ);

            if (!chunkEntities.TryGetValue(chunkCoordinate, out entity) || !lightLookup.HasBuffer(entity))
            {
                return false;
            }

            index = ChunkUtility.ToIndex(normalizedX, localY, normalizedZ);
            return (uint)index < (uint)lightLookup[entity].Length;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;

            if (value % divisor != 0 && value < 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}
