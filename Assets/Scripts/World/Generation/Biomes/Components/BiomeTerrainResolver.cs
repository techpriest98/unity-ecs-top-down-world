using Game.World.Blocks;
using Game.World.Generation.Biomes.Ocean;
using Unity.Entities;

namespace Game.World.Generation.Biomes
{
    public struct BiomeTerrainResolver : IComponentData
    {
        private OceanTerrain ocean;

        public BiomeTerrainResolver(in OceanSettingsComponent oceanSettings)
        {
            ocean = new OceanTerrain(oceanSettings);
        }

        public BlockId GetOceanBlock(
            int y,
            int terrainHeight,
            int waterLevel)
        {
            return ocean.GetBlock(y, terrainHeight, waterLevel);
        }
    }
}