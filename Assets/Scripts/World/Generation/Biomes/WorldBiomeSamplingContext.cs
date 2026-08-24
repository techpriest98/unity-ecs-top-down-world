using Unity.Mathematics;

namespace Game.World.Generation
{
    public readonly struct WorldBiomeSamplingContext
    {
        public readonly float2 StartUv;
        public readonly float2 FinalUv;
        public readonly float2 SeedOffset;
        public readonly float SeaLevel;

        public WorldBiomeSamplingContext(
            float2 startUv,
            float2 finalUv,
            float2 seedOffset,
            float seaLevel)
        {
            StartUv = startUv;
            FinalUv = finalUv;
            SeedOffset = seedOffset;
            SeaLevel = seaLevel;
        }
    }
}