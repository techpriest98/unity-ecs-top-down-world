using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ChunkProjectionBuilder
    {
        private const float PlayerClipCenterOffset = 1f;
        public const float ProjectionClipRadius = 4f;

        public static void Build(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            WaterProjectionWriter waterWriter,
            ViewDirection direction)
        {
            Build(
                blocks,
                chunkCoordinate,
                blockAccessor,
                writer,
                waterWriter,
                direction,
                projectionClippingEnabled: false,
                playerPosition: float3.zero);
        }

        public static void Build(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            WaterProjectionWriter waterWriter,
            ViewDirection direction,
            bool projectionClippingEnabled,
            float3 playerPosition)
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
                            checked((ushort)projectionY),
                            projectionClippingEnabled,
                            playerPosition);
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
            ushort projectionY,
            bool projectionClippingEnabled,
            float3 playerPosition)
        {
            BlockData currentBlock =
                GetBlockOrAir(
                    blocks,
                    chunkCoordinate,
                    blockAccessor,
                    world);

            BlockId currentBlockId =
                currentBlock.BlockId;

            ushort sourceAirIndex = checked((ushort)ChunkUtility.ToIndex(
                world.x,
                world.y,
                world.z));

            ProcessOpaqueCell(
                chunkCoordinate,
                blockAccessor,
                writer,
                currentBlockId,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex,
                projectionClippingEnabled,
                playerPosition);

            ProcessWaterCell(
                chunkCoordinate,
                blockAccessor,
                waterWriter,
                currentBlockId,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex);
        }

        private static void ProcessOpaqueCell(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            BlockId currentBlockId,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex,
            bool projectionClippingEnabled,
            float3 playerPosition)
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
                projectionY,
                sourceAirIndex,
                projectionClippingEnabled,
                playerPosition);

            TryEmitOpaqueTop(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex,
                projectionClippingEnabled,
                playerPosition);
        }

        private static void ProcessWaterCell(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            WaterProjectionWriter writer,
            BlockId currentBlockId,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex)
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
                projectionY,
                sourceAirIndex);

            TryEmitWaterTop(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex);
        }

       private static void TryEmitOpaqueTop(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 transparentWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex,
            bool projectionClippingEnabled,
            float3 playerPosition)
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

            float3 facePosition =
                GetGlobalPosition(
                    chunkCoordinate,
                    belowWorld,
                    1f);

            if (ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    facePosition,
                    belowWorld.y,
                    direction))
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
                sourceAirIndex,
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
            ushort projectionY,
            ushort sourceAirIndex,
            bool projectionClippingEnabled,
            float3 playerPosition)
        {
            if (projectionY < 2)
                return;

            int3 blockWorld = transparentWorld + ViewDirectionUtility
                .GetAwayFromCameraOffset(direction);

            BlockData block = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                blockWorld.x,
                blockWorld.y,
                blockWorld.z);

            if (!IsOpaque(block.BlockId))
                return;

            float3 facePosition = GetGlobalPosition(
                chunkCoordinate, blockWorld, 0.5f);

            if (ShouldClipOpaqueFace(
                projectionClippingEnabled,
                playerPosition,
                facePosition,
                blockWorld.y,
                direction))
                return;

            int3 rightOffset = direction switch
            {
                ViewDirection.Front => new int3(1, 0, 0),
                ViewDirection.Back => new int3(-1, 0, 0),
                ViewDirection.Right => new int3(0, 0, 1),
                ViewDirection.Left => new int3(0, 0, -1),
                _ => int3.zero
            };

            int3 aboveWorld = blockWorld + new int3(0, 1, 0);
            int3 rightWorld = blockWorld + rightOffset;
            int3 leftWorld = blockWorld - rightOffset;

            BlockData aboveBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate, aboveWorld.x, aboveWorld.y, aboveWorld.z);
            BlockData rightBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate, rightWorld.x, rightWorld.y, rightWorld.z);
            BlockData leftBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate, leftWorld.x, leftWorld.y, leftWorld.z);

            byte neighborMask = (byte)(
                (IsOpaque(aboveBlock.BlockId) ? 1 : 0) |
                (IsOpaque(rightBlock.BlockId) ? 2 : 0) |
                (IsOpaque(leftBlock.BlockId) ? 8 : 0));

            int3 footWorld = transparentWorld + new int3(0, -1, 0);
            BlockData footBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate, footWorld.x, footWorld.y, footWorld.z);

            BlockId footBlockId = IsOpaque(footBlock.BlockId)
                ? footBlock.BlockId
                : BlockId.Air;

            writer.TryAddSideUpper(
                block,
                neighborMask,
                footBlockId,
                sourceAirIndex,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                block,
                neighborMask,
                footBlockId,
                sourceAirIndex,
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
            ushort projectionY,
            ushort sourceAirIndex)
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
                sourceAirIndex,
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
            ushort projectionY,
            ushort sourceAirIndex)
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
                sourceAirIndex,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                block,
                opticalDepth,
                sourceAirIndex,
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

        private static float3 GetGlobalPosition(
            int2 chunkCoordinate,
            int3 localPosition,
            float heightOffset)
        {
            return new float3(
                chunkCoordinate.x *
                    ChunkSettings.SizeX +
                localPosition.x +
                0.5f,

                localPosition.y +
                heightOffset,

                chunkCoordinate.y *
                    ChunkSettings.SizeZ +
                localPosition.z +
                0.5f);
        }

        private static bool ShouldClipOpaqueFace(
            bool projectionClippingEnabled,
            float3 playerPosition,
            float3 facePosition,
            int blockY,
            ViewDirection direction)
        {
            if (!projectionClippingEnabled)
            {
                return false;
            }

            int playerFloorY = (int)math.floor(playerPosition.y + 0.01f);

            if (blockY < playerFloorY)
            {
                return false;
            }

            float3 playerClipCenterPosition = playerPosition + new float3(
                0f,
                PlayerClipCenterOffset,
                0f);

            int2 towardCamera =
                ViewDirectionUtility.Forward(
                    direction);

            int2 playerCell = new int2(
                (int)math.floor(playerPosition.x),
                (int)math.floor(playerPosition.z));

            int2 faceCell = new int2(
                (int)math.floor(facePosition.x),
                (int)math.floor(facePosition.z));

            int depthDistance = math.dot(
                faceCell - playerCell,
                towardCamera);

            if (depthDistance <= 0)
            {
                return false;
            }

            float2 projectedFacePosition = ProjectWorldPosition(facePosition, direction);
            float2 projectedPlayerPosition = ProjectWorldPosition(playerClipCenterPosition, direction);

            return math.distancesq(
                    projectedFacePosition,
                    projectedPlayerPosition) <
                ProjectionClipRadius *
                ProjectionClipRadius;
        }

        private static float2 ProjectWorldPosition(
            float3 worldPosition,
            ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front =>
                    new float2(
                        worldPosition.x,
                        worldPosition.y -
                        worldPosition.z * 0.5f),

                ViewDirection.Back =>
                    new float2(
                        -worldPosition.x,
                        worldPosition.y +
                        worldPosition.z * 0.5f),

                ViewDirection.Right =>
                    new float2(
                        worldPosition.z,
                        worldPosition.y +
                        worldPosition.x * 0.5f),

                ViewDirection.Left =>
                    new float2(
                        -worldPosition.z,
                        worldPosition.y -
                        worldPosition.x * 0.5f),

                _ =>
                    float2.zero
            };
        }

        private static bool IsWater(
            BlockId blockId)
        {
            return blockId == BlockId.OceanWater;
        }
    }
}
