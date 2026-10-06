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
                for (int h = bounds.Height - 1; h >= 0; h--)
                {
                    int heightFromTop = bounds.Height - 1 - h;

                    for (int v = bounds.Depth - 1; v >= 0; v--)
                    {
                        int projectionY = heightFromTop * 2 + v;

                        ViewCoordinate view = new ViewCoordinate(
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
            BlockData currentBlock = GetBlockOrAir(
                blocks,
                chunkCoordinate,
                blockAccessor,
                world);

            BlockId currentBlockId = currentBlock.BlockId;

            ushort sourceAirIndex = checked(
                (ushort)ChunkUtility.ToIndex(
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
                TryEmitCutTop(
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
                return;

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
                transparentWorld + new int3(0, -1, 0);

            if (!ChunkUtility.IsInside(
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z))
            {
                return;
            }

            BlockData belowBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                belowWorld.x,
                belowWorld.y,
                belowWorld.z);

            if (!IsOpaque(belowBlock.BlockId))
                return;

            float3 facePosition = GetGlobalPosition(
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

            byte neighborMask = TopNeighborMaskUtility.Build(
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

            int3 blockWorld =
                transparentWorld +
                ViewDirectionUtility.GetAwayFromCameraOffset(direction);

            BlockData block = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                blockWorld.x,
                blockWorld.y,
                blockWorld.z);

            if (!IsOpaque(block.BlockId))
                return;

            float3 facePosition = GetGlobalPosition(
                chunkCoordinate,
                blockWorld,
                0.5f);

            if (ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    facePosition,
                    blockWorld.y,
                    direction))
            {
                return;
            }

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
                chunkCoordinate,
                aboveWorld.x,
                aboveWorld.y,
                aboveWorld.z);

            BlockData rightBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                rightWorld.x,
                rightWorld.y,
                rightWorld.z);

            BlockData leftBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                leftWorld.x,
                leftWorld.y,
                leftWorld.z);

            byte neighborMask = (byte)(
                (IsOpaque(aboveBlock.BlockId) ? 1 : 0) |
                (IsOpaque(rightBlock.BlockId) ? 2 : 0) |
                (IsOpaque(leftBlock.BlockId) ? 8 : 0));

            int3 footWorld =
                transparentWorld + new int3(0, -1, 0);

            BlockData footBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                footWorld.x,
                footWorld.y,
                footWorld.z);

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
            int3 belowWorld = airWorld + new int3(0, -1, 0);

            if (!ChunkUtility.IsInside(
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z))
            {
                return;
            }

            BlockData belowBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                belowWorld.x,
                belowWorld.y,
                belowWorld.z);

            if (!IsWater(belowBlock.BlockId))
                return;

            int3 awayFromCamera =
                ViewDirectionUtility.GetAwayFromCameraOffset(direction);

            // Центр верхньої грані водяного блока.
            float3 rayOrigin = new float3(
                belowWorld.x + 0.5f,
                belowWorld.y + 1f,
                belowWorld.z + 0.5f);

            byte opticalDepth = MeasureWaterDepth(
                chunkCoordinate,
                blockAccessor,
                rayOrigin,
                awayFromCamera);

            int3 behindWorld = belowWorld + awayFromCamera;

            BlockData behindBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                behindWorld.x,
                behindWorld.y,
                behindWorld.z);

            bool topInset = IsOpaque(behindBlock.BlockId);

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
                return;

            int3 awayFromCamera =
                ViewDirectionUtility.GetAwayFromCameraOffset(direction);

            int3 blockWorld = airWorld + awayFromCamera;

            BlockData block = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                blockWorld.x,
                blockWorld.y,
                blockWorld.z);

            if (!IsWater(block.BlockId))
                return;

            // Центр передньої грані, повернутої до камери.
            float3 faceCenter =
                (float3)blockWorld +
                new float3(0.5f, 0.5f, 0.5f) -
                (float3)awayFromCamera * 0.5f;

            byte upperDepth = MeasureWaterDepth(
                chunkCoordinate,
                blockAccessor,
                faceCenter + new float3(0f, 0.25f, 0f),
                awayFromCamera);

            byte lowerDepth = MeasureWaterDepth(
                chunkCoordinate,
                blockAccessor,
                faceCenter - new float3(0f, 0.25f, 0f),
                awayFromCamera);

            writer.TryAddSideUpper(
                block,
                upperDepth,
                sourceAirIndex,
                projectionX,
                checked((ushort)(projectionY - 2)));

            writer.TryAddSideLower(
                block,
                lowerDepth,
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
            float3 rayOrigin,
            int3 awayFromCamera)
        {
            // За один горизонтальний блок промінь
            // опускається на половину блока.
            const float verticalSpeed = 0.5f;
            const float epsilon = 0.00001f;
            const int maxSteps = byte.MaxValue * 3 + 3;

            bool movesAlongX = awayFromCamera.x != 0;

            int horizontalStep = movesAlongX
                ? awayFromCamera.x
                : awayFromCamera.z;

            if (horizontalStep == 0)
                return 0;

            float horizontalOrigin = movesAlongX
                ? rayOrigin.x
                : rayOrigin.z;

            // На межі обираємо клітинку в напрямку променя.
            int horizontalCell = horizontalStep > 0
                ? (int)math.floor(horizontalOrigin)
                : (int)math.ceil(horizontalOrigin) - 1;

            int verticalCell = (int)math.ceil(rayOrigin.y) - 1;

            int3 currentWorld = (int3)math.floor(rayOrigin);

            if (movesAlongX)
                currentWorld.x = horizontalCell;
            else
                currentWorld.z = horizontalCell;

            currentWorld.y = verticalCell;

            float nextHorizontal = horizontalStep > 0
                ? horizontalCell + 1f - horizontalOrigin
                : horizontalOrigin - horizontalCell;

            float nextVertical =
                (rayOrigin.y - verticalCell) / verticalSpeed;

            float distance = 0f;
            float maxDistance = byte.MaxValue / verticalSpeed;

            for (int step = 0; step < maxSteps; step++)
            {
                BlockData block = blockAccessor.GetBlockOrAir(
                    chunkCoordinate,
                    currentWorld.x,
                    currentWorld.y,
                    currentWorld.z);

                // Завершуємо на дні або при виході з води.
                if (!IsWater(block.BlockId))
                    break;

                float nextBoundary = math.min(
                    nextHorizontal,
                    nextVertical);

                distance = math.min(nextBoundary, maxDistance);

                if (distance >= maxDistance)
                    break;

                bool crossHorizontal =
                    nextHorizontal <= nextBoundary + epsilon;

                bool crossVertical =
                    nextVertical <= nextBoundary + epsilon;

                if (crossHorizontal)
                {
                    currentWorld += awayFromCamera;
                    nextHorizontal += 1f;
                }

                if (crossVertical)
                {
                    currentWorld.y--;
                    nextVertical += 1f / verticalSpeed;
                }
            }

            // Один блок вертикальної товщини =
            // одна одиниця оптичної глибини.
            float depth = distance * verticalSpeed;

            return (byte)math.clamp(
                (int)math.ceil(depth - epsilon),
                0,
                byte.MaxValue);
        }

        private static bool IsOpaque(BlockId blockId)
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
                chunkCoordinate.x * ChunkSettings.SizeX +
                localPosition.x + 0.5f,

                localPosition.y + heightOffset,

                chunkCoordinate.y * ChunkSettings.SizeZ +
                localPosition.z + 0.5f);
        }

        private static bool ShouldClipOpaqueFace(
            bool projectionClippingEnabled,
            float3 playerPosition,
            float3 facePosition,
            int blockY,
            ViewDirection direction)
        {
            if (!projectionClippingEnabled)
                return false;

            int playerFloorY = (int)math.floor(
                playerPosition.y + PlayerClipCenterOffset + 0.01f);

            if (blockY < playerFloorY)
                return false;

            float3 playerClipCenterPosition =
                playerPosition +
                new float3(0f, PlayerClipCenterOffset, 0f);

            int2 towardCamera =
                ViewDirectionUtility.Forward(direction);

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
                return false;

            float2 projectedFacePosition =
                ProjectWorldPosition(facePosition, direction);

            float2 projectedPlayerPosition =
                ProjectWorldPosition(
                    playerClipCenterPosition,
                    direction);

            return math.distancesq(
                       projectedFacePosition,
                       projectedPlayerPosition) <
                   ProjectionClipRadius * ProjectionClipRadius;
        }

        private static void TryEmitCutTop(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 upperWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceCellIndex,
            bool projectionClippingEnabled,
            float3 playerPosition)
        {
            if (!projectionClippingEnabled)
                return;

            int clipFromBlockY = (int)math.floor(
                playerPosition.y + PlayerClipCenterOffset + 0.01f);

            if (upperWorld.y != clipFromBlockY)
                return;

            int3 belowWorld = upperWorld + new int3(0, -1, 0);

            if (!ChunkUtility.IsInside(
                    belowWorld.x,
                    belowWorld.y,
                    belowWorld.z))
            {
                return;
            }

            BlockData belowBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                belowWorld.x,
                belowWorld.y,
                belowWorld.z);

            if (!IsOpaque(belowBlock.BlockId))
                return;

            float3 upperFacePosition = GetGlobalPosition(
                chunkCoordinate,
                upperWorld,
                0.5f);

            if (!ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    upperFacePosition,
                    upperWorld.y,
                    direction))
            {
                return;
            }

            writer.TryAddCutTop(
                belowBlock,
                sourceCellIndex,
                projectionX,
                projectionY);
        }

        private static float2 ProjectWorldPosition(
            float3 worldPosition,
            ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front => new float2(
                    worldPosition.x,
                    worldPosition.y - worldPosition.z * 0.5f),

                ViewDirection.Back => new float2(
                    -worldPosition.x,
                    worldPosition.y + worldPosition.z * 0.5f),

                ViewDirection.Right => new float2(
                    worldPosition.z,
                    worldPosition.y + worldPosition.x * 0.5f),

                ViewDirection.Left => new float2(
                    -worldPosition.z,
                    worldPosition.y - worldPosition.x * 0.5f),

                _ => float2.zero
            };
        }

        private static bool IsWater(BlockId blockId)
        {
            return blockId == BlockId.OceanWater;
        }
    }
}