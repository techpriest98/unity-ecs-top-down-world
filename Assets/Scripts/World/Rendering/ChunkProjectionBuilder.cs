using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ChunkProjectionBuilder
    {
        public static void Build(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            ViewDirection direction)
        {
            int2 chunkSizeXZ = new int2(
                ChunkSettings.SizeX,
                ChunkSettings.SizeZ);

            ViewBounds bounds =
                ViewCoordinateUtility.GetViewBounds(
                    chunkSizeXZ,
                    ChunkSettings.SizeY,
                    direction);

            for (int u = 0; u < bounds.Width; u++)
            {
                for (int h = bounds.Height - 1;
                     h >= 0;
                     h--)
                {
                    int heightFromTop =
                        bounds.Height - 1 - h;

                    for (int v = bounds.Depth - 1;
                         v >= 0;
                         v--)
                    {
                        int projectionY =
                            heightFromTop * 2 + v;

                        ViewCoordinate view =
                            new ViewCoordinate(
                                u,
                                v,
                                h);

                        int3 world =
                            ViewCoordinateUtility.ViewToWorld(
                                view,
                                chunkSizeXZ,
                                direction);

                        ProcessCell(
                            blocks,
                            chunkCoordinate,
                            blockAccessor,
                            writer,
                            view,
                            world,
                            chunkSizeXZ,
                            direction,
                            checked((ushort)u),
                            checked((ushort)projectionY));
                    }
                }
            }
        }

        private static void ProcessCell(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            ViewCoordinate view,
            int3 world,
            int2 chunkSizeXZ,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            BlockId currentBlockId =
                GetBlockIdOrAir(
                    blocks,
                    chunkCoordinate,
                    blockAccessor,
                    world);

            if (BlockUtility.IsSolid(currentBlockId))
            {
                return;
            }

            TryEmitSide(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY);

            TryEmitTop(
                blocks,
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                projectionX,
                projectionY);
        }

        private static void TryEmitTop(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 airWorld,
            ushort projectionX,
            ushort projectionY)
        {
            int3 belowWorld =
                airWorld +
                new int3(0, -1, 0);

            if (!ChunkUtility.IsInside(
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z))
            {
                return;
            }

            BlockData belowBlock =
                blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z);

            if (!BlockUtility.IsSolid(
                    belowBlock.BlockId))
            {
                return;
            }

            writer.TryAddTop(
                belowBlock,
                projectionX,
                projectionY);
        }

        private static void TryEmitSide(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 airWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            if (projectionY < 2)
            {
                return;
            }

            int3 blockWorld =
                airWorld +
                ViewDirectionUtility
                    .GetAwayFromCameraOffset(
                        direction);

            BlockData block =
                blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    blockWorld.x,
                    blockWorld.y,
                    blockWorld.z);

            if (!BlockUtility.IsSolid(
                    block.BlockId))
            {
                return;
            }

            writer.TryAddSideUpper(
                block,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                block,
                projectionX,
                checked((ushort)(projectionY - 1)));
        }

        private static BlockId GetBlockIdOrAir(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            int3 world)
        {
            BlockData block =
                blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    world.x,
                    world.y,
                    world.z);

            return block.BlockId;
        }
    }
}