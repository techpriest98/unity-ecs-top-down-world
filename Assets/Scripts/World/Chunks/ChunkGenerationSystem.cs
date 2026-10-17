using Game.World.Blocks;
using Game.World.Generation;
using Game.World.Generation.Biomes;
using Game.World.Generation.Biomes.RockyShore;
using Game.World.Generation.Terrain;
using Game.World.Rendering;
using Game.World.Saving;
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
        private Entity minimapEntity;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ChunkComponent>();
            state.RequireForUpdate<WorldSeedComponent>();
            state.RequireForUpdate<WorldGenerationSettingsComponent>();
            state.RequireForUpdate<BiomeTerrainResolver>();
            state.RequireForUpdate<BlockDatabaseComponent>();
            state.RequireForUpdate<WorldBlockChanges>();

            worldHeightMap = default;
            coastDistanceMap = default;
            landmassMap = default;
            worldAnchors = default;
            biomeSamplingContext = default;

            macroSampleOrigin = int2.zero;

            loadedSeed = 0;
            loadedWorldSettings = default;
            hasLoadedWorld = false;
            minimapEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(minimapEntity, default(WorldMinimapInfo));
            state.EntityManager.AddBuffer<WorldMinimapCell>(minimapEntity);
        }

        public void OnDestroy(ref SystemState state)
        {
            DisposeWorldData();
            if (state.EntityManager.Exists(minimapEntity)) state.EntityManager.DestroyEntity(minimapEntity);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldBlockChanges changes = SystemAPI.GetSingleton<WorldBlockChanges>();
            if (!changes.IsReady)
                return;

            uint worldSeed = SystemAPI.GetSingleton<WorldSeedComponent>().Value;

            WorldGenerationSettingsComponent worldSettings =
                SystemAPI.GetSingleton<WorldGenerationSettingsComponent>();

            BiomeTerrainResolver biomeTerrainResolver =
                SystemAPI.GetSingleton<BiomeTerrainResolver>();

            BlockDatabaseComponent blockDatabase =
                SystemAPI.GetSingleton<BlockDatabaseComponent>();

            if (EnsureWorldData(worldSeed, worldSettings)) PublishMinimap(ref state, worldSeed, worldSettings);
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

            foreach (var (chunk, blocks, columns, entity) in
                     SystemAPI.Query<
                             RefRO<ChunkComponent>,
                             DynamicBuffer<BlockData>,
                             DynamicBuffer<ChunkColumnData>>()
                         .WithNone<ChunkGenerated>()
                         .WithEntityAccess())
            {
                if (generatedCount >= MaxChunksGeneratedPerFrame)
                    break;

                int2 coordinate = chunk.ValueRO.Coordinate;

                GenerateChunk(
                    blocks,
                    state.EntityManager.GetBuffer<SplitCellData>(entity),
                    columns,
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

                changes.Apply(coordinate, blocks, state.EntityManager.GetBuffer<SplitCellData>(entity));
                var surface = state.EntityManager.GetBuffer<ChunkSurfaceSample>(entity);
                surface.ResizeUninitialized(ChunkSettings.SizeX * ChunkSettings.SizeZ);
                var surfaceSplits = state.EntityManager.GetBuffer<SplitCellData>(entity, true);
                for (int z = 0; z < ChunkSettings.SizeZ; z++)
                    for (int x = 0; x < ChunkSettings.SizeX; x++)
                    {
                        surface[z * ChunkSettings.SizeX + x] = default;
                        TerrainMinimapUtility.UpdateColumn(blocks, surfaceSplits, surface, x, z);
                    }
                state.EntityManager.SetComponentData(entity, new ChunkSurfaceRevision { Value = 1 });

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

        private bool EnsureWorldData(
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
                return false;

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

            macroSampleOrigin = anchorPosition - new int2(
                ChunkSettings.SizeX / 2,
                ChunkSettings.SizeZ / 2);

            loadedSeed = worldSeed;
            loadedWorldSettings = worldSettings;
            hasLoadedWorld = true;
            return true;
        }

        private void PublishMinimap(ref SystemState state, uint seed, in WorldGenerationSettingsComponent settings)
        {
            using var map = WorldBiomeMap.Create(worldHeightMap, coastDistanceMap, landmassMap,
                worldAnchors, settings.MacroSeaLevel, seed, Allocator.Temp);
            var pixels = state.EntityManager.GetBuffer<WorldMinimapCell>(minimapEntity);
            pixels.ResizeUninitialized(map.Resolution * map.Resolution);
            for (int z = 0; z < map.Resolution; z++)
                for (int x = 0; x < map.Resolution; x++)
                    pixels[x + z * map.Resolution] = new WorldMinimapCell { Biome = map.Get(x, z) };
            uint revision = state.EntityManager.GetComponentData<WorldMinimapInfo>(minimapEntity).Revision + 1;
            state.EntityManager.SetComponentData(minimapEntity, new WorldMinimapInfo
            {
                Resolution = map.Resolution,
                WorldMinimum = new int2(worldHeightMap.WorldMinX, worldHeightMap.WorldMinZ),
                WorldSize = worldHeightMap.WorldSizeInBlocks,
                MacroOrigin = macroSampleOrigin,
                Revision = revision
            });
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

        private static void GenerateChunk(
            DynamicBuffer<BlockData> blocks,
            DynamicBuffer<SplitCellData> splits,
            DynamicBuffer<ChunkColumnData> columns,
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
            splits.Clear();
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

                    float baseSurfaceHeight = WorldTerrainHeight.SampleContinuous(
                        worldX,
                        worldZ,
                        elevation,
                        heightMap,
                        worldSeed,
                        worldSettings);

                    int baseHeight = (int)math.round(baseSurfaceHeight);

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

                    int waterLevel = worldSettings.WaterSurfaceHeight;
                    bool isWaterColumn = worldSettings.IsWaterColumn(baseHeight);

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
                    // Ocean columns normally bypass Rocky Shore shaping, but
                    // nearby water must share the same seeded rock footprints.
                    bool rockyCoast = biome == WorldBiome.RockyShore ||
                        (biome == WorldBiome.Ocean && RockyShoreBiomeMask.Contains(
                            RockyShoreBiomeMask.SampleInfluence(uv, coastDistance, biomeContext)));
                    if (rockyCoast)
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
                            biome == WorldBiome.RockyShore || biome == WorldBiome.Ocean ? baseSurfaceHeight : baseHeight,
                            shoreDistance,
                            biomeInfluence,
                            worldSeed,
                            worldSettings);

                    if (rockyCoast)
                        terrainSample = biomeTerrainResolver.ApplyCoastalRocks(worldX, worldZ,
                            shoreDistance, worldSeed, worldSettings, terrainSample);

                    int columnIndex = z * ChunkSettings.SizeX + x;

                    columns[columnIndex] = new ChunkColumnData
                    {
                        Biome = terrainSample.Biome,
                        Zone = terrainSample.Zone,
                        SurfaceHeight = terrainSample.Height
                    };

                    for (int y = 0; y < ChunkSettings.SizeY; y++)
                    {
                        if (terrainSample.HasRockCover)
                        {
                            var lower = blockDatabase.CreateBlock(
                                Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(y * 2, waterLevel, terrainSample));
                            var upper = blockDatabase.CreateBlock(
                                Game.World.Generation.Biomes.RockyShore.RockyShoreTerrain.GetCoveredBlock(y * 2 + 1, waterLevel, terrainSample));
                            ChunkUtility.SetBlock(blocks, x, y, z, lower);
                            if (lower.BlockId != upper.BlockId)
                                SplitCellUtility.Set(blocks, splits, ChunkUtility.ToIndex(x, y, z),
                                    new CellContents(new BlockPart(lower.BlockId, lower.Durability),
                                        new BlockPart(upper.BlockId, upper.Durability)));
                            continue;
                        }
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
                        if (terrainSample.HalfSurface && y == terrainSample.Height - 1)
                        {
                            var above = blockDatabase.CreateBlock(y < waterLevel ? BlockId.OceanWater : BlockId.Air);
                            SplitCellUtility.Set(blocks, splits, ChunkUtility.ToIndex(x, y, z),
                                new CellContents(new BlockPart(block.BlockId, block.Durability),
                                    new BlockPart(above.BlockId, above.Durability)));
                        }
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
