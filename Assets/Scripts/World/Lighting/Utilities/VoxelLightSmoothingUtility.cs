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

        private static float3 DirectionVector(uint packed)
        {
            float confidence = (packed >> 24) / 255f;
            float3 v = new float3(packed & 255u, (packed >> 8) & 255u, (packed >> 16) & 255u) / 127.5f - 1f;
            return math.normalizesafe(v) * confidence;
        }

        public static byte PackCornerDirection(float3 vector)
        {
            float confidence = math.saturate(math.length(vector));
            uint c = (uint)math.round(confidence * 3f);
            if (c == 0) return 0;
            float3 n = math.normalizesafe(vector);
            n /= math.abs(n.x) + math.abs(n.y) + math.abs(n.z);
            float2 uv = n.xy;
            if (n.z < 0f) uv = (1f - math.abs(uv.yx)) * new float2(uv.x >= 0 ? 1 : -1, uv.y >= 0 ? 1 : -1);
            uint2 encoded = (uint2)math.round(math.saturate(uv * .5f + .5f) * 7f);
            return (byte)(encoded.x | (encoded.y << 3) | (c << 6));
        }

        public static float3 UnpackCornerDirection(byte packed)
        {
            float2 uv = new float2(packed & 7, (packed >> 3) & 7) / 7f * 2f - 1f;
            float3 n = new float3(uv, 1f - math.abs(uv.x) - math.abs(uv.y));
            if (n.z < 0f) n.xy = (1f - math.abs(n.yx)) * new float2(n.x >= 0 ? 1 : -1, n.y >= 0 ? 1 : -1);
            return math.normalizesafe(n) * ((packed >> 6) / 3f);
        }

        public static byte AverageDirection(byte a, byte b) =>
            PackCornerDirection((UnpackCornerDirection(a) + UnpackCornerDirection(b)) * .5f);

        public static byte SampleCornerDirection(int2 coordinate, int3 position, int3 a, int3 b,
            ChunkBlockAccessor blocks, ChunkLightAccessor lights)
        {
            if (!TrySample(coordinate, position, blocks, lights, out _)) return 0;
            float3 sum = DirectionVector(SampleDirection(coordinate, position, blocks, lights));
            int count = 1;
            bool hasA = TrySample(coordinate, position + a, blocks, lights, out _);
            bool hasB = TrySample(coordinate, position + b, blocks, lights, out _);
            if (hasA) { sum += DirectionVector(SampleDirection(coordinate, position + a, blocks, lights)); count++; }
            if (hasB) { sum += DirectionVector(SampleDirection(coordinate, position + b, blocks, lights)); count++; }
            if (hasA && hasB && TrySample(coordinate, position + a + b, blocks, lights, out _))
            { sum += DirectionVector(SampleDirection(coordinate, position + a + b, blocks, lights)); count++; }
            return PackCornerDirection(sum / count);
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
