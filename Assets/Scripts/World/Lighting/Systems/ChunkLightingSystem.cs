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
            ViewDirection direction = GetProjectionDirection(ref state);

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

            foreach (var (chunk, projectedCells, projectedWaterCells, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<ProjectedCellData>,
                             DynamicBuffer<ProjectedWaterCellData>>()
                         .WithAll<ChunkGenerated, ChunkNeedsLighting>()
                         .WithEntityAccess())
            {
                if (litChunkCount >= MaxChunksLitPerFrame)
                {
                    break;
                }

                UpdateOpaqueCells(
                    projectedCells,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    light,
                    direction);

                UpdateWaterCells(
                    projectedWaterCells,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    light,
                    direction);

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
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light,
            ViewDirection direction)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedCellData cell = cells[i];

                cell.LightData = CalculateLightData(
                    cell,
                    chunkCoordinate,
                    blockAccessor,
                    light,
                    direction);

                cells[i] = cell;
            }
        }

        private static void UpdateWaterCells(
            DynamicBuffer<ProjectedWaterCellData> cells,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light,
            ViewDirection direction)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ProjectedWaterCellData waterCell = cells[i];
                ProjectedCellData cell = waterCell.Value;

                cell.LightData = CalculateLightData(
                    cell,
                    chunkCoordinate,
                    blockAccessor,
                    light,
                    direction);

                waterCell.Value = cell;
                cells[i] = waterCell;
            }
        }

        private static uint CalculateLightData(
            ProjectedCellData cell,
            int2 chunkCoordinate,
            ChunkBlockAccessor blockAccessor,
            DirectionalLightData light,
            ViewDirection direction)
        {
            ProjectedFaceType faceType =
                (ProjectedFaceType)((cell.BlockData >> 8) & 0xFFu);

            float3 normal = GetFaceNormal(faceType, direction);
            float directIntensity =
                math.max(math.dot(normal, light.DirectionToLight), 0f);

            bool occluded = false;

            if (light.Intensity > 0f && directIntensity > 0f)
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

            float shadowVisibility = occluded ? 0f : 1f;

            float3 finalLight =
                light.AmbientColor +
                light.Color *
                light.Intensity *
                directIntensity *
                shadowVisibility;

            return LightDataUtility.Pack(finalLight);
        }

        private static float3 GetFaceNormal(
            ProjectedFaceType faceType,
            ViewDirection direction)
        {
            if (faceType == ProjectedFaceType.Top)
            {
                return new float3(0f, 1f, 0f);
            }

            int2 towardCamera = ViewDirectionUtility.Forward(direction);

            return new float3(
                towardCamera.x,
                0f,
                towardCamera.y);
        }
    }
}