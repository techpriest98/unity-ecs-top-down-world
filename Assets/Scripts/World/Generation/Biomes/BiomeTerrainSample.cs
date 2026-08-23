namespace Game.World.Generation.Biomes
{
    public readonly struct BiomeTerrainSample
    {
        public const byte NoZone = byte.MaxValue;

        public readonly WorldBiome Biome;
        public readonly int Height;
        public readonly byte Zone;

        public BiomeTerrainSample(
            WorldBiome biome,
            int height,
            byte zone = NoZone)
        {
            Biome = biome;
            Height = height;
            Zone = zone;
        }
    }
}