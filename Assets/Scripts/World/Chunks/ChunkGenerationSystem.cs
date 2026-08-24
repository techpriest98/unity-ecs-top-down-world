using Game.World.Blocks;
using Game.World.Generation;
using Game.World.Generation.Biomes;
using Game.World.Generation.Biomes.RockyShore;
using Game.World.Generation.Terrain;
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
        }

        public void OnDestroy(ref SystemState state)
        {
            DisposeWorldData();
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

            EnsureWorldData(worldSeed, worldSettings);

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

            foreach (var (chunk, blocks, entity) in
                     SystemAPI.Query<RefRO<ChunkComponent>, DynamicBuffer<BlockData>>()
                         .WithNone<ChunkGenerated>()
                         .WithEntityAccess())
            {
                if (generatedCount >= MaxChunksGeneratedPerFrame)
                    break;

                int2 coordinate = chunk.ValueRO.Coordinate;

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
                    blockDatabase);

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

            int2 spawnPosition = FindStartSpawnWorldPosition(
                anchorPosition,
                worldHeightMap,
                coastDistanceMap,
                landmassMap,
                biomeSamplingContext,
                worldSeed,
                worldSettings);

            // Spawn point approximately in the center of chunk (0,0).
            macroSampleOrigin = spawnPosition - new int2(
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
        // Spawn
        // ================================================================

        private static int2 FindStartSpawnWorldPosition(
            int2 anchorPosition,
            WorldHeightMap heightMap,
            CoastDistanceMap coastMap,
            LandmassMap landmassMap,
            WorldBiomeSamplingContext biomeContext,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            int searchRadius = worldSettings.MacroCellSize * 2;
            const int searchStep = 2;

            int2 bestPosition = anchorPosition;
            float bestScore = float.MaxValue;
            bool found = false;

            for (int dz = -searchRadius; dz <= searchRadius; dz += searchStep)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx += searchStep)
                {
                    int worldX = anchorPosition.x + dx;
                    int worldZ = anchorPosition.y + dz;

                    if (!WorldSamplingUtility.IsInsideWorld(
                            worldX,
                            worldZ,
                            heightMap))
                    {
                        continue;
                    }

                    float elevation = heightMap.Sample(
                        worldX,
                        worldZ);

                    int baseHeight = WorldTerrainHeight.Sample(
                        worldX,
                        worldZ,
                        elevation,
                        heightMap,
                        worldSeed,
                        worldSettings);

                    if (baseHeight < worldSettings.SeaLevelHeight)
                        continue;

                    float coastDistance = coastMap.Sample(
                        worldX,
                        worldZ);

                    bool isMainland = WorldSamplingUtility.IsMainland(
                        worldX,
                        worldZ,
                        heightMap,
                        landmassMap);

                    int waterLevel =
                        worldSettings.SeaLevelHeight - 1;

                    bool isWaterColumn =
                        baseHeight < waterLevel;

                    float2 uv = WorldSamplingUtility.WorldToUv(
                        worldX,
                        worldZ,
                        heightMap);

                    float biomeElevation = math.max(
                        elevation,
                        worldSettings.MacroSeaLevel);

                    WorldBiome biome = WorldBiomeSampler.Sample(
                        uv,
                        biomeElevation,
                        coastDistance,
                        isMainland,
                        isWaterColumn,
                        biomeContext);

                    if (biome != WorldBiome.RockyShore)
                        continue;

                    if (!HasOceanNearby(
                            worldX,
                            worldZ,
                            heightMap,
                            worldSeed,
                            worldSettings))
                    {
                        continue;
                    }

                    float distanceFromAnchor =
                        math.length(new float2(dx, dz)) /
                        math.max(1f, searchRadius);

                    float heightScore = math.abs(baseHeight - worldSettings.SeaLevelHeight);

                    float score =
                        coastDistance * 3f +
                        heightScore * 0.4f +
                        distanceFromAnchor * 0.15f;

                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    bestPosition = new int2(worldX, worldZ);
                    found = true;
                }
            }

            return found
                ? bestPosition
                : anchorPosition;
        }

        private static bool HasOceanNearby(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            const int distance = 16;

            return
                WorldTerrainHeight.IsWaterColumn(
                    worldX + distance,
                    worldZ,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX - distance,
                    worldZ,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX,
                    worldZ + distance,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX,
                    worldZ - distance,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX + distance,
                    worldZ + distance,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX - distance,
                    worldZ + distance,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX + distance,
                    worldZ - distance,
                    heightMap,
                    worldSeed,
                    worldSettings) ||
                WorldTerrainHeight.IsWaterColumn(
                    worldX - distance,
                    worldZ - distance,
                    heightMap,
                    worldSeed,
                    worldSettings);
        }

        // ================================================================
        // Chunk generation
        // ================================================================

        private static void GenerateChunk(
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
            in BlockDatabaseComponent blockDatabase)
        {
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

                    WorldBiome biome = WorldBiomeSampler.Sample(
                        uv,
                        biomeElevation,
                        coastDistance,
                        isMainland,
                        isWaterColumn,
                        biomeContext);

                    float rockyInfluence =
                        WorldBiomeSampler.SampleRockyShoreInfluence(
                            uv,
                            coastDistance,
                            biomeContext);

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
                            rockyInfluence,
                            worldSeed,
                            worldSettings);

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