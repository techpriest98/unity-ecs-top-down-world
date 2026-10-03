using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class VoxelLightSmoothingUtility
    {
        private const float SkyToByte = 255f / 15f;

        public static uint SampleCorner(
            int2 chunkCoordinate,
            int3 airPosition,
            int3 offsetA,
            int3 offsetB,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights)
        {
            if (!TrySample(
                    chunkCoordinate,
                    airPosition,
                    blocks,
                    lights,
                    out float4 center))
            {
                return 0u;
            }

            float4 sum = center;
            int count = 1;

            bool hasA = TrySample(
                chunkCoordinate,
                airPosition + offsetA,
                blocks,
                lights,
                out float4 a);

            bool hasB = TrySample(
                chunkCoordinate,
                airPosition + offsetB,
                blocks,
                lights,
                out float4 b);

            if (hasA)
            {
                sum += a;
                count++;
            }

            if (hasB)
            {
                sum += b;
                count++;
            }

            if (hasA && hasB &&
                TrySample(
                    chunkCoordinate,
                    airPosition + offsetA + offsetB,
                    blocks,
                    lights,
                    out float4 diagonal))
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
            out float4 value)
        {
            value = float4.zero;

            if (!blocks.TryGetBlock(
                    chunkCoordinate,
                    position.x,
                    position.y,
                    position.z,
                    out BlockData block))
            {
                return false;
            }

            if (BlockUtility.IsSolid(block.BlockId) &&
                block.BlockId != BlockId.OceanWater)
            {
                return false;
            }

            if (!lights.TryGetLight(
                    chunkCoordinate,
                    position,
                    out VoxelLightData light))
            {
                return false;
            }

            value = new float4(
                light.R,
                light.G,
                light.B,
                math.min((int)light.Sky, 15) * SkyToByte);

            return true;
        }

        private static uint Pack(float4 light)
        {
            uint4 value = (uint4)math.clamp(
                math.round(light),
                0f,
                255f);
                
            return value.x |
                   (value.y << 8) |
                   (value.z << 16) |
                   (value.w << 24);
        }
    }
}