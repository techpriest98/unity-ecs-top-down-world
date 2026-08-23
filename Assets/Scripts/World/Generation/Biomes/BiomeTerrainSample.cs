namespace Game.World.Generation.Biomes
{
    public readonly struct BiomeTerrainSample
    {
        public readonly int Height;
        public readonly byte Zone;

        public BiomeTerrainSample(
            int height,
            byte zone = 0)
        {
            Height = height;
            Zone = zone;
        }
    }
}