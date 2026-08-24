using Game.World.Blocks;
using Game.World.Generation.Biomes.Ocean;
using Game.World.Generation.Biomes.RockyShore;
using Unity.Entities;

namespace Game.World.Generation.Biomes
{
    public struct BiomeTerrainResolver : IComponentData
    {
        private OceanTerrain ocean;
        private RockyShoreTerrain rockyShore;

        public BiomeTerrainResolver(
            in OceanSettingsComponent oceanSettings,
            in RockyShoreSettingsComponent rockyShoreSettings)
        {
            ocean = new OceanTerrain(oceanSettings);
            rockyShore = new RockyShoreTerrain(
                rockyShoreSettings);
        }

        public BiomeTerrainSample Sample(
            WorldBiome biome,
            int worldX,
            int worldZ,
            int baseHeight,
            float shoreDistance,
            float biomeInfluence,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            return biome switch
            {
                WorldBiome.Ocean =>
                    ocean.Sample(baseHeight),

                WorldBiome.RockyShore =>
                    rockyShore.Sample(
                        worldX,
                        worldZ,
                        baseHeight,
                        shoreDistance,
                        biomeInfluence,
                        worldSeed,
                        worldSettings),

                _ =>
                    new BiomeTerrainSample(
                        biome,
                        baseHeight)
            };
        }

        // ================================================================
        // Rocky shore
        // ================================================================

        public int GetRockyShoreSearchDistance()
        {
            return rockyShore.GetShoreSearchDistance();
        }

        public BiomeTerrainSample SampleRockyShore(
            int worldX,
            int worldZ,
            int baseHeight,
            float shoreDistance,
            float biomeInfluence,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            return rockyShore.Sample(
                worldX,
                worldZ,
                baseHeight,
                shoreDistance,
                biomeInfluence,
                worldSeed,
                worldSettings);
        }

        public BlockId GetBlock(
            int y,
            int waterLevel,
            in BiomeTerrainSample terrainSample)
        {
            int depth =
                terrainSample.Height - 1 - y;

            return terrainSample.Biome switch
            {
                WorldBiome.RockyShore =>
                    rockyShore.GetBlock(
                        y,
                        terrainSample.Zone,
                        terrainSample.Height,
                        waterLevel),

                WorldBiome.Plains =>
                    depth == 0
                        ? BlockId.PlainsGrass
                        : depth <= 3
                            ? BlockId.Dirt
                            : BlockId.Stone,

                WorldBiome.Meadows =>
                    depth == 0
                        ? BlockId.MeadowGrass
                        : depth <= 3
                            ? BlockId.Dirt
                            : BlockId.Stone,

                WorldBiome.DarkForest =>
                    depth == 0
                        ? BlockId.DarkForestGrass
                        : depth <= 4
                            ? BlockId.Dirt
                            : BlockId.Stone,

                WorldBiome.Highlands =>
                    BlockId.Stone,

                WorldBiome.Mountains =>
                    depth == 0
                        ? BlockId.Snow
                        : BlockId.Stone,

                WorldBiome.Swamp =>
                    depth == 0
                        ? BlockId.MeadowGrass
                        : depth <= 4
                            ? BlockId.Dirt
                            : BlockId.Stone,

                WorldBiome.BurntForest =>
                    depth == 0
                        ? BlockId.PlainsGrass
                        : depth <= 3
                            ? BlockId.Dirt
                            : BlockId.Stone,

                WorldBiome.Ocean =>
                    ocean.GetBlock(
                        y,
                        terrainSample.Zone,
                        terrainSample.Height,
                        waterLevel),

                _ =>
                    depth == 0
                        ? BlockId.Sand
                        : BlockId.Stone
            };
        }
    }
}