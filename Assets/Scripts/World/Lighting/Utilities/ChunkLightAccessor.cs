using Game.World.Chunks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public readonly struct ChunkLightAccessor
    {
        private readonly NativeParallelHashMap<int2, Entity> chunkEntities;
        private readonly BufferLookup<VoxelLightData> lightLookup;

        public ChunkLightAccessor(
            NativeParallelHashMap<int2, Entity> chunkEntities,
            BufferLookup<VoxelLightData> lightLookup)
        {
            this.chunkEntities = chunkEntities;
            this.lightLookup = lightLookup;
        }

        public bool TryGetLight(int2 chunkCoordinate, int3 position, out VoxelLightData light)
        {
            light = default;

            if (position.y < 0 || position.y >= ChunkSettings.SizeY)
                return false;

            int offsetX = FloorDiv(position.x, ChunkSettings.SizeX);
            int offsetZ = FloorDiv(position.z, ChunkSettings.SizeZ);

            int2 target = chunkCoordinate + new int2(offsetX, offsetZ);

            if (!chunkEntities.TryGetValue(target, out Entity entity) ||
                !lightLookup.HasBuffer(entity))
                return false;

            int x = position.x - offsetX * ChunkSettings.SizeX;
            int z = position.z - offsetZ * ChunkSettings.SizeZ;
            int index = ChunkUtility.ToIndex(x, position.y, z);

            DynamicBuffer<VoxelLightData> lights = lightLookup[entity];

            if ((uint)index >= (uint)lights.Length)
                return false;

            light = lights[index];
                return true;
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;

            if (value < 0 && value % divisor != 0)
                quotient--;
            return
                quotient;
        }
    }
}