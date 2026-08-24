using Game.World.Blocks;
using Game.World.Generation;
using Game.World.Generation.Biomes;
using Game.World.Generation.Biomes.RockyShore;
using Game.World.Generation.Terrain;
using Game.World.Generation.Spawning;
using Game.World.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkProjectionSystem))]
    public partial struct ChunkGenerationSystem : ISystem
    {
        private const int MaxChunksGeneratedPerFrame = 2;

        private WorldHeightMap worldHeightMap;
        private CoastDistanceMap coastDistanceMap;
        private LandmassMap landmassMap;
        private WorldAnchors worldAnchors;
        private WorldBiomeSamplingContext biomeSamplingContext;

        // Runtime (0,0) samples this macro-world position.
        private int2 macroSampleOrigin;

        private uint loadedSeed;
        private WorldGenerationSettingsComponent loadedWorldSettings;
        private bool hasLoadedWorld;

        private Entity spawnPointEntity;
        private bool hasResolvedSpawnPoint;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkComponent>();
            state.RequireForUpdate<WorldSeedComponent>();
            state.RequireForUpdate<WorldGenerationSettingsComponent>();
            state.RequireForUpdate<BiomeTerrainResolver>();
            state.RequireForUpdate<BlockDatabaseComponent>();

            worldHeightMap = default;
            coastDistanceMap = default;
            landmassMap = default;
            worldAnchors = default;
            biomeSamplingContext = default;

            macroSampleOrigin = int2.zero;

            loadedSeed = 0;
            loadedWorldSettings = default;
            hasLoadedWorld = false;

            spawnPointEntity = state.EntityManager.CreateEntity();
            hasResolvedSpawnPoint = false;

            state.EntityManager.AddComponentData(
                spawnPointEntity,
                new WorldSpawnPointComponent
                {
                    Position = default
                });

            state.EntityManager.SetComponentEnabled<
                WorldSpawnPointComponent>(
                spawnPointEntity,
                false);
        }

        public void OnDestroy(ref SystemState state)
        {
            DisposeWorldData();

            if (state.EntityManager.Exists(spawnPointEntity))
            {
                state.EntityManager.DestroyEntity(spawnPointEntity);
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            uint worldSeed = SystemAPI.GetSingleton<WorldSeedComponent>().Value;

            WorldGenerationSettingsComponent worldSettings =
                SystemAPI.GetSingleton<WorldGenerationSettingsComponent>();

            BiomeTerrainResolver biomeTerrainResolver =
                SystemAPI.GetSingleton<BiomeTerrainResolver>();

            BlockDatabaseComponent blockDatabase =
                SystemAPI.GetSingleton<BlockDatabaseComponent>();

            EnsureWorldData(ref state, worldSeed, worldSettings);
            int chunkCount = SystemAPI.QueryBuilder()
                .WithAll<ChunkComponent>()
                .Build()
                .CalculateEntityCount();

            if (chunkCount == 0)
                return;

            var loadedChunks = new NativeParallelHashMap<int2, Entity>(
                math.max(chunkCount, 1),
                Allocator.Temp);

            foreach (var (chunk, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>>().WithEntityAccess())
            {
                loadedChunks.TryAdd(
                    chunk.ValueRO.Coordinate,
                    entity);
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            int generatedCount = 0;

            bool searchSpawnPoint = !hasResolvedSpawnPoint;

            int2 spawnSearchOrigin =
                new int2(
                    ChunkSettings.SizeX / 2,
                    ChunkSettings.SizeZ / 2);

            foreach (var (chunk, blocks, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>, DynamicBuffer<BlockData>>()
                         .WithNone<ChunkGenerated>()
                         .WithEntityAccess())
            {
                if (generatedCount >= MaxChunksGeneratedPerFrame)
                    break;

                int2 coordinate = chunk.ValueRO.Coordinate;

                bool foundSpawnPoint =
                    GenerateChunk(
                        blocks,
                        coordinate,
                        worldHeightMap,
                        coastDistanceMap,
                        landmassMap,
                        biomeSamplingContext,
                        macroSampleOrigin,
                        worldSeed,
                        worldSettings,
                        biomeTerrainResolver,
                        blockDatabase,
                        searchSpawnPoint,
                        spawnSearchOrigin,
                        out int3 spawnPosition);

                if (foundSpawnPoint)
                {
                    hasResolvedSpawnPoint = true;

                    state.EntityManager.SetComponentData(
                        spawnPointEntity,
                        new WorldSpawnPointComponent
                        {
                            Position = spawnPosition
                        });

                    state.EntityManager.SetComponentEnabled<
                        WorldSpawnPointComponent>(
                        spawnPointEntity,
                        true);

                    searchSpawnPoint = false;
                }

                ecb.AddComponent<ChunkGenerated>(entity);
                ecb.SetComponentEnabled<ChunkNeedsProjection>(
                    entity,
                    true);

                MarkAdjacentChunksForProjection(
                    ecb,
                    loadedChunks,
                    coordinate);

                generatedCount++;
            }

            ecb.Playback(state.EntityManager);

            ecb.Dispose();
            loadedChunks.Dispose();
        }

        // ================================================================
        // World data
        // ================================================================

        private void EnsureWorldData(
            ref SystemState state,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            bool sameWorld =
                hasLoadedWorld &&
                loadedSeed == worldSeed &&
                worldHeightMap.IsCreated &&
                coastDistanceMap.IsCreated &&
                landmassMap.IsCreated;

            if (sameWorld)
                return;

            DisposeWorldData();

            state.EntityManager.SetComponentEnabled<
                WorldSpawnPointComponent>(
                spawnPointEntity,
                false);

            worldHeightMap = WorldHeightMap.Create(
                worldSettings.HeightMapResolution,
                worldSettings.MacroCellSize,
                worldSeed,
                worldSettings.DiamondSquareRoughness,
                Allocator.Persistent);

            coastDistanceMap = CoastDistanceMap.Create(
                worldHeightMap,
                worldSettings.MacroSeaLevel,
                Allocator.Persistent);

            landmassMap = LandmassMap.Create(
                worldHeightMap,
                worldSettings.MacroSeaLevel,
                Allocator.Persistent);

            worldAnchors = WorldAnchorGenerator.Generate(
                worldHeightMap,
                coastDistanceMap,
                landmassMap,
                worldSettings.MacroSeaLevel,
                worldSeed);

            biomeSamplingContext = WorldBiomeSampler.CreateContext(
                worldAnchors,
                worldSettings.MacroSeaLevel,
                worldSeed);

            int2 anchorPosition = WorldSamplingUtility.UvToWorld(
                worldAnchors.StartUv,
                worldHeightMap);

            macroSampleOrigin = anchorPosition - new int2(
                ChunkSettings.SizeX / 2,
                ChunkSettings.SizeZ / 2);

            loadedSeed = worldSeed;
            loadedWorldSettings = worldSettings;
            hasLoadedWorld = true;
        }

        private void DisposeWorldData()
        {
            if (landmassMap.IsCreated)
                landmassMap.Dispose();

            if (coastDistanceMap.IsCreated)
                coastDistanceMap.Dispose();

            if (worldHeightMap.IsCreated)
                worldHeightMap.Dispose();

            landmassMap = default;
            coastDistanceMap = default;
            worldHeightMap = default;
            worldAnchors = default;
            biomeSamplingContext = default;

            macroSampleOrigin = int2.zero;

            loadedSeed = 0;
            loadedWorldSettings = default;
            hasLoadedWorld = false;
        }

        // ================================================================
        // Chunk generation
        // ================================================================

        private static bool GenerateChunk(
            DynamicBuffer<BlockData> blocks,
            int2 chunkCoordinate,
            WorldHeightMap heightMap,
            CoastDistanceMap coastMap,
            LandmassMap landmassMap,
            WorldBiomeSamplingContext biomeContext,
            int2 macroOrigin,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings,
            in BiomeTerrainResolver biomeTerrainResolver,
            in BlockDatabaseComponent blockDatabase,
            bool searchSpawnPoint,
            int2 spawnSearchOrigin,
            out int3 spawnPosition)
        {
            spawnPosition = default;

            bool foundSpawnPoint = false;
            int bestSpawnDistanceSquared = int.MaxValue;
            int shoreSearchDistance = biomeTerrainResolver.GetRockyShoreSearchDistance();

            ChunkShoreDistanceMap shoreDistanceMap = default;

            for (int z = 0; z < ChunkSettings.SizeZ; z++)
            {
                for (int x = 0; x < ChunkSettings.SizeX; x++)
                {
                    int globalX =
                        chunkCoordinate.x *
                        ChunkSettings.SizeX +
                        x;

                    int globalZ =
                        chunkCoordinate.y *
                        ChunkSettings.SizeZ +
                        z;

                    int worldX = macroOrigin.x + globalX;
                    int worldZ = macroOrigin.y + globalZ;

                    float elevation = heightMap.Sample(
                        worldX,
                        worldZ);

                    // ====================================================
                    // Base terrain
                    // ====================================================

                    int baseHeight = WorldTerrainHeight.Sample(
                        worldX,
                        worldZ,
                        elevation,
                        heightMap,
                        worldSeed,
                        worldSettings);

                    bool baseHasWater =
                        baseHeight <
                        worldSettings.SeaLevelHeight;

                    // ====================================================
                    // Biome
                    // ====================================================

                    float coastDistance = coastMap.Sample(
                        worldX,
                        worldZ);

                    bool isMainland = WorldSamplingUtility.IsMainland(
                        worldX,
                        worldZ,
                        heightMap,
                        landmassMap);

                    int waterLevel = worldSettings.SeaLevelHeight - 1;
                    bool isWaterColumn =
                        baseHeight < waterLevel;

                    float2 uv = WorldSamplingUtility.WorldToUv(
                        worldX,
                        worldZ,
                        heightMap);

                    float biomeElevation = baseHasWater
                        ? elevation
                        : math.max(
                            elevation,
                            worldSettings.MacroSeaLevel);

                    WorldBiomeSample biomeSample = WorldBiomeSampler.Sample(
                        uv,
                        biomeElevation,
                        coastDistance,
                        isMainland,
                        isWaterColumn,
                        biomeContext);

                    WorldBiome biome = biomeSample.Biome;
                    float biomeInfluence = biomeSample.Influence;

                    // ====================================================
                    // Biome terrain shaping
                    // ====================================================

                    float shoreDistance = 0f;
                    if (biome == WorldBiome.RockyShore)
                    {
                        if (!shoreDistanceMap.IsCreated)
                        {
                            shoreDistanceMap =
                                ChunkShoreDistanceMap.Create(
                                    chunkCoordinate,
                                    macroOrigin,
                                    shoreSearchDistance,
                                    heightMap,
                                    worldSeed,
                                    worldSettings,
                                    Allocator.Temp);
                        }

                        shoreDistance = shoreDistanceMap.Get(x, z);
                    }

                    BiomeTerrainSample terrainSample =
                        biomeTerrainResolver.Sample(
                            biome,
                            worldX,
                            worldZ,
                            baseHeight,
                            shoreDistance,
                            biomeInfluence,
                            worldSeed,
                            worldSettings);

                    if (searchSpawnPoint &&
                        terrainSample.Biome ==
                        WorldBiome.RockyShore &&
                        terrainSample.Zone ==
                        (byte)RockyShoreZone.Beach)
                    {
                        int offsetX =
                            globalX -
                            spawnSearchOrigin.x;

                        int offsetZ =
                            globalZ -
                            spawnSearchOrigin.y;

                        int distanceSquared =
                            offsetX * offsetX +
                            offsetZ * offsetZ;

                        if (distanceSquared <
                            bestSpawnDistanceSquared)
                        {
                            bestSpawnDistanceSquared =
                                distanceSquared;

                            spawnPosition = new int3(
                                globalX,
                                terrainSample.Height,
                                globalZ);

                            foundSpawnPoint = true;
                        }
                    }

                    for (int y = 0; y < ChunkSettings.SizeY; y++)
                    {
                        BlockId blockId =
                            biomeTerrainResolver.GetBlock(
                                y,
                                waterLevel,
                                terrainSample);

                        BlockData block =
                            blockDatabase.CreateBlock(blockId);

                        ChunkUtility.SetBlock(
                            blocks,
                            x,
                            y,
                            z,
                            block);
                    }
                }
            }

            if (shoreDistanceMap.IsCreated)
                shoreDistanceMap.Dispose();

            return foundSpawnPoint;
        }

        // ================================================================
        // Projection invalidation
        // ================================================================

        private static void MarkAdjacentChunksForProjection(
            EntityCommandBuffer ecb,
            NativeParallelHashMap<int2, Entity> loadedChunks,
            int2 center)
        {
            MarkChunkForProjection(
                ecb,
                loadedChunks,
                center + new int2(1, 0));

            MarkChunkForProjection(
                ecb,
                loadedChunks,
                center + new int2(-1, 0));

            MarkChunkForProjection(
                ecb,
                loadedChunks,
                center + new int2(0, 1));

            MarkChunkForProjection(
                ecb,
                loadedChunks,
                center + new int2(0, -1));
        }

        private static void MarkChunkForProjection(
            EntityCommandBuffer ecb,
            NativeParallelHashMap<int2, Entity> loadedChunks,
            int2 coordinate)
        {
            if (!loadedChunks.TryGetValue(
                    coordinate,
                    out Entity entity))
            {
                return;
            }

            ecb.SetComponentEnabled<ChunkNeedsProjection>(
                entity,
                true);
        }
    }
}
