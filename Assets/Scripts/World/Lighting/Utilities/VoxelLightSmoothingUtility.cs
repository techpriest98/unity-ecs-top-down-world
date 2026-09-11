using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class VoxelLightSmoothingUtility
    {
        public static uint SampleCorner(
            int2 chunkCoordinate,
            int3 airPosition,
            int3 offsetA,
            int3 offsetB,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights)
        {
            if (!TrySample(chunkCoordinate, airPosition, blocks, lights, out float3 center))
                return 0;

            float3 sum = center;
            int count = 1;

            bool hasA = TrySample(
                chunkCoordinate, airPosition + offsetA,
                blocks, lights, out float3 a);

            bool hasB = TrySample(
                chunkCoordinate, airPosition + offsetB,
                blocks, lights, out float3 b);

            if (hasA) { sum += a; count++; }
            if (hasB) { sum += b; count++; }

            if (hasA && hasB && TrySample(
                chunkCoordinate, airPosition + offsetA + offsetB,
                blocks, lights, out float3 diagonal))
            {
                sum += diagonal;
                count++;
            }

            return Pack(sum / count);
        }

        private static bool TrySample(
            int2 chunkCoordinate,
            int3 position,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights,
            out float3 rgb)
        {
            rgb = float3.zero;

            if (!blocks.TryGetBlock(
                chunkCoordinate, position.x, position.y, position.z,
                out BlockData block))
                return false;

            if (BlockUtility.IsSolid(block.BlockId) &&
                block.BlockId != BlockId.OceanWater)
                return false;

            if (!lights.TryGetLight(chunkCoordinate, position, out VoxelLightData light))
                return false;

            rgb = new float3(light.R, light.G, light.B);
            return true;
        }

        private static uint Pack(float3 rgb)
        {
            uint3 value = (uint3)math.clamp(math.round(rgb), 0f, 255f);
            return value.x | (value.y << 8) | (value.z << 16);
        }
    }
}