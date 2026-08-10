using Game.World.Blocks;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [BurstCompile]
    [UpdateInGroup(
        typeof(SimulationSystemGroup))]
    [UpdateBefore(
        typeof(ChunkProjectionSystem))]
    public partial struct ChunkGenerationSystem :
        ISystem
    {
        private const byte MaxDurability =
            byte.MaxValue;

        private const int
            MaxChunksGeneratedPerFrame = 2;


        public void OnCreate(
            ref SystemState state)
        {
            state.RequireForUpdate<
                ChunkComponent>();
        }


        [BurstCompile]
        public void OnUpdate(
            ref SystemState state)
        {
            int chunkCount =
                SystemAPI.QueryBuilder()
                    .WithAll<ChunkComponent>()
                    .Build()
                    .CalculateEntityCount();


            if (chunkCount == 0)
            {
                return;
            }


            // ============================================================
            // Map of ALL created chunks.
            //
            // Тут навмисно не вимагаємо ChunkGenerated.
            //
            // Ми можемо виставити NeedsProjection навіть
            // ще не згенерованому чанку.
            //
            // ChunkProjectionSystem все одно не прочитає
            // його до появи ChunkGenerated.
            // ============================================================

            var loadedChunks =
                new NativeParallelHashMap<
                    int2,
                    Entity>(
                    math.max(
                        chunkCount,
                        1),
                    Allocator.Temp);


            foreach (var (
                         chunk,
                         entity)
                     in SystemAPI.Query<
                             RefRO<ChunkComponent>>()
                         .WithEntityAccess())
            {
                loadedChunks.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }


            var ecb =
                new EntityCommandBuffer(
                    Allocator.Temp);


            int generatedCount = 0;


            // ============================================================
            // Generate limited amount
            // ============================================================

            foreach (var (
                         chunk,
                         blocks,
                         entity)
                     in SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<BlockData>>()
                         .WithNone<ChunkGenerated>()
                         .WithEntityAccess())
            {
                if (generatedCount >=
                    MaxChunksGeneratedPerFrame)
                {
                    break;
                }


                int2 coordinate =
                    chunk.ValueRO.Coordinate;


                // --------------------------------------------------------
                // Fill BlockData
                // --------------------------------------------------------

                GenerateChunk(
                    blocks,
                    coordinate);


                // --------------------------------------------------------
                // Chunk becomes valid
                // --------------------------------------------------------

                ecb.AddComponent<
                    ChunkGenerated>(
                    entity);


                // --------------------------------------------------------
                // Own projection
                // --------------------------------------------------------

                ecb.SetComponentEnabled<
                    ChunkNeedsProjection>(
                    entity,
                    true);


                // --------------------------------------------------------
                // IMPORTANT
                //
                // Invalidate all existing neighbour entities.
                //
                // Не перевіряємо ChunkGenerated.
                //
                // Якщо neighbour ще не generated,
                // dirty flag просто почекає.
                //
                // Якщо neighbour уже generated,
                // його projection буде перестворено.
                // --------------------------------------------------------

                MarkAdjacentChunksForProjection(
                    ecb,
                    loadedChunks,
                    coordinate);


                generatedCount++;
            }


            ecb.Playback(
                state.EntityManager);

            ecb.Dispose();

            loadedChunks.Dispose();
        }


        // ================================================================
        // Neighbour invalidation
        // ================================================================

        private static void
            MarkAdjacentChunksForProjection(
                EntityCommandBuffer ecb,
                NativeParallelHashMap<
                    int2,
                    Entity> loadedChunks,
                int2 centerCoordinate)
        {
            MarkChunkForProjection(
                ecb,
                loadedChunks,
                centerCoordinate +
                new int2(
                    1,
                    0));


            MarkChunkForProjection(
                ecb,
                loadedChunks,
                centerCoordinate +
                new int2(
                    -1,
                    0));


            MarkChunkForProjection(
                ecb,
                loadedChunks,
                centerCoordinate +
                new int2(
                    0,
                    1));


            MarkChunkForProjection(
                ecb,
                loadedChunks,
                centerCoordinate +
                new int2(
                    0,
                    -1));
        }


        private static void
            MarkChunkForProjection(
                EntityCommandBuffer ecb,
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


            // Не перевіряємо ChunkGenerated.
            //
            // ChunkNeedsProjection є в archetype
            // кожного chunk entity з моменту створення.

            ecb.SetComponentEnabled<
                ChunkNeedsProjection>(
                entity,
                true);
        }


        // ================================================================
        // Terrain generation
        // ================================================================

        private static void GenerateChunk(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate)
        {
            BlockData air =
                new(
                    BlockId.Air,
                    0);


            BlockData grass =
                new(
                    BlockId.Grass,
                    MaxDurability);


            BlockData dirt =
                new(
                    BlockId.Dirt,
                    MaxDurability);


            BlockData stone =
                new(
                    BlockId.Stone,
                    MaxDurability);


            for (int z = 0;
                 z < ChunkSettings.SizeZ;
                 z++)
            {
                for (int x = 0;
                     x < ChunkSettings.SizeX;
                     x++)
                {
                    int globalX =
                        chunkCoordinate.x *
                        ChunkSettings.SizeX +
                        x;


                    int globalZ =
                        chunkCoordinate.y *
                        ChunkSettings.SizeZ +
                        z;


                    int terrainHeight =
                        GetTerrainHeight(
                            globalX,
                            globalZ);


                    for (int y = 0;
                         y < ChunkSettings.SizeY;
                         y++)
                    {
                        BlockData block;


                        if (y >= terrainHeight)
                        {
                            block =
                                air;
                        }
                        else if (
                            y ==
                            terrainHeight - 1)
                        {
                            block =
                                grass;
                        }
                        else if (
                            y >=
                            terrainHeight - 4)
                        {
                            block =
                                dirt;
                        }
                        else
                        {
                            block =
                                stone;
                        }


                        ChunkUtility.SetBlock(
                            blocks,
                            x,
                            y,
                            z,
                            block);
                    }
                }
            }
        }


        // ================================================================
        // Terrain height
        // ================================================================

        private static int GetTerrainHeight(
            int globalX,
            int globalZ)
        {
            const float baseHeight =
                36f;


            const float xFrequency =
                0.08f;

            const float zFrequency =
                0.06f;


            const float xAmplitude =
                14f;

            const float zAmplitude =
                10f;


            float xWave =
                math.sin(
                    globalX *
                    xFrequency) *
                xAmplitude;


            float zWave =
                math.cos(
                    globalZ *
                    zFrequency) *
                zAmplitude;


            float diagonalWave =
                math.sin(
                    (globalX +
                     globalZ) *
                    0.035f) *
                6f;


            float height =
                baseHeight +
                xWave +
                zWave +
                diagonalWave;


            return math.clamp(
                (int)math.round(
                    height),
                1,
                ChunkSettings.SizeY - 1);
        }
    }
}