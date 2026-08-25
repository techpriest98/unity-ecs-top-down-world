using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.Player
{
    public static class PlayerCollisionUtility
    {
        private const float BoundsEpsilon =
            0.0001f;

        public static bool IsPositionBlocked(
            float3 position,
            in PlayerCollisionShape shape,
            in ChunkBlockAccessor blockAccessor)
        {
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

                        if (!blockAccessor.TryGetBlock(
                                sourceChunkCoordinate,
                                localX,
                                blockY,
                                localZ,
                                out BlockData block))
                        {
                            return true;
                        }

                        if (BlockUtility.IsSolid(
                                block.BlockId))
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