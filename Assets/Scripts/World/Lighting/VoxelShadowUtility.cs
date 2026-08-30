using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class VoxelShadowUtility
    {
        private const float DirectionEpsilon = 0.0001f;
        private const float BoundaryEpsilon = 0.00001f;

        public static bool IsOccluded(
            int2 chunkCoordinate,
            int3 sourceAirPosition,
            ChunkBlockAccessor blockAccessor,
            float3 directionToLight,
            float maximumDistance)
        {
            if (math.lengthsq(directionToLight) < DirectionEpsilon)
            {
                return false;
            }

            float3 direction = math.normalize(directionToLight);
            float3 origin = new float3(
                sourceAirPosition.x + 0.5f,
                sourceAirPosition.y + 0.5f,
                sourceAirPosition.z + 0.5f);
            int3 voxel = sourceAirPosition;

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
                float nextDistance = math.cmin(distanceToBoundary);

                if (nextDistance > maximumDistance)
                {
                    return false;
                }

                bool3 crossedBoundary =
                    distanceToBoundary <= nextDistance + BoundaryEpsilon;

                if (crossedBoundary.x)
                {
                    voxel.x += step.x;
                    distanceToBoundary.x += distanceDelta.x;
                }

                if (crossedBoundary.y)
                {
                    voxel.y += step.y;
                    distanceToBoundary.y += distanceDelta.y;
                }

                if (crossedBoundary.z)
                {
                    voxel.z += step.z;
                    distanceToBoundary.z += distanceDelta.z;
                }

                BlockData block = blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    voxel.x,
                    voxel.y,
                    voxel.z);

                if (IsOpaque(block.BlockId))
                {
                    return true;
                }
            }
        }

        private static int GetStep(float direction)
        {
            if (direction > DirectionEpsilon)
            {
                return 1;
            }

            if (direction < -DirectionEpsilon)
            {
                return -1;
            }

            return 0;
        }

        private static float GetDistanceDelta(float direction)
        {
            return math.abs(direction) > DirectionEpsilon
                ? math.abs(1f / direction)
                : float.MaxValue;
        }

        private static float GetDistanceToBoundary(
            float origin,
            int voxel,
            float direction)
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
            return BlockUtility.IsSolid(blockId) &&
                   blockId != BlockId.OceanWater;
        }
    }
}