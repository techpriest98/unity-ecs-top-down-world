using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    [BurstCompile]
    public partial struct ChunkProjectionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkNeedsProjection>();
            state.RequireForUpdate<ViewDirectionComponent>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);

            var occupancy =
                new ProjectionOccupancy(
                    capacity: ChunkSettings.BlockCount,
                    allocator: Allocator.Temp);

            int chunkCount =
                SystemAPI.QueryBuilder()
                    .WithAll<ChunkComponent>()
                    .Build()
                    .CalculateEntityCount();

            var chunkEntities =
                new NativeParallelHashMap<int2, Entity>(
                    math.max(chunkCount, 1),
                    Allocator.Temp);

            foreach (var (chunk, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>>()
                         .WithEntityAccess())
            {
                chunkEntities.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }

            BufferLookup<BlockData> blockLookup =
                SystemAPI.GetBufferLookup<BlockData>(
                    isReadOnly: true);

            var blockAccessor =
                new ChunkBlockAccessor(
                    chunkEntities,
                    blockLookup);

            ViewDirection direction =
                SystemAPI
                    .GetSingleton<ViewDirectionComponent>()
                    .Value;

            foreach (var (
                        chunk,
                        blocks,
                        projectedCells,
                        entity)
                    in SystemAPI.Query<
                            RefRO<ChunkComponent>,
                            DynamicBuffer<BlockData>,
                            DynamicBuffer<ProjectedCellData>>()
                        .WithAll<ChunkNeedsProjection>()
                        .WithEntityAccess())
            {
                projectedCells.Clear();
                occupancy.Clear();

                var writer =
                    new ProjectionWriter(
                        occupancy,
                        projectedCells);

                ChunkProjectionBuilder.Build(
                    blocks,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    writer,
                    direction);

                ecb.SetComponentEnabled<ChunkNeedsProjection>(
                    entity,
                    false);

                ecb.SetComponentEnabled<ChunkNeedsRender>(
                    entity,
                    true);
            }

            chunkEntities.Dispose();
            occupancy.Dispose();

            ecb.Playback(
                state.EntityManager);

            ecb.Dispose();
        }
    }
}