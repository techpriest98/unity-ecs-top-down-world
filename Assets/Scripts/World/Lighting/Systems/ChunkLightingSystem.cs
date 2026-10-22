using Game.World.Chunks;
using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SunShadowStateSystem))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateBefore(typeof(ChunkProceduralRenderSystem))]
    public partial struct ChunkLightingSystem : ISystem
    {
        private const int MaxChunksLitPerFrame = 1;
        private const int MaxShadowChunksPerFrame = 1;
        private const float MaximumShadowDistance = 16f;
        private const byte MaximumSkyLight = 15;

        public static ushort GetLightSourceIndex(ProjectedCellData cell)
        {
            // A half Top lies inside its owner's cell, not in the cell above it.
            int layerSize = ChunkSettings.SizeX * ChunkSettings.SizeZ;
            return cell.IsLowerHalf && cell.FaceType == ProjectedFaceType.Top &&
                   cell.SourceAirIndex >= layerSize
                ? (ushort)(cell.SourceAirIndex - layerSize)
                : cell.SourceAirIndex;
        }

        private EntityQuery generatedChunksQuery;
        private EntityQuery chunksNeedingLightingQuery;
        private EntityQuery chunksNeedingShadowUpdateQuery;

        public void OnCreate(ref SystemState state)
        {
            generatedChunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated>()
                .Build(ref state);

            chunksNeedingLightingQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, ChunkNeedsLighting>()
                .WithDisabled<ChunkNeedsSkyLight>()
                .Build(ref state);

            chunksNeedingShadowUpdateQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, ChunkNeedsSunShadowUpdate>()
                .Build(ref state);

            state.RequireForUpdate<SunShadowState>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            bool cpuDisabled = SystemAPI.TryGetSingleton<SunLightingSettings>(out var settings) && !settings.CpuShadows;
            if (cpuDisabled)
                foreach (var pending in SystemAPI.Query<EnabledRefRW<ChunkNeedsSunShadowUpdate>>().WithAll<ChunkGenerated>())
                    pending.ValueRW = false;
            bool needsLighting =
                chunksNeedingLightingQuery.CalculateEntityCount() > 0;
            bool needsShadowUpdate = !cpuDisabled &&
                chunksNeedingShadowUpdateQuery.CalculateEntityCount() > 0;

            if (!needsLighting && !needsShadowUpdate)
            {
                return;
            }

            Entity shadowStateEntity =
                SystemAPI.GetSingletonEntity<SunShadowState>();

            SunShadowState shadowState =
                state.EntityManager.GetComponentData<SunShadowState>(
                    shadowStateEntity);

            int generatedChunkCount =
                generatedChunksQuery.CalculateEntityCount();

            var chunkEntities = new NativeParallelHashMap<int2, Entity>(
                math.max(generatedChunkCount, 1),
                Allocator.Temp);

            foreach (var (chunk, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>>()
                         .WithAll<ChunkGenerated>()
                         .WithEntityAccess())
            {
                chunkEntities.TryAdd(chunk.ValueRO.Coordinate, entity);
            }

            BufferLookup<Game.World.Blocks.BlockData> blockLookup =
                SystemAPI.GetBufferLookup<Game.World.Blocks.BlockData>(true);

            ChunkBlockAccessor blockAccessor = new(
                chunkEntities,
                blockLookup, SystemAPI.GetBufferLookup<Game.World.Blocks.SplitCellData>(true));

            ChunkLightAccessor lightAccessor = new(
                chunkEntities, SystemAPI.GetBufferLookup<VoxelLightData>(true));

            ViewDirection direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(out var transition) &&
                transition.IsActive)
                direction = transition.TargetDirection;

            EntityCommandBuffer ecb = new(Allocator.Temp);
            var directionCache = new LocalLightDirectionCache(1024, Allocator.Temp);

            foreach (var (chunk, voxelLight, cells, waterCells, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<VoxelLightData>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>>()
                         .WithAll<ChunkGenerated, ChunkNeedsLighting,
                             ChunkNeedsImmediateLighting>()
                         .WithDisabled<ChunkNeedsSkyLight>()
                         .WithEntityAccess())
            {
                UpdateChunkFully(
                    cells,
                    waterCells,
                    voxelLight,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    lightAccessor,
                    direction,
                    shadowState, ref directionCache);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsImmediateLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsSunShadowUpdate>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);
            }

            int processedCount = 0;

            foreach (var (chunk, voxelLight, cells, waterCells, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<VoxelLightData>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>>()
                         .WithAll<ChunkGenerated, ChunkNeedsLighting>()
                         .WithDisabled<ChunkNeedsSkyLight>()
                         .WithDisabled<ChunkNeedsImmediateLighting>()
                         .WithEntityAccess())
            {
                if (processedCount >= MaxChunksLitPerFrame)
                {
                    break;
                }

                UpdateChunkFully(
                    cells,
                    waterCells,
                    voxelLight,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    lightAccessor,
                    direction,
                    shadowState, ref directionCache);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsSunShadowUpdate>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);
                processedCount++;
            }

            if (shadowState.IsTransitioning)
            {
                byte targetMaskIndex =
                    (byte)(1 - shadowState.ActiveMaskIndex);

                processedCount = 0;

                foreach (var (chunk, cells, waterCells, entity) in
                         SystemAPI.Query<
                                 RefRO<ChunkComponent>,
                                 DynamicBuffer<ProjectedCellData>,
                                 DynamicBuffer<ProjectedWaterCellData>>()
                             .WithAll<ChunkGenerated, ChunkNeedsSunShadowUpdate>()
                             .WithDisabled<ChunkNeedsLighting>()
                             .WithEntityAccess())
                {
                    if (processedCount >= MaxShadowChunksPerFrame)
                    {
                        break;
                    }

                    UpdateChunkShadowMask(
                        cells,
                        waterCells,
                        chunk.ValueRO.Coordinate,
                        blockAccessor,
                        shadowState.TargetLight,
                        targetMaskIndex);

                    // Upload the inactive mask as each chunk finishes. The
                    // range uploader only visits chunks marked for rendering;
                    // flipping the global mask alone leaves stale GPU shadows.
                    ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);
                    ecb.SetComponentEnabled<ChunkNeedsSunShadowUpdate>(
                        entity,
                        false);

                    processedCount++;
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            directionCache.Dispose();
            chunkEntities.Dispose();

            if (shadowState.IsTransitioning &&
                chunksNeedingShadowUpdateQuery.CalculateEntityCount() == 0)
            {
                shadowState.ActiveMaskIndex =
                    (byte)(1 - shadowState.ActiveMaskIndex);
                shadowState.ActiveLight = shadowState.TargetLight;
                shadowState.IsTransitioning = false;

                state.EntityManager.SetComponentData(
                    shadowStateEntity,
                    shadowState);

                foreach (EnabledRefRW<ChunkNeedsRender> needsRender in
                         SystemAPI.Query<EnabledRefRW<ChunkNeedsRender>>()
                             .WithAll<ChunkGenerated>()
                             .WithOptions(
                                 EntityQueryOptions.IgnoreComponentEnabledState))
                {
                    needsRender.ValueRW = true;
                    break;
                }
            }
        }

        private static void UpdateChunkFully(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<ProjectedWaterCellData> waterCells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            ChunkLightAccessor lightAccessor,
            ViewDirection direction,
            SunShadowState shadowState, ref LocalLightDirectionCache directionCache)
        {
            byte targetMaskIndex =
                (byte)(1 - shadowState.ActiveMaskIndex);

            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedCellData cell = cells[i];
                cell.LightData = CalculateFullLightData(
                    cell,
                    voxelLight,
                    chunkCoordinate,
                    blockAccessor,
                    shadowState,
                    targetMaskIndex);
                UpdateLocalLightCorners(ref cell, chunkCoordinate, blockAccessor, lightAccessor, direction, ref directionCache);
                cells[i] = cell;
            }

            for (int i = 0; i < waterCells.Length; i++)
            {
                ProjectedWaterCellData waterCell = waterCells[i];
                ProjectedCellData cell = waterCell.Value;
                cell.LightData = CalculateFullLightData(
                    cell,
                    voxelLight,
                    chunkCoordinate,
                    blockAccessor,
                    shadowState,
                    targetMaskIndex);
                UpdateLocalLightCorners(ref cell, chunkCoordinate, blockAccessor, lightAccessor, direction, ref directionCache);
                waterCell.Value = cell;
                waterCells[i] = waterCell;
            }
        }

        public static void UpdateLocalLightCorners(
            ref ProjectedCellData cell,
            int2 chunkCoordinate,
            ChunkBlockAccessor blocks,
            ChunkLightAccessor lights,
            ViewDirection direction, ref LocalLightDirectionCache directionCache)
        {
            int3 position = ChunkUtility.ToLocalPosition(GetLightSourceIndex(cell));
            int3 right = direction switch
            {
                ViewDirection.Front => new int3(1, 0, 0),
                ViewDirection.Back => new int3(-1, 0, 0),
                ViewDirection.Right => new int3(0, 0, 1),
                ViewDirection.Left => new int3(0, 0, -1),
                _ => new int3(1, 0, 0)
            };

            ProjectedFaceType face = cell.FaceType;
            bool isTop = face == ProjectedFaceType.Top;
            int3 up = isTop
                ? ViewDirectionUtility.GetAwayFromCameraOffset(direction)
                : new int3(0, 1, 0);

            uint bottomLeft = VoxelLightSmoothingUtility.SampleCorner(
                chunkCoordinate, position, -right, -up, blocks, lights);
            uint bottomRight = VoxelLightSmoothingUtility.SampleCorner(
                chunkCoordinate, position, right, -up, blocks, lights);
            uint topLeft = VoxelLightSmoothingUtility.SampleCorner(
                chunkCoordinate, position, -right, up, blocks, lights);
            uint topRight = VoxelLightSmoothingUtility.SampleCorner(
                chunkCoordinate, position, right, up, blocks, lights);

            if (!isTop)
            {
                uint middleLeft = AverageCornerLight(bottomLeft, topLeft);
                uint middleRight = AverageCornerLight(bottomRight, topRight);
                if (face == ProjectedFaceType.SideUpper && !cell.IsLowerHalf)
                {
                    bottomLeft = middleLeft;
                    bottomRight = middleRight;
                }
                else
                {
                    topLeft = middleLeft;
                    topRight = middleRight;
                }
            }

            cell.LocalLightBottomLeft = bottomLeft;
            cell.LocalLightBottomRight = bottomRight;
            cell.LocalLightTopLeft = topLeft;
            cell.LocalLightTopRight = topRight;
            cell.LocalLightDirection = 0u;
            if (cell.BlockId != BlockId.OceanWater && cell.BlockId != BlockId.RiverWater &&
                ((bottomLeft | bottomRight | topLeft | topRight) & 0xFFFFFFu) != 0)
            {
                byte dl = VoxelLightSmoothingUtility.SampleCornerDirection(chunkCoordinate, position, -right, -up, blocks, lights, ref directionCache);
                byte dr = VoxelLightSmoothingUtility.SampleCornerDirection(chunkCoordinate, position, right, -up, blocks, lights, ref directionCache);
                byte ul = VoxelLightSmoothingUtility.SampleCornerDirection(chunkCoordinate, position, -right, up, blocks, lights, ref directionCache);
                byte ur = VoxelLightSmoothingUtility.SampleCornerDirection(chunkCoordinate, position, right, up, blocks, lights, ref directionCache);
                if (!isTop)
                {
                    byte ml = VoxelLightSmoothingUtility.AverageDirection(dl, ul);
                    byte mr = VoxelLightSmoothingUtility.AverageDirection(dr, ur);
                    if (face == ProjectedFaceType.SideUpper && !cell.IsLowerHalf) { dl = ml; dr = mr; }
                    else { ul = ml; ur = mr; }
                }
                cell.LocalLightDirection = (uint)dl | ((uint)dr << 8) | ((uint)ul << 16) | ((uint)ur << 24);
            }
        }

        private static uint AverageCornerLight(uint a, uint b)
        {
            return LightDataUtility.AverageCornerLight(a, b);
        }

        private static void UpdateChunkShadowMask(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<ProjectedWaterCellData> waterCells,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData targetLight,
            byte targetMaskIndex)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedCellData cell = cells[i];
                cell.LightData = ReplaceSunVisibility(
                    cell,
                    cell.LightData,
                    chunkCoordinate,
                    blockAccessor,
                    targetLight,
                    targetMaskIndex);
                cells[i] = cell;
            }

            for (int i = 0; i < waterCells.Length; i++)
            {
                ProjectedWaterCellData waterCell = waterCells[i];
                ProjectedCellData cell = waterCell.Value;
                cell.LightData = ReplaceSunVisibility(
                    cell,
                    cell.LightData,
                    chunkCoordinate,
                    blockAccessor,
                    targetLight,
                    targetMaskIndex);
                waterCell.Value = cell;
                waterCells[i] = waterCell;
            }
        }

        private static uint CalculateFullLightData(
            ProjectedCellData cell,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            SunShadowState shadowState,
            byte targetMaskIndex)
        {
            bool activeVisible = shadowState.CpuShadowsDisabled || IsSunVisible(
                cell,
                chunkCoordinate,
                blockAccessor,
                shadowState.ActiveLight);

            int lightSourceIndex = GetLightSourceIndex(cell);
            VoxelLightData voxel = lightSourceIndex < voxelLight.Length
                ? voxelLight[lightSourceIndex]
                : new VoxelLightData(MaximumSkyLight);

            float3 localLight = new float3(
                voxel.R,
                voxel.G,
                voxel.B) / 255f;

            uint lightData = LightDataUtility.Pack(
                localLight,
                voxel.Sky,
                activeVisible);

            if (shadowState.IsTransitioning)
            {
                bool targetVisible = IsSunVisible(
                    cell,
                    chunkCoordinate,
                    blockAccessor,
                    shadowState.TargetLight);

                lightData = LightDataUtility.ReplaceSunVisibility(
                    lightData,
                    targetMaskIndex,
                    targetVisible);
            }

            return lightData;
        }

        private static uint ReplaceSunVisibility(
            ProjectedCellData cell,
            uint lightData,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light,
            byte maskIndex)
        {
            bool visible = IsSunVisible(
                cell,
                chunkCoordinate,
                blockAccessor,
                light);

            return LightDataUtility.ReplaceSunVisibility(
                lightData,
                maskIndex,
                visible);
        }

        private static bool IsSunVisible(
            ProjectedCellData cell,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light)
        {
            if (light.Intensity <= 0f)
            {
                return true;
            }

            int3 sourceAirPosition =
                ChunkUtility.ToLocalPosition(GetLightSourceIndex(cell));

            return !VoxelShadowUtility.IsOccluded(
                chunkCoordinate,
                sourceAirPosition,
                blockAccessor,
                light.DirectionToLight,
                MaximumShadowDistance);
        }
    }
}
