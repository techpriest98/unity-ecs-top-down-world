using Game.World.Blocks;
using Game.World.Generation;
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
        private const byte MaxDurability = byte.MaxValue;
        private const int MaxChunksGeneratedPerFrame = 2;

        private const bool ShowOceanWater = true;

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
            state.RequireForUpdate<RockyShoreSettingsComponent>();

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

            RockyShoreSettingsComponent rockyShoreSettings =
                SystemAPI.GetSingleton<RockyShoreSettingsComponent>();

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
                    rockyShoreSettings);

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
            in RockyShoreSettingsComponent rockyShoreSettings)
        {
            BlockData air = new(BlockId.Air, 0);
            BlockData dirt = new(BlockId.Dirt, MaxDurability);
            BlockData stone = new(BlockId.Stone, MaxDurability);
            BlockData sand = new(BlockId.Sand, MaxDurability);
            BlockData oceanWater = new(BlockId.OceanWater, MaxDurability);
            BlockData snow = new(BlockId.Snow, MaxDurability);

            BlockData plainsGrass =
                new(BlockId.PlainsGrass, MaxDurability);

            BlockData meadowGrass =
                new(BlockId.MeadowGrass, MaxDurability);

            BlockData darkForestGrass =
                new(BlockId.DarkForestGrass, MaxDurability);

            int shoreSearchDistance = GetShoreSearchDistance(
                rockyShoreSettings);

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
                        biomeContext);

                    float rockyInfluence =
                        WorldBiomeSampler.SampleRockyShoreInfluence(
                            uv,
                            coastDistance,
                            biomeContext);

                    // ====================================================
                    // Biome terrain shaping
                    // ====================================================

                    int terrainHeight = baseHeight;
                    RockyShoreZone rockyZone = RockyShoreZone.GrassTop;
                    bool hasRockyShoreInfluence = rockyInfluence > 0f;

                    if (hasRockyShoreInfluence)
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

                        float shoreDistance =
                            shoreDistanceMap.Get(
                                x,
                                z);

                        RockyShoreTerrainSample rockySample =
                            RockyShoreTerrain.Sample(
                                worldX,
                                worldZ,
                                baseHeight,
                                shoreDistance,
                                rockyInfluence,
                                worldSeed,
                                worldSettings,
                                rockyShoreSettings);

                        terrainHeight =
                            rockySample.Height;

                        rockyZone =
                            rockySample.Zone;
                    }

                    int waterLevel = worldSettings.SeaLevelHeight - 1;
                    bool hasWater = terrainHeight < waterLevel;

                    // ====================================================
                    // Blocks
                    // ====================================================

                    for (int y = 0; y < ChunkSettings.SizeY; y++)
                    {
                        BlockData block;

                        if (hasWater)
                        {
                            if (y < terrainHeight)
                            {
                                int depth = terrainHeight - 1 - y;

                                block = depth <= 3
                                    ? sand
                                    : stone;
                            }
                            else if (y < waterLevel && ShowOceanWater)
                            {
                                block = oceanWater;
                            }
                            else
                            {
                                block = air;
                            }
                        }
                        else if (y >= terrainHeight)
                        {
                            block = air;
                        }
                        else
                        {
                            int depth =
                                terrainHeight - 1 - y;

                            if (biome == WorldBiome.RockyShore)
                            {
                                BlockId blockId =
                                    RockyShoreTerrain.GetBlock(
                                        depth,
                                        rockyZone,
                                        rockyShoreSettings);

                                block = new BlockData(
                                    blockId,
                                    MaxDurability);
                            }
                            else
                            {
                                block = GetLandBlock(
                                    biome,
                                    depth,
                                    dirt,
                                    stone,
                                    sand,
                                    snow,
                                    plainsGrass,
                                    meadowGrass,
                                    darkForestGrass);
                            }
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

            if (shoreDistanceMap.IsCreated)
                shoreDistanceMap.Dispose();
        }

        private static int GetShoreSearchDistance(in RockyShoreSettingsComponent settings)
        {
            float cliffWidth = math.max(1f, settings.CliffWidth);
            float widthVariation = math.saturate(settings.CliffWidthVariation);

            float maxCliffWidth = cliffWidth * (1f + widthVariation);
            float maxInlandBlendWidth = math.max(6f, maxCliffWidth * 0.75f);

            return math.max(
                1,
                (int)math.ceil(maxCliffWidth + maxInlandBlendWidth));
        }

        // ================================================================
        // Other biome blocks
        // ================================================================

        private static BlockData GetLandBlock(
            WorldBiome biome,
            int depth,
            BlockData dirt,
            BlockData stone,
            BlockData sand,
            BlockData snow,
            BlockData plainsGrass,
            BlockData meadowGrass,
            BlockData darkForestGrass)
        {
            switch (biome)
            {
                case WorldBiome.Plains:
                    if (depth == 0)
                        return plainsGrass;

                    if (depth <= 3)
                        return dirt;

                    return stone;

                case WorldBiome.Meadows:
                    if (depth == 0)
                        return meadowGrass;

                    if (depth <= 3)
                        return dirt;

                    return stone;

                case WorldBiome.DarkForest:
                    if (depth == 0)
                        return darkForestGrass;

                    if (depth <= 4)
                        return dirt;

                    return stone;

                case WorldBiome.Highlands:
                    return stone;

                case WorldBiome.Mountains:
                    return depth == 0
                        ? snow
                        : stone;

                case WorldBiome.Swamp:
                    if (depth == 0)
                        return meadowGrass;

                    if (depth <= 4)
                        return dirt;

                    return stone;

                case WorldBiome.BurntForest:
                    if (depth == 0)
                        return plainsGrass;

                    if (depth <= 3)
                        return dirt;

                    return stone;

                default:
                    return depth == 0
                        ? sand
                        : stone;
            }
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