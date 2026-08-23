using Game.World.Blocks;

namespace Game.World.Generation.Biomes.Ocean
{
    public readonly struct OceanTerrain
    {
        private readonly OceanSettingsComponent settings;

        public OceanTerrain(in OceanSettingsComponent settings)
        {
            this.settings = settings;
        }

        public BlockId GetBlock(int y, int terrainHeight, int waterLevel)
        {
            if (y < terrainHeight)
            {
                int depth = terrainHeight - 1 - y;

                return depth <= settings.SandDepth
                    ? BlockId.Sand
                    : BlockId.Stone;
            }

            if (y < waterLevel)
                return BlockId.OceanWater;

            return BlockId.Air;
        }
    }
}