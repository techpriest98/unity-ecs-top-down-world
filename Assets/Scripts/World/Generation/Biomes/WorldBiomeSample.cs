namespace Game.World.Generation
{
    public readonly struct WorldBiomeSample
    {
        public readonly WorldBiome Biome;
        public readonly float Influence;

        public WorldBiomeSample(
            WorldBiome biome,
            float influence = 1f)
        {
            Biome = biome;
            Influence = influence;
        }
    }
}