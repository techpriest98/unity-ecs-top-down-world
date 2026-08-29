using Game.Player;
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
    [UpdateAfter(
        typeof(ViewDirectionInputSystem))]
    public partial struct ChunkProjectionSystem :
        ISystem
    {
        private const int
            MaxChunksProjectedPerFrame = 1;

        private EntityQuery
            generatedChunksQuery;

        private EntityQuery
            chunksNeedingProjectionQuery;


        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                ChunkComponent>();

            state.RequireForUpdate<
                ViewDirectionComponent>();

            state.RequireForUpdate<
                PlayerWorldPosition>();

            generatedChunksQuery =
                new EntityQueryBuilder(
                        Allocator.Temp)
                    .WithAll<
                        ChunkComponent,
                        ChunkGenerated>()
                    .Build(
                        ref state);
                        
            chunksNeedingProjectionQuery =
                new EntityQueryBuilder(
                        Allocator.Temp)
                    .WithAll<
                        ChunkComponent,
                        ChunkGenerated,
                        ChunkNeedsProjection>()
                    .Build(
                        ref state);
        }


        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            // Немає роботи —
            // не створюємо навіть тимчасові
            // Native containers.
            if (chunksNeedingProjectionQuery
                    .CalculateEntityCount() == 0)
            {
                return;
            }


            // ============================================================
            // Direction
            // ============================================================

            ViewDirection activeDirection =
                SystemAPI
                    .GetSingleton<
                        ViewDirectionComponent>()
                    .Value;


            ViewDirection projectionDirection =
                activeDirection;

            float3 playerPosition = SystemAPI.GetSingleton<PlayerWorldPosition>().Value;
            ComponentLookup<ChunkProjectionClippingEnabled>
            projectionClippingLookup = SystemAPI.GetComponentLookup<ChunkProjectionClippingEnabled>(
                isReadOnly: true);


            bool transitionActive =
                false;


            if (SystemAPI.TryGetSingleton<
                    ViewDirectionTransitionComponent>(
                    out ViewDirectionTransitionComponent
                        transition))
            {
                transitionActive =
                    transition.IsActive;


                if (transitionActive)
                {
                    projectionDirection = transition.TargetDirection;
                }
            }


            // ============================================================
            // Generated chunk lookup
            // ============================================================

            int generatedChunkCount =
                generatedChunksQuery
                    .CalculateEntityCount();


            var chunkEntities =
                new NativeParallelHashMap<
                    int2,
                    Entity>(
                    math.max(
                        generatedChunkCount,
                        1),
                    Allocator.Temp);


            foreach (var (
                         chunk,
                         entity)
                     in SystemAPI.Query<
                             RefRO<ChunkComponent>>()
                         .WithAll<
                             ChunkGenerated>()
                         .WithEntityAccess())
            {
                chunkEntities.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }


            // ============================================================
            // Block accessor
            // ============================================================

            BufferLookup<BlockData>
                blockLookup =
                    SystemAPI
                        .GetBufferLookup<
                            BlockData>(
                            isReadOnly: true);


            var blockAccessor =
                new ChunkBlockAccessor(
                    chunkEntities,
                    blockLookup);


            // ============================================================
            // Temporary projection data
            // ============================================================

            ProjectionOccupancy occupancy = new ProjectionOccupancy(
                ChunkSettings.BlockCount,
                Allocator.Temp);

            ProjectionOccupancy waterOccupancy = new ProjectionOccupancy(
                ChunkSettings.BlockCount,
                Allocator.Temp);


            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);


            int projectedCount = 0;


            // ============================================================
            // Budgeted projection
            // ============================================================

            foreach (var (
                    chunk,
                    blocks,
                    projectedCells,
                    projectedWaterCells,
                    entity)
                in SystemAPI.Query<
                        RefRO<ChunkComponent>,
                        DynamicBuffer<BlockData>,
                        DynamicBuffer<ProjectedCellData>,
                        DynamicBuffer<ProjectedWaterCellData>>()
                    .WithAll<
                        ChunkGenerated,
                        ChunkNeedsProjection>()
                    .WithEntityAccess())
            {
                if (projectedCount >=
                    MaxChunksProjectedPerFrame)
                {
                    break;
                }


                projectedCells.Clear();
                projectedWaterCells.Clear();

                occupancy.Clear();
                waterOccupancy.Clear();

                ProjectionWriter writer = new ProjectionWriter(
                    occupancy,
                    projectedCells);

                WaterProjectionWriter waterWriter =
                    new WaterProjectionWriter(
                        occupancy,
                        waterOccupancy,
                        projectedWaterCells);

                bool projectionClippingEnabled =
                    projectionClippingLookup.HasComponent(entity) &&
                    projectionClippingLookup.IsComponentEnabled(entity);

                ChunkProjectionBuilder.Build(
                    blocks,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    writer,
                    waterWriter,
                    projectionDirection,
                    projectionClippingEnabled,
                    playerPosition);

                ecb.SetComponentEnabled<ChunkNeedsProjection>(entity, false);
                ecb.SetComponentEnabled<ChunkNeedsLighting>(entity, true);

                projectedCount++;
            }


            ecb.Playback(
                state.EntityManager);


            ecb.Dispose();

            occupancy.Dispose();
            waterOccupancy.Dispose();

            chunkEntities.Dispose();
        }
    }
}