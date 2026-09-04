using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class DynamicLightVisibilityUtility
    {
        private const float DirectionEpsilon = 0.0001f;

        public static bool IsVisible(
            int2 sourceChunk,
            int3 sourcePosition,
            int3 targetPosition,
            ChunkBlockAccessor blockAccessor)
        {
            float3 origin = new float3(sourcePosition) + 0.5f;
            float3 target = new float3(targetPosition) + 0.5f;
            float3 ray = target - origin;
            float maximumDistance = math.length(ray);

            if (maximumDistance < DirectionEpsilon)
            {
                return true;
            }

            float3 direction = ray / maximumDistance;
            int3 voxel = sourcePosition;
            int3 step = new int3(
                GetStep(direction.x),
                GetStep(direction.y),
                GetStep(direction.z));

            float3 distanceDelta = new float3(
                GetDistanceDelta(direction.x),
                GetDistanceDelta(direction.y),
                GetDistanceDelta(direction.z));

            float3 distanceToBoundary = new float3(
                GetDistanceToBoundary(origin.x, voxel.x, direction.x),
                GetDistanceToBoundary(origin.y, voxel.y, direction.y),
                GetDistanceToBoundary(origin.z, voxel.z, direction.z));

            while (true)
            {
                if (distanceToBoundary.x <= distanceToBoundary.y &&
                    distanceToBoundary.x <= distanceToBoundary.z)
                {
                    voxel.x += step.x;
                    distanceToBoundary.x += distanceDelta.x;
                }
                else if (distanceToBoundary.y <= distanceToBoundary.z)
                {
                    voxel.y += step.y;
                    distanceToBoundary.y += distanceDelta.y;
                }
                else
                {
                    voxel.z += step.z;
                    distanceToBoundary.z += distanceDelta.z;
                }

                if (math.all(voxel == targetPosition))
                {
                    return true;
                }

                if (!blockAccessor.TryGetBlock(
                        sourceChunk,
                        voxel.x,
                        voxel.y,
                        voxel.z,
                        out BlockData block) ||
                    IsOpaque(block.BlockId))
                {
                    return false;
                }
            }
        }

        private static int GetStep(float direction)
        {
            return direction > DirectionEpsilon
                ? 1
                : direction < -DirectionEpsilon
                    ? -1
                    : 0;
        }

        private static float GetDistanceDelta(float direction)
        {
            return math.abs(direction) > DirectionEpsilon
                ? math.abs(1f / direction)
                : float.MaxValue;
        }

        private static float GetDistanceToBoundary(float origin, int voxel, float direction)
        {
            if (direction > DirectionEpsilon)
            {
                return (voxel + 1f - origin) / direction;
            }

            if (direction < -DirectionEpsilon)
            {
                return (origin - voxel) / -direction;
            }

            return float.MaxValue;
        }

        private static bool IsOpaque(BlockId blockId)
        {
            return BlockUtility.IsSolid(blockId) && blockId != BlockId.OceanWater;
        }
    }
}
