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

        // ================================================================
        // Ocean
        // ================================================================

        public BlockId GetOceanBlock(
            int y,
            int terrainHeight,
            int waterLevel)
        {
            return ocean.GetBlock(
                y,
                terrainHeight,
                waterLevel);
        }

        // ================================================================
        // Rocky shore
        // ================================================================

        public int GetRockyShoreSearchDistance()
        {
            return rockyShore.GetShoreSearchDistance();
        }
        public RockyShoreTerrainSample SampleRockyShore(
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

        public BlockId GetRockyShoreBlock(
            int depth,
            RockyShoreZone zone)
        {
            return rockyShore.GetBlock(
                depth,
                zone);
        }
    }
}