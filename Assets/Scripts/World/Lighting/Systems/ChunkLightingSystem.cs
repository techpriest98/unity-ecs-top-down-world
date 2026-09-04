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
    [UpdateAfter(typeof(DirectionalLightSystem))]
    [UpdateAfter(typeof(ChunkProjectionSystem))]
    [UpdateBefore(typeof(ChunkProceduralRenderSystem))]
    public partial struct ChunkLightingSystem : ISystem
    {
        private const int MaxChunksLitPerFrame = 1;
        private const float MaximumShadowDistance = 16f;
        private const byte MaximumSkyLight = 15;
        private const float ShadowDirectionEpsilon = 0.0001f;

        private EntityQuery generatedChunksQuery;
        private EntityQuery chunksNeedingLightingQuery;

        private float3 lastShadowDirection;
        private bool lastSunActive;
        private bool initialized;

        public void OnCreate(ref SystemState state)
        {
            generatedChunksQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated>()
                .Build(ref state);

            chunksNeedingLightingQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkComponent, ChunkGenerated, ChunkNeedsLighting>()
                .WithDisabled<ChunkNeedsSkyLight>()
                .Build(ref state);

            state.RequireForUpdate<DirectionalLightData>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DirectionalLightData light = SystemAPI.GetSingleton<DirectionalLightData>();
            bool sunActive = light.Intensity > 0f;

            bool shadowDirectionChanged =
                sunActive &&
                (!lastSunActive ||
                 math.distancesq(light.DirectionToLight, lastShadowDirection) > ShadowDirectionEpsilon);

            if (!initialized || shadowDirectionChanged)
            {
                MarkAllChunksForLighting(ref state);
            }

            lastShadowDirection = light.DirectionToLight;
            lastSunActive = sunActive;
            initialized = true;

            if (chunksNeedingLightingQuery.CalculateEntityCount() == 0)
            {
                return;
            }

            int generatedChunkCount = generatedChunksQuery.CalculateEntityCount();

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

            foreach (var (chunk, voxelLight, projectedCells, projectedWaterCells, entity) in
                SystemAPI.Query<
                        RefRO<ChunkComponent>,
                        DynamicBuffer<VoxelLightData>,
                        DynamicBuffer<ProjectedCellData>,
                        DynamicBuffer<ProjectedWaterCellData>>()
                    .WithAll<ChunkGenerated, ChunkNeedsLighting, ChunkNeedsImmediateLighting>()
                    .WithDisabled<ChunkNeedsSkyLight>()
                    .WithEntityAccess())
            {
                UpdateChunk(projectedCells, projectedWaterCells, voxelLight,
                    chunk.ValueRO.Coordinate, blockAccessor, light);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsImmediateLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);
            }

            int litChunkCount = 0;

            foreach (var (chunk, voxelLight, projectedCells, projectedWaterCells, entity) in
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
                if (litChunkCount >= MaxChunksLitPerFrame)
                {
                    break;
                }

                UpdateChunk(projectedCells, projectedWaterCells, voxelLight,
                    chunk.ValueRO.Coordinate, blockAccessor, light);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsRender>(entity, true);

                litChunkCount++;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            chunkEntities.Dispose();
        }

        private void MarkAllChunksForLighting(ref SystemState state)
        {
            foreach (EnabledRefRW<ChunkNeedsLighting> needsLighting in
                     SystemAPI.Query<EnabledRefRW<ChunkNeedsLighting>>()
                         .WithAll<ChunkGenerated>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                needsLighting.ValueRW = true;
            }
        }

        private static void UpdateChunk(
            DynamicBuffer<ProjectedCellData> projectedCells,
            DynamicBuffer<ProjectedWaterCellData> projectedWaterCells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light)
        {
            UpdateOpaqueCells(
                projectedCells,
                voxelLight,
                chunkCoordinate,
                blockAccessor,
                light);

            UpdateWaterCells(
                projectedWaterCells,
                voxelLight,
                chunkCoordinate,
                blockAccessor,
                light);
        }

        private ViewDirection GetProjectionDirection(ref SystemState state)
        {
            ViewDirection direction =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            if (SystemAPI.TryGetSingleton<ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent transition) &&
                transition.IsActive)
            {
                direction = transition.TargetDirection;
            }

            return direction;
        }

        private static void UpdateOpaqueCells(
            DynamicBuffer<ProjectedCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedCellData cell = cells[i];

                cell.LightData = CalculateLightData(
                    cell,
                    voxelLight,
                    chunkCoordinate,
                    blockAccessor,
                    light);

                cells[i] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedWaterCellData waterCell = cells[i];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = CalculateLightData(
                    cell,
                    voxelLight,
                    chunkCoordinate,
                    blockAccessor,
                    light);

                waterCell.Value = cell;
                cells[i] = waterCell;
            }
        }

       private static uint CalculateLightData(
            ProjectedCellData cell,
            DynamicBuffer<VoxelLightData> voxelLight,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light)
        {
            bool occluded = false;

            if (light.Intensity > 0f)
            {
                int3 sourceAirPosition =
                    ChunkUtility.ToLocalPosition(cell.SourceAirIndex);

                occluded = VoxelShadowUtility.IsOccluded(
                    chunkCoordinate,
                    sourceAirPosition,
                    blockAccessor,
                    light.DirectionToLight,
                    MaximumShadowDistance);
            }

            VoxelLightData voxel = cell.SourceAirIndex < voxelLight.Length
                ? voxelLight[cell.SourceAirIndex]
                : new VoxelLightData(MaximumSkyLight);

            float3 localLight = new float3(
                voxel.R,
                voxel.G,
                voxel.B) / 255f;

            return LightDataUtility.Pack(
                localLight,
                voxel.Sky,
                !occluded);
        }
    }
}
