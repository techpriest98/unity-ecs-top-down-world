using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;
using Unity.Collections;
using Game.World.Trees;

namespace Game.Player
{
    public static class PlayerCollisionUtility
    {
        private const float BoundsEpsilon =
            0.0001f;
        private const float GroundProbeDistance = 0.02f;
        private const float StepHeight = 0.5f;
        private const float MaximumHorizontalStep = 0.2f;

        public static bool IsGrounded(float3 position, in PlayerCollisionShape shape,
            in ChunkBlockAccessor accessor) => IsPositionBlocked(position - new float3(0f, GroundProbeDistance, 0f), shape, accessor);

        public static float3 MoveHorizontally(float3 position, float2 displacement,
            in PlayerCollisionShape shape, in ChunkBlockAccessor accessor, bool allowStep, NativeArray<ChunkTree> trunks = default)
        {
            int steps = math.max(1, (int)math.ceil(math.length(displacement) / MaximumHorizontalStep));
            float3 increment = new float3(displacement.x, 0f, displacement.y) / steps;
            for (int i = 0; i < steps; i++)
            {
                float3 candidate = position + increment;
                if (!IsPositionBlocked(candidate, shape, accessor, trunks))
                {
                    position = candidate;
                    continue;
                }
                if (!allowStep || !IsGrounded(position, shape, accessor)) break;

                // Check the entire upward sweep, not only the raised endpoint.
                PlayerCollisionShape upwardSweep = shape;
                upwardSweep.Height += StepHeight;
                if (IsPositionBlocked(position, upwardSweep, accessor, trunks)) break;

                candidate.y += StepHeight;
                if (IsPositionBlocked(candidate, shape, accessor, trunks) || !IsGrounded(candidate, shape, accessor)) break;
                position = candidate;
            }
            return position;
        }

        public static bool IsPositionBlocked(
            float3 position,
            in PlayerCollisionShape shape,
            in ChunkBlockAccessor blockAccessor, NativeArray<ChunkTree> trunks = default)
        {
            if (trunks.IsCreated)
                foreach (var trunk in trunks)
                    if (TreeTrunkCollision.Intersects(position, shape.HalfExtentsXZ, shape.Height, trunk)) return true;
            int2 sourceChunkCoordinate =
                GetChunkCoordinate(
                    position);

            int sourceBlockX =
                sourceChunkCoordinate.x *
                ChunkSettings.SizeX;

            int sourceBlockZ =
                sourceChunkCoordinate.y *
                ChunkSettings.SizeZ;

            int minBlockX =
                (int)math.floor(
                    position.x -
                    shape.HalfExtentsXZ.x +
                    BoundsEpsilon);

            int maxBlockX =
                (int)math.floor(
                    position.x +
                    shape.HalfExtentsXZ.x -
                    BoundsEpsilon);

            int minBlockY =
                (int)math.floor(
                    position.y +
                    BoundsEpsilon);

            int maxBlockY =
                (int)math.floor(
                    position.y +
                    shape.Height -
                    BoundsEpsilon);

            int minBlockZ =
                (int)math.floor(
                    position.z -
                    shape.HalfExtentsXZ.y +
                    BoundsEpsilon);

            int maxBlockZ =
                (int)math.floor(
                    position.z +
                    shape.HalfExtentsXZ.y -
                    BoundsEpsilon);

            for (int blockY = minBlockY;
                 blockY <= maxBlockY;
                 blockY++)
            {
                for (int blockZ = minBlockZ;
                     blockZ <= maxBlockZ;
                     blockZ++)
                {
                    for (int blockX = minBlockX;
                         blockX <= maxBlockX;
                         blockX++)
                    {
                        int localX =
                            blockX -
                            sourceBlockX;

                        int localZ =
                            blockZ -
                            sourceBlockZ;

                        if (!blockAccessor.TryGetRawBlock(
                                sourceChunkCoordinate,
                                localX,
                                blockY,
                                localZ,
                                out BlockData block))
                        {
                            return true;
                        }

                        // Keep the existing material solidity rules. Resolve the
                        // two actual volumes rather than a compatibility material.
                        CellContents contents = SplitCellUtility.IsSplit(block)
                            ? blockAccessor.GetContentsOrAir(sourceChunkCoordinate, localX, blockY, localZ)
                            : CellContents.FromLegacy(block);
                        float feet = position.y + BoundsEpsilon;
                        float head = position.y + shape.Height - BoundsEpsilon;
                        if ((BlockUtility.IsSolid(contents.Lower.Id) && feet < blockY + 0.5f && head > blockY) ||
                            (BlockUtility.IsSolid(contents.Upper.Id) && feet < blockY + 1f && head > blockY + 0.5f))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static int2 GetChunkCoordinate(
            float3 position)
        {
            return new int2(
                (int)math.floor(
                    position.x /
                    ChunkSettings.SizeX),

                (int)math.floor(
                    position.z /
                    ChunkSettings.SizeZ));
        }
    }
}
