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
            WaterProjectionWriter waterWriter,
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
                            waterWriter,
                            world,
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
            WaterProjectionWriter waterWriter,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            BlockData currentBlock =
                GetBlockOrAir(
                    blocks,
                    chunkCoordinate,
                    blockAccessor,
                    world);

            BlockId currentBlockId =
                currentBlock.BlockId;

            ProcessOpaqueCell(
                chunkCoordinate,
                blockAccessor,
                writer,
                currentBlockId,
                world,
                direction,
                projectionX,
                projectionY);

            ProcessWaterCell(
                chunkCoordinate,
                blockAccessor,
                waterWriter,
                currentBlockId,
                world,
                direction,
                projectionX,
                projectionY);
        }

        private static void ProcessOpaqueCell(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            BlockId currentBlockId,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            if (IsOpaque(currentBlockId))
            {
                return;
            }

            TryEmitOpaqueSide(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY);

            TryEmitOpaqueTop(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY);
        }

        private static void ProcessWaterCell(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            WaterProjectionWriter writer,
            BlockId currentBlockId,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            if (!BlockUtility.IsAir(currentBlockId))
            {
                return;
            }

            TryEmitWaterSide(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY);

            TryEmitWaterTop(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY);
        }

       private static void TryEmitOpaqueTop(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 transparentWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            int3 belowWorld =
                transparentWorld +
                new int3(
                    0,
                    -1,
                    0);

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

            if (!IsOpaque(belowBlock.BlockId))
            {
                return;
            }

            byte neighborMask =
                TopNeighborMaskUtility.Build(
                    chunkCoordinate,
                    belowWorld,
                    blockAccessor,
                    direction);

            writer.TryAddTop(
                belowBlock,
                neighborMask,
                projectionX,
                projectionY);
        }

        private static void TryEmitOpaqueSide(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 transparentWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY)
        {
            if (projectionY < 2)
            {
                return;
            }

            int3 blockWorld =
                transparentWorld +
                ViewDirectionUtility
                    .GetAwayFromCameraOffset(
                        direction);

            BlockData block =
                blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    blockWorld.x,
                    blockWorld.y,
                    blockWorld.z);

            if (!IsOpaque(block.BlockId))
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

        private static void TryEmitWaterTop(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            WaterProjectionWriter writer,
            int3 airWorld,
            ViewDirection direction,
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

            if (!IsWater(belowBlock.BlockId))
            {
                return;
            }

            byte opticalDepth =
                MeasureWaterDepth(
                    chunkCoordinate,
                    blockAccessor,
                    belowWorld,
                    new int3(0, -1, 0));

            int3 awayFromCamera =
                ViewDirectionUtility
                    .GetAwayFromCameraOffset(
                        direction);

            int3 behindWorld =
                belowWorld +
                awayFromCamera;

            BlockData behindBlock =
                blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    behindWorld.x,
                    behindWorld.y,
                    behindWorld.z);

            bool topInset =
                IsOpaque(
                    behindBlock.BlockId);

            writer.TryAddTop(
                belowBlock,
                opticalDepth,
                topInset,
                projectionX,
                projectionY);
        }

        private static void TryEmitWaterSide(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            WaterProjectionWriter writer,
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

            if (!IsWater(block.BlockId))
            {
                return;
            }

            int3 awayFromCamera =
                ViewDirectionUtility
                    .GetAwayFromCameraOffset(
                        direction);

            byte opticalDepth =
                MeasureWaterDepth(
                    chunkCoordinate,
                    blockAccessor,
                    blockWorld,
                    awayFromCamera);

            writer.TryAddSideUpper(
                block,
                opticalDepth,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                block,
                opticalDepth,
                projectionX,
                checked((ushort)(projectionY - 1)));
        }

        private static BlockData GetBlockOrAir(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            int3 world)
        {
            return blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                world.x,
                world.y,
                world.z);
        }

        private static byte MeasureWaterDepth(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            int3 startWorld,
            int3 direction)
        {
            int depth = 0;
            int3 currentWorld =
                startWorld;

            while (depth < byte.MaxValue)
            {
                BlockData block =
                    blockAccessor.GetBlockOrAir(
                        chunkCoordinate,
                        currentWorld.x,
                        currentWorld.y,
                        currentWorld.z);

                if (!IsWater(block.BlockId))
                {
                    break;
                }

                depth++;

                currentWorld +=
                    direction;
            }

            return checked((byte)depth);
        }

        private static bool IsOpaque(
            BlockId blockId)
        {
            return BlockUtility.IsSolid(blockId) &&
                   !IsWater(blockId);
        }

        private static bool IsWater(
            BlockId blockId)
        {
            return blockId == BlockId.OceanWater;
        }
    }
}