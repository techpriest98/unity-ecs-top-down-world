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
            BlockData sourceBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate, sourceAirPosition.x, sourceAirPosition.y, sourceAirPosition.z);
            origin.y += BlockLightGeometry.SampleOffset(sourceBlock);
            if ((sourceBlock.Shape == BlockShape.LowerHalf || sourceBlock.Shape == BlockShape.UpperHalf) &&
                BlockLightGeometry.Intersects(sourceBlock, sourceAirPosition, origin, direction, maximumDistance))
                return true;
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

                if (BlockLightGeometry.Intersects(block, voxel, origin, direction, maximumDistance))
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

    // Light stored in a lower-half cell represents its empty upper half.
    public static class BlockLightGeometry
    {
        public static bool IsOpaque(BlockData block) =>
            BlockUtility.IsSolid(block.BlockId) && block.BlockId != BlockId.OceanWater && block.BlockId != BlockId.RiverWater;

        public static bool IsHalf(BlockData block) =>
            IsOpaque(block) && block.Shape == BlockShape.LowerHalf;
        public static bool IsUpperHalf(BlockData block) =>
            IsOpaque(block) && block.Shape == BlockShape.UpperHalf;

        public static bool IsFull(BlockData block) => IsOpaque(block) && !IsHalf(block) && !IsUpperHalf(block);
        public static float SampleOffset(BlockData block) => IsHalf(block) ? .25f :
            IsOpaque(block) && block.Shape == BlockShape.UpperHalf ? -.25f : 0f;

        public static bool Intersects(BlockData block, int3 cell, float3 origin,
            float3 direction, float maximumDistance)
        {
            if (!IsOpaque(block)) return false;
            if (!IsHalf(block) && block.Shape != BlockShape.UpperHalf) return true;
            float3 minimum = cell;
            float3 maximum = minimum + new float3(1f, 1f, 1f);
            if (block.Shape == BlockShape.UpperHalf) minimum.y += .5f;
            else maximum.y -= .5f;
            float near = 0f;
            float far = maximumDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                if (math.abs(direction[axis]) < 0.0001f)
                {
                    if (origin[axis] < minimum[axis] || origin[axis] > maximum[axis]) return false;
                    continue;
                }
                float a = (minimum[axis] - origin[axis]) / direction[axis];
                float b = (maximum[axis] - origin[axis]) / direction[axis];
                near = math.max(near, math.min(a, b));
                far = math.min(far, math.max(a, b));
                if (near > far) return false;
            }
            return far > 0.0001f && near < maximumDistance;
        }
    }
}
