using Game.World.Chunks;
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
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            bool needsLighting =
                chunksNeedingLightingQuery.CalculateEntityCount() > 0;
            bool needsShadowUpdate =
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
                blockLookup);

            EntityCommandBuffer ecb = new(Allocator.Temp);

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
                    shadowState);

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
                    shadowState);

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

                    ecb.SetComponentEnabled<ChunkNeedsSunShadowUpdate>(
                        entity,
                        false);

                    processedCount++;
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
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
            SunShadowState shadowState)
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
                waterCell.Value = cell;
                waterCells[i] = waterCell;
            }
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
            bool activeVisible = IsSunVisible(
                cell,
                chunkCoordinate,
                blockAccessor,
                shadowState.ActiveLight);

            VoxelLightData voxel = cell.SourceAirIndex < voxelLight.Length
                ? voxelLight[cell.SourceAirIndex]
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
                ChunkUtility.ToLocalPosition(cell.SourceAirIndex);

            return !VoxelShadowUtility.IsOccluded(
                chunkCoordinate,
                sourceAirPosition,
                blockAccessor,
                light.DirectionToLight,
                MaximumShadowDistance);
        }
    }
}
