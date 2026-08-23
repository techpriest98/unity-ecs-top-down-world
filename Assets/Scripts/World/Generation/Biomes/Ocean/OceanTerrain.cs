using Game.World.Blocks;

namespace Game.World.Generation.Biomes.Ocean
{
    public enum OceanZone : byte
    {
        DeepOcean = 1
    }

    public readonly struct OceanTerrain
    {
        private readonly OceanSettingsComponent settings;

        public OceanTerrain(in OceanSettingsComponent settings)
        {
            this.settings = settings;
        }

        public BiomeTerrainSample Sample(
            int baseHeight)
        {
            return new BiomeTerrainSample(
                WorldBiome.Ocean,
                baseHeight,
                (byte)OceanZone.DeepOcean);
        }

        public BlockId GetBlock(int y, byte zone, int terrainHeight, int waterLevel)
        {
            if (y < terrainHeight)
            {
                int depth = terrainHeight - 1 - y;

                switch((OceanZone)zone)
                {
                    case OceanZone.DeepOcean:
                        return depth <= settings.SandDepth
                            ? BlockId.Sand
                            : BlockId.Stone;
                            
                    default:
                        return BlockId.Stone;
                }
            }

            if (y < waterLevel)
                return BlockId.OceanWater;

            return BlockId.Air;
        }
    }
}