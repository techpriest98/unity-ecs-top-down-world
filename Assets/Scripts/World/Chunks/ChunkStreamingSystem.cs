using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [UpdateInGroup(
        typeof(InitializationSystemGroup))]
    [UpdateBefore(
        typeof(ChunkEntityGraphicsSetupSystem))]
    public partial struct ChunkStreamingSystem :
        ISystem
    {
        private EntityArchetype chunkArchetype;
        private EntityQuery chunkQuery;

        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                ChunkStreamingSettings>();

            state.RequireForUpdate<
                ChunkStreamingCenter>();

            chunkArchetype =
                state.EntityManager
                    .CreateArchetype(
                        typeof(ChunkComponent),
                        typeof(BlockData),
                        typeof(ProjectedCellData),
                        typeof(ChunkNeedsProjection),
                        typeof(ChunkNeedsRender));

            chunkQuery =
                state.EntityManager
                    .CreateEntityQuery(
                        ComponentType.ReadOnly<
                            ChunkComponent>());
        }

        public void OnUpdate(
            ref SystemState state)
        {
            ChunkStreamingSettings settings =
                SystemAPI.GetSingleton<
                    ChunkStreamingSettings>();

            ChunkStreamingCenter center =
                SystemAPI.GetSingleton<
                    ChunkStreamingCenter>();

            int loadRadius =
                math.max(
                    settings.LoadRadius,
                    0);

            int diameter =
                loadRadius * 2 + 1;

            int desiredChunkCount =
                diameter * diameter;

            int existingChunkCount =
                chunkQuery.CalculateEntityCount();

            int mapCapacity =
                math.max(
                    existingChunkCount +
                    desiredChunkCount,
                    1);

            var loadedChunks =
                new NativeParallelHashMap<
                    int2,
                    Entity>(
                    mapCapacity,
                    Allocator.Temp);

            var chunksToDestroy =
                new NativeList<Entity>(
                    Allocator.Temp);

            var removedCoordinates =
                new NativeList<int2>(
                    Allocator.Temp);

            CollectLoadedChunks(
                ref state,
                center.Coordinate,
                loadRadius,
                ref loadedChunks,
                ref chunksToDestroy,
                ref removedCoordinates);

            DestroyDistantChunks(
                ref state,
                chunksToDestroy);

            MarkNeighboursAfterRemoval(
                ref state,
                loadedChunks,
                removedCoordinates);

            CreateMissingChunks(
                ref state,
                center.Coordinate,
                loadRadius,
                ref loadedChunks);

            removedCoordinates.Dispose();
            chunksToDestroy.Dispose();
            loadedChunks.Dispose();
        }

        private void CollectLoadedChunks(
            ref SystemState state,
            int2 centerCoordinate,
            int loadRadius,
            ref NativeParallelHashMap<
                int2,
                Entity> loadedChunks,
            ref NativeList<Entity> chunksToDestroy,
            ref NativeList<int2> removedCoordinates)
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

                bool insideLoadArea =
                    difference.x <= loadRadius &&
                    difference.y <= loadRadius;

                if (insideLoadArea)
                {
                    loadedChunks.TryAdd(
                        coordinate,
                        entity);
                }
                else
                {
                    chunksToDestroy.Add(
                        entity);

                    removedCoordinates.Add(
                        coordinate);
                }
            }
        }

        private static void DestroyDistantChunks(
            ref SystemState state,
            NativeList<Entity> chunksToDestroy)
        {
            for (int index = 0;
                 index < chunksToDestroy.Length;
                 index++)
            {
                state.EntityManager.DestroyEntity(
                    chunksToDestroy[index]);
            }
        }

        private static void MarkNeighboursAfterRemoval(
            ref SystemState state,
            NativeParallelHashMap<
                int2,
                Entity> loadedChunks,
            NativeList<int2> removedCoordinates)
        {
            for (int index = 0;
                 index < removedCoordinates.Length;
                 index++)
            {
                MarkAdjacentChunksForProjection(
                    ref state,
                    loadedChunks,
                    removedCoordinates[index]);
            }
        }

        private void CreateMissingChunks(
            ref SystemState state,
            int2 centerCoordinate,
            int loadRadius,
            ref NativeParallelHashMap<
                int2,
                Entity> loadedChunks)
        {
            for (int chunkZ = -loadRadius;
                 chunkZ <= loadRadius;
                 chunkZ++)
            {
                for (int chunkX = -loadRadius;
                     chunkX <= loadRadius;
                     chunkX++)
                {
                    int2 coordinate =
                        centerCoordinate +
                        new int2(
                            chunkX,
                            chunkZ);

                    if (loadedChunks.ContainsKey(
                            coordinate))
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

                    // Уже згенеровані сусіди повинні
                    // перестворити проєкцію, оскільки
                    // поруч з'явився новий чанк.
                    MarkAdjacentChunksForProjection(
                        ref state,
                        loadedChunks,
                        coordinate);
                }
            }
        }

        private Entity CreateChunk(
            ref SystemState state,
            int2 coordinate)
        {
            Entity entity =
                state.EntityManager.CreateEntity(
                    chunkArchetype);

            state.EntityManager.SetComponentData(
                entity,
                new ChunkComponent
                {
                    Coordinate =
                        coordinate
                });

            DynamicBuffer<BlockData> blocks =
                state.EntityManager.GetBuffer<
                    BlockData>(
                    entity);

            blocks.ResizeUninitialized(
                ChunkSettings.BlockCount);

            state.EntityManager
                .SetComponentEnabled<
                    ChunkNeedsProjection>(
                    entity,
                    false);

            state.EntityManager
                .SetComponentEnabled<
                    ChunkNeedsRender>(
                    entity,
                    false);

            return entity;
        }

        private static void
            MarkAdjacentChunksForProjection(
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
                new int2(1, 0));

            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(-1, 0));

            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(0, 1));

            MarkChunkForProjection(
                ref state,
                loadedChunks,
                centerCoordinate +
                new int2(0, -1));
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

            // Незгенерований чанк не позначаємо:
            // ChunkGenerationSystem сама активує
            // ChunkNeedsProjection після генерації.
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
    }
}