using Game.World.Chunks;
using Game.World.Rendering;
using Game.World.Time;
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
        private const float MaximumSkyLight = 15f;
        private const float MinimumAmbientVisibility = 0.08f;

        private EntityQuery generatedChunksQuery;
        private EntityQuery chunksNeedingLightingQuery;

        private int lastHour;
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

            state.RequireForUpdate<WorldTime>();
            state.RequireForUpdate<DirectionalLightData>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int hour = SystemAPI.GetSingleton<WorldTime>().Hour;

            if (!initialized || hour != lastHour)
            {
                MarkAllChunksForLighting(ref state);
                lastHour = hour;
                initialized = true;
            }

            if (chunksNeedingLightingQuery.CalculateEntityCount() == 0)
            {
                return;
            }

            DirectionalLightData light = SystemAPI.GetSingleton<DirectionalLightData>();

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
            int litChunkCount = 0;

            foreach (var (chunk, voxelLight, projectedCells, projectedWaterCells, entity) in
                SystemAPI.Query<
                        RefRO<ChunkComponent>,
                        DynamicBuffer<VoxelLightData>,
                        DynamicBuffer<ProjectedCellData>,
                        DynamicBuffer<ProjectedWaterCellData>>()
                    .WithAll<ChunkGenerated, ChunkNeedsLighting>()
                    .WithDisabled<ChunkNeedsSkyLight>()
                    .WithEntityAccess())
            {
                if (litChunkCount >= MaxChunksLitPerFrame)
                {
                    break;
                }

                UpdateOpaqueCells(
                    projectedCells,
                    voxelLight,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    light);

                UpdateWaterCells(
                    projectedWaterCells,
                    voxelLight,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    light);

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
                : new VoxelLightData((byte)MaximumSkyLight);


            float skyLevel =voxel.Sky / (float)MaximumSkyLight;

            float ambientVisibility = math.lerp(
                MinimumAmbientVisibility,
                1f,
                skyLevel);

            float3 localLight = new float3(
                voxel.R,
                voxel.G,
                voxel.B) / 255f;

            float3 indirectLight =
                light.AmbientColor *
                ambientVisibility +
                localLight;

            float sunVisibility =
                occluded ? 0f : 1f;

            return LightDataUtility.Pack(
                indirectLight,
                sunVisibility);
        }
    }
}