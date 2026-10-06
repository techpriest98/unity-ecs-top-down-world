using Game.World.Blocks;
using Game.World.Rendering;
using Game.World.Lighting;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [UpdateInGroup(
        typeof(InitializationSystemGroup))]
    public partial struct ChunkStreamingSystem :
        ISystem
    {
        private const int
            MaxChunksCreatedPerFrame = 2;

        private const int
            MaxChunksDestroyedPerFrame = 4;

        private const int
            PreloadMargin = 1;

        private const int
            UnloadMargin = 2;


        private EntityArchetype
            chunkArchetype;

        private EntityQuery
            chunkQuery;


        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkStreamingRuntime>();
            state.RequireForUpdate<ChunkStreamingCenter>();

            chunkArchetype = state.EntityManager.CreateArchetype(
                typeof(ChunkComponent),
                typeof(BlockData),
                typeof(ChunkColumnData),
                typeof(ProjectedCellData),
                typeof(ProjectedWaterCellData),
                typeof(ChunkShaderClippingEnabled),
                typeof(ChunkProjectionClippingEnabled),
                typeof(VoxelLightData),
                typeof(ChunkNeedsProjection),
                typeof(ChunkNeedsRender),
                typeof(ChunkNeedsLighting),
                typeof(ChunkNeedsLocalLightUpdate),
                typeof(ChunkNeedsSkyLight),
                typeof(ChunkNeedsImmediateLighting),
                typeof(ChunkNeedsSunShadowUpdate));


            chunkQuery = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkComponent>());
        }


        public void OnUpdate(ref SystemState state)
        {
            ChunkStreamingSettings settings = SystemAPI.GetSingleton<ChunkStreamingSettings>();
            ChunkStreamingCenter center = SystemAPI.GetSingleton<ChunkStreamingCenter>();

            int loadRadius = SystemAPI.GetSingleton<ChunkStreamingRuntime>().LoadRadius;
            int preloadRadius = loadRadius + PreloadMargin;
            int unloadRadius = loadRadius + UnloadMargin;
            int preloadDiameter = preloadRadius * 2 + 1;

            int desiredChunkCount = preloadDiameter * preloadDiameter;
            int existingChunkCount = chunkQuery.CalculateEntityCount();
            int mapCapacity = math.max(existingChunkCount + desiredChunkCount, 1);

            var loadedChunks = new NativeParallelHashMap<int2, Entity>(mapCapacity, Allocator.Temp);
            var chunksToDestroy = new NativeList<ChunkRemovalCandidate>(Allocator.Temp);
            var removedCoordinates = new NativeList<int2>(Allocator.Temp);

            CollectLoadedChunks(
                ref state,
                center.Coordinate,
                unloadRadius,
                ref loadedChunks,
                ref chunksToDestroy);

            DestroyDistantChunks(
                ref state,
                chunksToDestroy,
                ref removedCoordinates);

            MarkNeighboursAfterRemoval(
                ref state,
                loadedChunks,
                removedCoordinates);

            CreateMissingChunks(
                ref state,
                center.Coordinate,
                preloadRadius,
                ref loadedChunks);


            removedCoordinates.Dispose();
            chunksToDestroy.Dispose();
            loadedChunks.Dispose();
        }


        // ================================================================
        // Collect existing chunks
        // ================================================================

        private void CollectLoadedChunks(
            ref SystemState state,
            int2 centerCoordinate,
            int unloadRadius,
            ref NativeParallelHashMap<
                int2,
                Entity> loadedChunks,
            ref NativeList<
                ChunkRemovalCandidate>
                chunksToDestroy)
        {
            foreach (var (
                    chunk,
                    entity)
                in SystemAPI
                    .Query<
                        RefRO<ChunkComponent>>()
                    .WithEntityAccess())
            {
                int2 coordinate =
                    chunk.ValueRO.Coordinate;


                int2 difference =
                    math.abs(
                        coordinate -
                        centerCoordinate);


                bool insideUnloadArea =
                    difference.x <=
                        unloadRadius &&
                    difference.y <=
                        unloadRadius;


                if (insideUnloadArea)
                {
                    loadedChunks.TryAdd(
                        coordinate,
                        entity);

                    continue;
                }


                chunksToDestroy.Add(
                    new ChunkRemovalCandidate
                    {
                        Entity = entity,

                        Coordinate =
                            coordinate
                    });
            }
        }


        // ================================================================
        // Destroy
        // ================================================================

        private static void DestroyDistantChunks(
            ref SystemState state,
            NativeList<
                ChunkRemovalCandidate>
                chunksToDestroy,
            ref NativeList<int2>
                removedCoordinates)
        {
            int destroyCount =
                math.min(
                    chunksToDestroy.Length,
                    MaxChunksDestroyedPerFrame);

            for (int index = 0;
                 index < destroyCount;
                 index++)
            {
                ChunkRemovalCandidate candidate =
                    chunksToDestroy[index];

                state.EntityManager
                    .DestroyEntity(
                        candidate.Entity);

                removedCoordinates.Add(
                    candidate.Coordinate);
            }
        }


        // ================================================================
        // Removal invalidation
        // ================================================================

        private static void
            MarkNeighboursAfterRemoval(
                ref SystemState state,
                NativeParallelHashMap<
                    int2,
                    Entity> loadedChunks,
                NativeList<int2>
                    removedCoordinates)
        {
            for (int index = 0;
                 index <
                 removedCoordinates.Length;
                 index++)
            {
                MarkAdjacentChunksForProjection(
                    ref state,
                    loadedChunks,
                    removedCoordinates[index]);
            }
        }


        // ================================================================
        // Creation
        // ================================================================

        private void CreateMissingChunks(
            ref SystemState state,
            int2 centerCoordinate,
            int preloadRadius,
            ref NativeParallelHashMap<
                int2,
                Entity> loadedChunks)
        {
            int createdCount = 0;

            for (int distance = 0;
                 distance <= preloadRadius;
                 distance++)
            {
                for (int chunkZ = -distance;
                     chunkZ <= distance;
                     chunkZ++)
                {
                    for (int chunkX = -distance;
                         chunkX <= distance;
                         chunkX++)
                    {
                        if (createdCount >=
                            MaxChunksCreatedPerFrame)
                        {
                            return;
                        }


                        int ringDistance =
                            math.max(
                                math.abs(
                                    chunkX),
                                math.abs(
                                    chunkZ));

                        if (ringDistance != distance)
                        {
                            continue;
                        }


                        int2 coordinate =
                            centerCoordinate +
                            new int2(
                                chunkX,
                                chunkZ);


                        if (loadedChunks.ContainsKey(coordinate))
                        {
                            continue;
                        }

                        Entity chunkEntity =
                            CreateChunk(
                                ref state,
                                coordinate);

                        loadedChunks.TryAdd(
                            coordinate,
                            chunkEntity);

                        createdCount++;
                    }
                }
            }
        }


        // ================================================================
        // Create one chunk
        // ================================================================

        private Entity CreateChunk(ref SystemState state, int2 coordinate)
        {
            Entity entity = state.EntityManager.CreateEntity(chunkArchetype);

            state.EntityManager.SetComponentData(entity, new ChunkComponent
            {
                Coordinate = coordinate
            });

            DynamicBuffer<BlockData> blocks =
                state.EntityManager.GetBuffer<BlockData>(entity);

            blocks.ResizeUninitialized(ChunkSettings.BlockCount);

            DynamicBuffer<VoxelLightData> voxelLight =
                state.EntityManager.GetBuffer<VoxelLightData>(entity);

            voxelLight.ResizeUninitialized(ChunkSettings.BlockCount);

            for (int index = 0; index < voxelLight.Length; index++)
            {
                voxelLight[index] = new VoxelLightData(0);
            }

            state.EntityManager.SetComponentEnabled<ChunkNeedsProjection>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkNeedsRender>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkShaderClippingEnabled>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkProjectionClippingEnabled>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkNeedsLighting>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkNeedsSkyLight>(entity, true);
            state.EntityManager.SetComponentEnabled<ChunkNeedsLocalLightUpdate>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkNeedsImmediateLighting>(entity, false);
            state.EntityManager.SetComponentEnabled<ChunkNeedsSunShadowUpdate>(entity, false);

            DynamicBuffer<ChunkColumnData> columns = state.EntityManager.GetBuffer<ChunkColumnData>(entity);
            columns.ResizeUninitialized(ChunkSettings.SizeX * ChunkSettings.SizeZ);
                        
            return entity;
        }


        // ================================================================
        // Projection invalidation
        // ================================================================

        private static void MarkAdjacentChunksForProjection(
            ref SystemState state,
            NativeParallelHashMap<
                int2,
                Entity> loadedChunks,
            int2 centerCoordinate)
        {
            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(
                    1,
                    0));


            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(
                    -1,
                    0));


            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(
                    0,
                    1));


            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(
                    0,
                    -1));
        }


        private static void MarkChunkForProjection(
            ref SystemState state,
            NativeParallelHashMap<
                int2,
                Entity> loadedChunks,
            int2 coordinate)
        {
            if (!loadedChunks.TryGetValue(
                    coordinate,
                    out Entity entity))
            {
                return;
            }


            if (!state.EntityManager
                    .HasComponent<
                        ChunkGenerated>(
                        entity))
            {
                return;
            }

            state.EntityManager
                .SetComponentEnabled<
                    ChunkNeedsProjection>(
                    entity,
                    true);
        }


        // ================================================================
        // Internal data
        // ================================================================

        private struct ChunkRemovalCandidate
        {
            public Entity Entity;
            public int2 Coordinate;
        }
    }
}