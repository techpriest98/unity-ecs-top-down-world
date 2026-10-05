using Game.World.Blocks;
using Game.World.Chunks;
using Game.World.Lighting;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    [BurstCompile]
    [UpdateAfter(typeof(ViewDirectionInputSystem))]
    public partial struct ChunkProjectionSystem : ISystem
    {
        private const int MaxChunksProjectedPerFrame = 1;

        private EntityQuery generatedChunksQuery;
        private EntityQuery chunksNeedingProjectionQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkComponent>();
            state.RequireForUpdate<ViewDirectionComponent>();

            generatedChunksQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<ChunkComponent, ChunkGenerated>()
                    .Build(ref state);

            chunksNeedingProjectionQuery =
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<
                        ChunkComponent,
                        ChunkGenerated,
                        ChunkNeedsProjection>()
                    .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (chunksNeedingProjectionQuery.CalculateEntityCount() == 0)
                return;

            ViewDirection projectionDirection =
                SystemAPI.GetSingleton<ViewDirectionComponent>().Value;

            if (SystemAPI.TryGetSingleton(
                    out ViewDirectionTransitionComponent transition) &&
                transition.IsActive)
            {
                projectionDirection = transition.TargetDirection;
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
                chunkEntities.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>(
                    isReadOnly: true);

            var blockAccessor = new ChunkBlockAccessor(
                chunkEntities,
                blockLookup);

            var occupancy = new ProjectionOccupancy(
                ChunkSettings.BlockCount,
                Allocator.Temp);

            var waterOccupancy = new ProjectionOccupancy(
                ChunkSettings.BlockCount,
                Allocator.Temp);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            int projectedCount = 0;

            foreach (var (
                    chunk,
                    blocks,
                    projectedCells,
                    projectedWaterCells,
                    entity) in
                SystemAPI.Query<
                        RefRO<ChunkComponent>,
                        DynamicBuffer<BlockData>,
                        DynamicBuffer<ProjectedCellData>,
                        DynamicBuffer<ProjectedWaterCellData>>()
                    .WithAll<ChunkGenerated, ChunkNeedsProjection>()
                    .WithEntityAccess())
            {
                if (projectedCount >= MaxChunksProjectedPerFrame)
                    break;

                projectedCells.Clear();
                projectedWaterCells.Clear();

                occupancy.Clear();
                waterOccupancy.Clear();

                var writer = new ProjectionWriter(
                    occupancy,
                    projectedCells);

                var waterWriter = new WaterProjectionWriter(
                    occupancy,
                    waterOccupancy,
                    projectedWaterCells);

                ChunkProjectionBuilder.Build(
                    blocks,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    writer,
                    waterWriter,
                    projectionDirection);

                ecb.SetComponentEnabled<ChunkNeedsProjection>(
                    entity,
                    false);

                ecb.SetComponentEnabled<ChunkNeedsLighting>(
                    entity,
                    true);

                projectedCount++;
            }

            ecb.Playback(state.EntityManager);

            ecb.Dispose();
            occupancy.Dispose();
            waterOccupancy.Dispose();
            chunkEntities.Dispose();
        }
    }
}