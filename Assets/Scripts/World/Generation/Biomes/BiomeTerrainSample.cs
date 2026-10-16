namespace Game.World.Generation.Biomes
{
    public readonly struct BiomeTerrainSample
    {
        public const byte NoZone = byte.MaxValue;

        public readonly WorldBiome Biome;
        public readonly int Height;
        public readonly bool HalfSurface;
        public readonly byte Zone;

        public BiomeTerrainSample(
            WorldBiome biome,
            int height,
            byte zone = NoZone)
        {
            Biome = biome;
            Height = height;
            HalfSurface = false;
            Zone = zone;
        }

        // Height remains the exclusive cell bound for material layers and spawn.
        public BiomeTerrainSample(WorldBiome biome, float height, byte zone = NoZone)
        {
            float surface = Unity.Mathematics.math.clamp(
                Unity.Mathematics.math.round(height * 2f) * .5f, 1f,
                Game.World.Chunks.ChunkSettings.SizeY - 1f);
            Biome = biome;
            Height = (int)Unity.Mathematics.math.ceil(surface);
            HalfSurface = surface < Height;
            Zone = zone;
        }
    }
}
