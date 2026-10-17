using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class VoxelLightSmoothingUtility
    {
        private const float SkyToByte = 255f / 15f;

        public static uint SampleDirection(int2 coordinate, int3 position, ChunkBlockAccessor blocks, ChunkLightAccessor lights)
        {
            if (!TrySample(coordinate, position, blocks, lights, out float4 center)) return 0;
            float brightness = math.dot(center.xyz, new float3(.2126f, .7152f, .0722f));
            if (brightness < .5f) return 0;
            float3 gradient = float3.zero;
            float incoming = 0f;
            for (int axis = 0; axis < 6; axis++)
            {
                int3 offset = axis == 0 ? new int3(1,0,0) : axis == 1 ? new int3(-1,0,0) :
                    axis == 2 ? new int3(0,1,0) : axis == 3 ? new int3(0,-1,0) :
                    axis == 4 ? new int3(0,0,1) : new int3(0,0,-1);
                if (!TrySample(coordinate, position + offset, blocks, lights, out float4 neighbour)) continue;
                float weight = math.max(0f, math.dot(neighbour.xyz, new float3(.2126f,.7152f,.0722f)) - brightness);
                gradient += (float3)offset * weight;
                incoming += weight;
            }
            return PackDirection(gradient, incoming);
        }

        public static uint PackDirection(float3 gradient, float incoming)
        {
            float length = math.length(gradient);
            if (length < .001f || incoming < .001f) return 0;
            uint3 direction = (uint3)math.round(math.saturate(gradient / length * .5f + .5f) * 255f);
            uint confidence = (uint)math.round(math.saturate(length / incoming) * 255f);
            return direction.x | (direction.y << 8) | (direction.z << 16) | (confidence << 24);
        }

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

            if (BlockLightGeometry.IsFull(block))
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
