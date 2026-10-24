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
        private static readonly Unity.Profiling.ProfilerMarker BuildMarker = new("ChunkProjection.Build");
        private static readonly Unity.Profiling.ProfilerMarker MovingColumnsMarker = new("ChunkProjection.MovingClipColumns");
        private struct ProjectionSnapshot
        {
            public float3 Player;
            public ViewDirection Direction;
            public bool Clipping;
            public uint Geometry;
        }
        private NativeParallelHashMap<Entity, ProjectionSnapshot> projectionSnapshots;
        private const int
            MaxChunksProjectedPerFrame = 1;

        private EntityQuery
            generatedChunksQuery;

        private EntityQuery
            chunksNeedingProjectionQuery;


        public void OnCreate(
            ref SystemState state)
        {
            projectionSnapshots = new NativeParallelHashMap<Entity, ProjectionSnapshot>(128, Allocator.Persistent);
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


        public void OnDestroy(ref SystemState state)
        {
            if (projectionSnapshots.IsCreated) projectionSnapshots.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            // Немає роботи —
            if (!WorldRenderDebug.ProjectionRequired) return;
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
            uint geometrySignature = (uint)generatedChunkCount;


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
                uint revision = state.EntityManager.HasComponent<ChunkGeometryRevision>(entity)
                    ? state.EntityManager.GetComponentData<ChunkGeometryRevision>(entity).Value : 0;
                geometrySignature = unchecked(geometrySignature + math.hash(new uint4((uint)entity.Index,
                    (uint)entity.Version, revision, math.hash(chunk.ValueRO.Coordinate))));
            }
            // Discard departed owners; never retain a growing history of streamed chunks.
            using (var oldOwners = projectionSnapshots.GetKeyArray(Allocator.Temp))
                foreach (var owner in oldOwners)
                    if (!state.EntityManager.Exists(owner) || !state.EntityManager.HasComponent<ChunkGenerated>(owner))
                        projectionSnapshots.Remove(owner);
            if (projectionSnapshots.Capacity < generatedChunkCount)
                projectionSnapshots.Capacity = math.ceilpow2(generatedChunkCount);


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
                    blockLookup, SystemAPI.GetBufferLookup<SplitCellData>(true), SystemAPI.GetBufferLookup<ChunkSurfaceSample>(true));


            // ============================================================
            // Temporary projection data
            // ============================================================
            var occupancy = new ProjectionOccupancy(ChunkSettings.BlockCount, Allocator.Temp);
            var waterOccupancy = new ProjectionOccupancy(ChunkSettings.BlockCount, Allocator.Temp);



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


                DynamicBuffer<ProjectedClipCell> clippedCells =
                    state.EntityManager.GetBuffer<ProjectedClipCell>(entity);
                bool projectionClippingEnabled = projectionClippingLookup.HasComponent(entity) &&
                    projectionClippingLookup.IsComponentEnabled(entity);
                bool hasPrevious = projectionSnapshots.TryGetValue(entity, out var previous);
                bool incremental = !WorldRenderDebug.OverlayRequested && hasPrevious &&
                    previous.Geometry == geometrySignature && previous.Direction == projectionDirection &&
                    ChunkProjectionBuilder.CanReuseProjectionColumns(previous.Player, previous.Clipping, playerPosition, projectionClippingEnabled);
                int2 columns = incremental ? ChunkProjectionBuilder.MovingClipColumns(chunk.ValueRO.Coordinate,
                    projectionDirection, previous.Player, previous.Clipping, playerPosition, projectionClippingEnabled) : new int2(0, 16);
                if (incremental)
                {
                    int end = columns.x + columns.y;
                    int kept = 0;
                    var mutableSolid = projectedCells;
                    var mutableWater = projectedWaterCells;
                    for (int i = 0; i < projectedCells.Length; i++)
                    {
                        var cell = projectedCells[i];
                        if (cell.ProjectionX < columns.x || cell.ProjectionX >= end) mutableSolid[kept++] = cell;
                    }
                    projectedCells.ResizeUninitialized(kept);
                    kept = 0;
                    for (int i = 0; i < projectedWaterCells.Length; i++)
                    {
                        var cell = projectedWaterCells[i];
                        if (cell.Value.ProjectionX < columns.x || cell.Value.ProjectionX >= end) mutableWater[kept++] = cell;
                    }
                    projectedWaterCells.ResizeUninitialized(kept);
                    kept = 0;
                    for (int i = 0; i < clippedCells.Length; i++)
                    {
                        var cell = clippedCells[i];
                        if (cell.X < columns.x || cell.X >= end) clippedCells[kept++] = cell;
                    }
                    clippedCells.ResizeUninitialized(kept);
                }
                else { projectedCells.Clear(); projectedWaterCells.Clear(); clippedCells.Clear(); }
                occupancy.Clear();
                waterOccupancy.Clear();

                ProjectionWriter writer = new ProjectionWriter(
                    occupancy,
                    projectedCells,
                    clippedCells);

                WaterProjectionWriter waterWriter =
                    new WaterProjectionWriter(
                        occupancy,
                        waterOccupancy,
                        projectedWaterCells);

                using (incremental ? MovingColumnsMarker.Auto() : BuildMarker.Auto())
                ChunkProjectionBuilder.BuildColumns(
                    blocks,
                    chunk.ValueRO.Coordinate,
                    blockAccessor,
                    writer,
                    waterWriter,
                    projectionDirection,
                    projectionClippingEnabled,
                    playerPosition, columns.x, columns.y);
                WorldRenderDebug.CountBaseProjectionBuild();
                projectionSnapshots[entity] = new ProjectionSnapshot { Player = playerPosition,
                    Direction = projectionDirection, Clipping = projectionClippingEnabled, Geometry = geometrySignature };

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
