using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Game.World.Rendering
{
    [BurstCompile]
    public partial struct ChunkProjectionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkNeedsProjection>();
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

            foreach (var (blocks, projectedCells, entity) in
                     SystemAPI.Query<
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

                ViewDirection direction =
                    SystemAPI
                        .GetSingleton<ViewDirectionComponent>()
                        .Value;

                ChunkProjectionBuilder.Build(
                    blocks,
                    writer,
                    direction);

                ecb.SetComponentEnabled<ChunkNeedsProjection>(
                    entity,
                    false);

                ecb.SetComponentEnabled<ChunkNeedsRender>(
                    entity,
                    true);
            }

            occupancy.Dispose();

            ecb.Playback(
                state.EntityManager);

            ecb.Dispose();
        }
    }
}