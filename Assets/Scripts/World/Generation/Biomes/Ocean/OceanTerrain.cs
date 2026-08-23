using Game.World.Blocks;

namespace Game.World.Generation.Biomes.Ocean
{
    public static class OceanTerrain
    {
        public static BlockId GetBlock(
            int y,
            int terrainHeight,
            int waterLevel)
        {
            if (y < terrainHeight)
            {
                int depth = terrainHeight - 1 - y;

                return depth <= 3
                    ? BlockId.Sand
                    : BlockId.Stone;
            }

            if (y < waterLevel)
                return BlockId.OceanWater;

            return BlockId.Air;
        }
    }
}