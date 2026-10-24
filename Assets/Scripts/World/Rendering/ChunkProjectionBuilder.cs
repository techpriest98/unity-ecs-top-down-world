using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static partial class ChunkProjectionBuilder
    {
        private const float PlayerClipCenterOffset = 1f;

        public const float ProjectionClipRadius = 4f;
        // Select complete quads touching the local region; never crop pixels inside a selected tile.
        public static bool TileTouchesLocalProjection(float2 tileCenter, float2 regionCenter)
        {
            float2 nearest = math.max(math.abs(tileCenter - regionCenter) - new float2(.5f, .25f), float2.zero);
            return math.lengthsq(nearest) < ProjectionClipRadius * ProjectionClipRadius;
        }
        public static bool CanReuseProjectionColumns(float3 previous, bool previousClipping, float3 current, bool currentClipping) =>
            (previousClipping && GetClippingHeight(previous) % 1f != 0f) ==
            (currentClipping && GetClippingHeight(current) % 1f != 0f);
        // Conservative horizontal support of the old and new clipping circles, including edge faces.
        public static int2 MovingClipColumns(int2 coordinate, ViewDirection direction, float3 previous,
            bool previousClipping, float3 current, bool currentClipping)
        {
            float first = 16, last = -1;
            for (int u = 0; u < 16; u++)
            {
                int3 local = ViewCoordinateUtility.ViewToWorld(new ViewCoordinate(u, 0, 0), new int2(16), direction);
                bool xAxis = direction == ViewDirection.Front || direction == ViewDirection.Back;
                float horizontal = xAxis ? coordinate.x * 16 + local.x + .5f : coordinate.y * 16 + local.z + .5f;
                float oldCenter = xAxis ? previous.x : previous.z;
                float newCenter = xAxis ? current.x : current.z;
                if ((previousClipping && math.abs(horizontal - oldCenter) <= ProjectionClipRadius + 2) ||
                    (currentClipping && math.abs(horizontal - newCenter) <= ProjectionClipRadius + 2))
                { first = math.min(first, u); last = math.max(last, u); }
            }
            return last < first ? new int2(0, 0) : new int2((int)first, (int)(last - first + 1));
        }
        public static bool UsesLocalProjectionOverlay(bool enabled, bool candidate, float2 cellCenter, float2 playerCenter) =>
            enabled && candidate && math.distancesq(cellCenter, playerCenter) < ProjectionClipRadius * ProjectionClipRadius;
        // Cut faces close the boundary of the clipping volume. Their quad centres can lie
        // outside the circle even though the face itself forms its edge; never leave them in the base layer.
        public static bool UsesLocalProjectionLayer(bool enabled, ProjectedCellData cell, bool candidate,
            float2 cellCenter, float2 playerCenter) => enabled && (cell.IsCutTop || cell.IsCutSide ||
                UsesLocalProjectionOverlay(true, candidate, cellCenter, playerCenter));

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
            BuildColumns(blocks, chunkCoordinate, blockAccessor, writer, waterWriter, direction,
                projectionClippingEnabled, playerPosition, 0, int.MaxValue);
        }

        // Select screen-space columns/rows, not a world-space distance from the player.
        // Every height/depth pair capable of contributing to those rows is still visited.
        // The caller owns output clearing/merging and must use fresh occupancy for this build.
        public static void BuildColumns(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            WaterProjectionWriter waterWriter,
            ViewDirection direction,
            bool projectionClippingEnabled,
            float3 playerPosition,
            int firstColumn,
            int columnCount,
            int firstRow = -10000,
            int lastRow = 10000)
        {
            int2 chunkSizeXZ = new int2(
                ChunkSettings.SizeX,
                ChunkSettings.SizeZ);

            ViewBounds bounds =
                ViewCoordinateUtility.GetViewBounds(
                    chunkSizeXZ,
                    ChunkSettings.SizeY,
                    direction);

            int3 away = ViewDirectionUtility.GetAwayFromCameraOffset(direction);
            bool splitPath = blockAccessor.HasSplitCells(chunkCoordinate) ||
                blockAccessor.HasSplitCells(chunkCoordinate + new int2(away.x, away.z)) ||
                (projectionClippingEnabled && GetClippingHeight(playerPosition) % 1f != 0f);

            int startColumn = math.clamp(firstColumn, 0, bounds.Width);
            int endColumn = startColumn + math.min(math.max(columnCount, 0), bounds.Width - startColumn);
            int4 traversal = ProjectionTraversalRange(bounds.Height, bounds.Depth, firstRow, lastRow);
            for (int u = startColumn; u < endColumn; u++)
            {
                for (int heightFromTop = traversal.x; heightFromTop < traversal.y; heightFromTop++)
                {
                    int h = bounds.Height - 1 - heightFromTop;
                    int firstDepth = math.max(0, traversal.z - heightFromTop * 2);
                    int lastDepth = math.min(bounds.Depth - 1, traversal.w - heightFromTop * 2);
                    for (int v = lastDepth; v >= firstDepth; v--)
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
                            playerPosition, splitPath);
                    }
                }
            }
        }

        // projectionY = 2 * heightFromTop + depth. Preserve the old emission margin,
        // while solving the inequalities before entering the expensive traversal.
        public static int4 ProjectionTraversalRange(int height, int depth, int firstRow, int lastRow)
        {
            if (height <= 0 || depth <= 0 || firstRow > lastRow) return new int4(0, 0, 0, -1);
            // Saturate before adding margins to avoid overflow for unbounded callers.
            int maximumRow = (height - 1) * 2 + depth - 1;
            int low = math.max(math.clamp(firstRow, -3, maximumRow + 4) - 3, 0);
            int high = math.min(math.clamp(lastRow, -4, maximumRow + 3) + 3, maximumRow);
            if (low > high) return new int4(0, 0, low, high);
            int firstHeight = math.max(0, (low - (depth - 1) + 1) / 2);
            int endHeight = math.min(height - 1, high / 2) + 1;
            return new int4(firstHeight, endHeight, low, high);
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
            float3 playerPosition,
            bool splitPath)
        {
            if (splitPath && TryProcessSplitCell(blocks, chunkCoordinate, blockAccessor, writer, waterWriter,
                    world, direction, projectionX, projectionY, projectionClippingEnabled, playerPosition))
                return;
            BlockData currentBlock = GetBlockOrAir(
                blocks,
                chunkCoordinate,
                blockAccessor,
                world);

            // The mask describes the clipped volume, including air and interior
            // cells, rather than only the visible faces emitted at its boundary.
            RecordClippedVolume(writer, chunkCoordinate, world, projectionX, projectionY,
                projectionClippingEnabled, playerPosition, direction);

            ushort sourceAirIndex = checked(
                (ushort)ChunkUtility.ToIndex(
                    world.x,
                    world.y,
                    world.z));

            // Only the cell immediately above a half block needs the extra
            // Top pass. Read the local buffer directly, without chunk lookups.
            bool hasHalfBelow = world.y > 0 &&
                blocks[sourceAirIndex - ChunkSettings.SizeX * ChunkSettings.SizeZ].Shape == BlockShape.LowerHalf;

            ProcessOpaqueCell(
                chunkCoordinate,
                blockAccessor,
                writer,
                currentBlock,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex,
                projectionClippingEnabled,
                playerPosition,
                hasHalfBelow);

            ProcessWaterCell(
                chunkCoordinate,
                blockAccessor,
                waterWriter,
                currentBlock,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex,
                hasHalfBelow, projectionClippingEnabled, playerPosition);
        }

        private static void ProcessOpaqueCell(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            BlockData currentBlock,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex,
            bool projectionClippingEnabled,
            float3 playerPosition,
            bool hasHalfBelow)
        {
            // The gap above a lower-half block exists even under another block.
            if (hasHalfBelow)
                TryEmitOpaqueTop(chunkCoordinate, blockAccessor, writer, world, direction,
                    projectionX, projectionY, sourceAirIndex, projectionClippingEnabled, playerPosition, halfOnly: true);

            if (IsOpaque(currentBlock.BlockId))
            {
                if (currentBlock.Shape == BlockShape.LowerHalf)
                    TryEmitOpaqueSide(chunkCoordinate, blockAccessor, writer, world, direction,
                        projectionX, projectionY, sourceAirIndex, projectionClippingEnabled, playerPosition, upperOnly: true);
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

                TryEmitCutSide(
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
            BlockData currentBlock,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex,
            bool hasHalfBelow, bool clipping, float3 player)
        {
            if (hasHalfBelow)
                TryEmitWaterTop(chunkCoordinate, blockAccessor, writer, world, direction,
                    projectionX, projectionY, sourceAirIndex, halfOnly: true, clipping: clipping, player: player);
            if (!BlockUtility.IsAir(currentBlock.BlockId))
            {
                if (currentBlock.Shape == BlockShape.LowerHalf)
                    TryEmitWaterSide(chunkCoordinate, blockAccessor, writer, world, direction,
                        projectionX, projectionY, sourceAirIndex, upperOnly: true, clipping: clipping, player: player);
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
                sourceAirIndex, clipping: clipping, player: player);

            TryEmitWaterTop(
                chunkCoordinate,
                blockAccessor,
                writer,
                world,
                direction,
                projectionX,
                projectionY,
                sourceAirIndex, clipping: clipping, player: player);
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
            float3 playerPosition,
            bool halfOnly = false)
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
            bool half = belowBlock.Shape == BlockShape.LowerHalf;
            if (half != halfOnly) return;
            if (half) projectionY++;

            float3 facePosition = GetGlobalPosition(
                chunkCoordinate,
                belowWorld,
                half ? 0.5f : 1f);

            if (ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    facePosition,
                    belowWorld.y,
                    direction, chunkCoordinate, projectionX, projectionY))
            {
                writer.RecordClippedCell(projectionX, projectionY);
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
                projectionY,
                BuildShoreMask(chunkCoordinate, blockAccessor, belowWorld, half, false, direction),
                TopNeighborMaskUtility.BuildFrontTransition(chunkCoordinate, belowWorld, blockAccessor,
                    direction, half, belowBlock.BlockId));
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
            float3 playerPosition,
            bool upperOnly = false)
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
            bool half = block.Shape == BlockShape.LowerHalf;
            if (upperOnly && half) return;

            float3 facePosition = GetGlobalPosition(
                chunkCoordinate,
                blockWorld,
                half ? 0.25f : 0.5f);

            if (ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    facePosition,
                    blockWorld.y,
                    direction, chunkCoordinate, projectionX, projectionY - (half ? 1 : 2), !half && !upperOnly ? projectionY - 1 : -1))
            {
                writer.RecordClippedCell(projectionX, checked((ushort)(projectionY - (half ? 1 : 2))));
                if (!half && !upperOnly) writer.RecordClippedCell(projectionX, checked((ushort)(projectionY - 1)));
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
                (!half && IsOpaque(aboveBlock.BlockId) ? 1 : 0) |
                (IsOpaque(rightBlock.BlockId) && (half || rightBlock.Shape == BlockShape.Full) ? 2 : 0) |
                (IsOpaque(leftBlock.BlockId) && (half || leftBlock.Shape == BlockShape.Full) ? 8 : 0));

            int3 footWorld =
                transparentWorld + new int3(0, -1, 0);

            BlockData footBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                footWorld.x,
                footWorld.y,
                footWorld.z);

            BlockId footBlockId = IsOpaque(footBlock.BlockId) && footBlock.Shape == BlockShape.Full
                ? footBlock.BlockId
                : BlockId.Air;

            // The visible upper face rests against the Top of the half block
            // in front, rather than against the floor one cell below it.
            if (upperOnly)
                footBlockId = blockAccessor.GetBlockOrAir(chunkCoordinate,
                    transparentWorld.x, transparentWorld.y, transparentWorld.z).BlockId;

            writer.TryAddSideUpper(
                block,
                neighborMask,
                footBlockId,
                sourceAirIndex,
                projectionX,
                checked((ushort)(projectionY - (half ? 1 : 2))),
                raisedFoot: upperOnly);

            if (half || upperOnly) return;
            byte lowerMask = (byte)((neighborMask & 1) |
                (IsOpaque(rightBlock.BlockId) ? 2 : 0) | (IsOpaque(leftBlock.BlockId) ? 8 : 0));
            writer.TryAddSideLower(
                block,
                lowerMask,
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
            ushort sourceAirIndex,
            bool halfOnly = false, bool clipping = false, float3 player = default)
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
            bool half = belowBlock.Shape == BlockShape.LowerHalf;
            if (half != halfOnly) return;
            if (half) projectionY++;
            if (ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(chunkCoordinate, belowWorld, half ? .5f : 1f), belowWorld.y, direction, chunkCoordinate, projectionX, projectionY)) return;

            int3 awayFromCamera =
                ViewDirectionUtility.GetAwayFromCameraOffset(direction);

            float3 rayOrigin = new float3(
                belowWorld.x + 0.5f,
                belowWorld.y + (half ? 0.5f : 1f),
                belowWorld.z + 0.5f);

            byte opticalDepth = MeasureWaterDepth(
                chunkCoordinate,
                blockAccessor,
                rayOrigin,
                awayFromCamera);

            int3 behindWorld = belowWorld + awayFromCamera;

            CellContents behindContents = blockAccessor.GetContentsOrAir(
                chunkCoordinate,
                behindWorld.x,
                behindWorld.y,
                behindWorld.z);

            // The water surface meets the neighbour's upper half, not its
            // compatibility geometry (which may report the solid lower half).
            bool topInset = !half && IsOpaque(behindContents.Upper.Id);

            writer.TryAddTop(
                belowBlock,
                opticalDepth,
                topInset,
                sourceAirIndex,
                projectionX,
                projectionY,
                BuildShoreMask(chunkCoordinate, blockAccessor, belowWorld, half, true, direction),
                skyVisible: IsSkyVisibleWaterSurface(chunkCoordinate, blockAccessor, belowWorld, half));
        }

        private static void TryEmitWaterSide(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            WaterProjectionWriter writer,
            int3 airWorld,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceAirIndex,
            bool upperOnly = false, bool clipping = false, float3 player = default)
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
            bool half = block.Shape == BlockShape.LowerHalf;
            if (half && upperOnly) return;
            if (ShouldClipOpaqueFace(clipping, player,
                    GetGlobalPosition(chunkCoordinate, blockWorld, half ? .25f : .5f), blockWorld.y, direction, chunkCoordinate, projectionX, projectionY - (half ? 1 : 2), !half && !upperOnly ? projectionY - 1 : -1)) return;

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
                half ? lowerDepth : upperDepth,
                sourceAirIndex,
                projectionX,
                checked((ushort)(projectionY - (half ? 1 : 2))));

            if (half || upperOnly) return;
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
            const float verticalSpeed = 0.5f;
            const float epsilon = 0.00001f;
            const int maxSteps = byte.MaxValue * 5 + 5;

            bool movesAlongX = awayFromCamera.x != 0;

            int horizontalStep = movesAlongX
                ? awayFromCamera.x
                : awayFromCamera.z;

            if (horizontalStep == 0)
                return 0;

            float horizontalOrigin = movesAlongX
                ? rayOrigin.x
                : rayOrigin.z;

            int horizontalCell = horizontalStep > 0
                ? (int)math.floor(horizontalOrigin)
                : (int)math.ceil(horizontalOrigin) - 1;

            int verticalCell = (int)math.ceil(rayOrigin.y * 2f) - 1;

            int3 currentWorld = (int3)math.floor(rayOrigin);

            if (movesAlongX)
                currentWorld.x = horizontalCell;
            else
                currentWorld.z = horizontalCell;

            currentWorld.y = (int)math.floor(verticalCell * .5f);

            float nextHorizontal = horizontalStep > 0
                ? horizontalCell + 1f - horizontalOrigin
                : horizontalOrigin - horizontalCell;

            float nextVertical =
                (rayOrigin.y - verticalCell * .5f) / verticalSpeed;

            float distance = 0f;
            float maxDistance = byte.MaxValue / verticalSpeed;

            for (int step = 0; step < maxSteps; step++)
            {
                CellContents contents = blockAccessor.GetContentsOrAir(
                    chunkCoordinate,
                    currentWorld.x,
                    currentWorld.y,
                    currentWorld.z);

                BlockId material = (verticalCell & 1) == 0 ? contents.Lower.Id : contents.Upper.Id;
                if (!IsWater(material))
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
                    verticalCell--;
                    currentWorld.y = (int)math.floor(verticalCell * .5f);
                    nextVertical += .5f / verticalSpeed;
                }
            }

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

        public static float GetClippingHeight(float3 playerPosition) =>
            math.floor((playerPosition.y + .01f) * 2f) * .5f;

        public static bool ShouldClipLocalObject(float3 worldPosition, float3 playerPosition, ViewDirection direction) =>
            worldPosition.y > GetClippingHeight(playerPosition) + .001f &&
            GetDepthDistance(playerPosition, worldPosition, direction) > 0;

        private static void RecordClippedVolume(ProjectionWriter writer, int2 coordinate, int3 position,
            ushort x, ushort y, bool clipping, float3 player, ViewDirection direction)
        {
            if (!clipping) return;
            if (ShouldClipOpaqueFace(clipping, player, GetGlobalPosition(coordinate, position, .25f), position.y, direction, coordinate, x, y))
                writer.RecordClippedCell(x, y);
            if (y > 0 && ShouldClipOpaqueFace(clipping, player, GetGlobalPosition(coordinate, position, .75f), position.y, direction, coordinate, x, y - 1))
                writer.RecordClippedCell(x, (ushort)(y - 1));
        }

        // The same rendered half-height tile lattice used by local output and coverage.
        public static bool IsLocalProjectionCell(int2 coordinate, int x, int y, float3 player, ViewDirection direction)
        {
            if (y < 0) return false;
            float3 origin = ChunkRenderPositionUtility.GetPosition(coordinate, direction, 1, .5f, 0);
            float2 tileCenter = new float2(origin.x - ChunkSettings.SizeX * .5f + x + .5f,
                origin.y + (ChunkSettings.SizeY * 2 + ChunkSettings.SizeZ) * .5f - (y + .5f) * .5f);
            float2 center = WorldPositionProjectionUtility.Project(player + new float3(0, PlayerClipCenterOffset, 0), direction);
            return TileTouchesLocalProjection(tileCenter, center);
        }

        private static bool ShouldClipOpaqueFace(
            bool projectionClippingEnabled,
            float3 playerPosition,
            float3 facePosition,
            int blockY,
            ViewDirection direction, int2 coordinate, int projectionX, int projectionY, int secondRow = -1)
        {
            if (!projectionClippingEnabled)
                return false;

            if (facePosition.y <= GetClippingHeight(playerPosition) + .001f)
                return false;

            int depthDistance = GetDepthDistance(
                playerPosition,
                facePosition,
                direction);

            if (depthDistance <= 0)
                return false;

            return IsLocalProjectionCell(coordinate, projectionX, projectionY, playerPosition, direction) ||
                (secondRow >= 0 && IsLocalProjectionCell(coordinate, projectionX, secondRow, playerPosition, direction));
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

            float clipHeight = GetClippingHeight(playerPosition);
            int clipFromBlockY = (int)math.floor(clipHeight);

            if (upperWorld.y != clipFromBlockY)
                return;

            if (clipHeight > clipFromBlockY)
            {
                CellContents contents = blockAccessor.GetContentsOrAir(chunkCoordinate, upperWorld.x, upperWorld.y, upperWorld.z);
                // A real lower-half surface is already emitted. Only cap a solid
                // volume crossing the half-height cut plane.
                if (!IsOpaque(contents.Lower.Id) || !IsOpaque(contents.Upper.Id) || projectionY == 0 ||
                    upperWorld.y + 1 >= ChunkSettings.SizeY ||
                    !ShouldClipOpaqueFace(true, playerPosition, GetGlobalPosition(chunkCoordinate, upperWorld, .75f),
                        upperWorld.y, direction, chunkCoordinate, projectionX, projectionY - 1)) return;
                writer.TryAddCutTop(new BlockData(contents.Lower.Id, contents.Lower.Durability, BlockShape.LowerHalf),
                    (ushort)ChunkUtility.ToIndex(upperWorld + new int3(0, 1, 0)), projectionX, (ushort)(projectionY - 1));
                return;
            }

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
            if (belowBlock.Shape == BlockShape.LowerHalf) return; // Real half-height Top was emitted separately.

            float3 upperFacePosition = GetGlobalPosition(
                chunkCoordinate,
                upperWorld,
                0.5f);

            if (!ShouldClipOpaqueFace(
                    projectionClippingEnabled,
                    playerPosition,
                    upperFacePosition,
                    upperWorld.y,
                    direction, chunkCoordinate, projectionX, projectionY))
            {
                return;
            }

            writer.TryAddCutTop(
                belowBlock,
                sourceCellIndex,
                projectionX,
                projectionY);
        }

        private static void TryEmitCutSide(
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ProjectionWriter writer,
            int3 world,
            ViewDirection direction,
            ushort projectionX,
            ushort projectionY,
            ushort sourceCellIndex,
            bool projectionClippingEnabled,
            float3 playerPosition)
        {
            if (!projectionClippingEnabled)
                return;

            int3 behindWorld =
                world +
                ViewDirectionUtility.GetAwayFromCameraOffset(direction);

            BlockData behindBlock = blockAccessor.GetBlockOrAir(
                chunkCoordinate,
                behindWorld.x,
                behindWorld.y,
                behindWorld.z);
            
            if (!IsOpaque(behindBlock.BlockId))
                return;
            // A lower-half block in front continues only the lower cut surface.
            // Keep the existing upper-half/full-block handling below unchanged.
            if (blockAccessor.GetBlockOrAir(chunkCoordinate, world.x, world.y, world.z).Shape == BlockShape.LowerHalf)
            {
                if (behindBlock.Shape != BlockShape.UpperHalf &&
                    behindWorld.y + .5f > GetClippingHeight(playerPosition) + .001f &&
                    GetDepthDistance(playerPosition, GetGlobalPosition(chunkCoordinate, behindWorld, .5f), direction) == 0 &&
                    IsLocalProjectionCell(chunkCoordinate, projectionX, projectionY - 1, playerPosition, direction))
                    writer.TryAddCutSideLower(behindBlock, sourceCellIndex, projectionX,
                        checked((ushort)(projectionY - 1)));
                return;
            }
            bool half = behindBlock.Shape == BlockShape.LowerHalf;
            bool upperOnly = blockAccessor.GetBlockOrAir(chunkCoordinate, world.x, world.y, world.z).Shape == BlockShape.LowerHalf;
            if (half && upperOnly) return;
            float clipHeight = GetClippingHeight(playerPosition);
            if (behindWorld.y + (half ? .5f : 1f) <= clipHeight + .001f) return;

            float3 facePosition = GetGlobalPosition(
                chunkCoordinate,
                behindWorld,
                0.5f);

            if (!IsLocalProjectionCell(chunkCoordinate, projectionX, projectionY - (half ? 1 : 2), playerPosition, direction) &&
                (half || upperOnly || !IsLocalProjectionCell(chunkCoordinate, projectionX, projectionY - 1, playerPosition, direction)))
            {
                return;
            }

            if (GetDepthDistance(
                    playerPosition,
                    facePosition,
                    direction) != 0)
            {
                return;
            }


            writer.TryAddCutSideUpper(
                behindBlock,
                sourceCellIndex,
                projectionX,
                checked((ushort)(projectionY - (half ? 1 : 2))));

            if (half || upperOnly) return;
            if (behindWorld.y + .5f <= clipHeight + .001f) return;
            writer.TryAddCutSideLower(
                behindBlock,
                sourceCellIndex,
                projectionX,
                checked((ushort)(projectionY - 1)));
        }

        private static bool IsWater(BlockId blockId)
        {
            return blockId == BlockId.OceanWater || blockId == BlockId.RiverWater;
        }

        private static int GetDepthDistance(
            float3 playerPosition,
            float3 facePosition,
            ViewDirection direction)
        {
            int2 towardCamera =
                ViewDirectionUtility.Forward(direction);

            int2 playerCell = new int2(
                (int)math.floor(playerPosition.x),
                (int)math.floor(playerPosition.z));

            int2 faceCell = new int2(
                (int)math.floor(facePosition.x),
                (int)math.floor(facePosition.z));

            return math.dot(faceCell - playerCell, towardCamera);
        }

    }
}
