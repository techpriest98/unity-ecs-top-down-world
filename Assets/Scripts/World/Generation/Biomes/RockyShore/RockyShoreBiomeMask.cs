using Unity.Mathematics;

namespace Game.World.Generation.Biomes.RockyShore
{
    public static class RockyShoreBiomeMask
    {
        private const float MaxCoastDistance = 0.055f;
        private const float CoreCoastDistance = 0.025f;
        private const float NoiseFrequency = 5f;
        private const float SelectionThreshold = 0.5f;


        public static bool Contains(float influence)
        {
            return influence >= SelectionThreshold;
        }

        public static float SampleInfluence(
            float2 uv,
            float coastDistance,
            in WorldBiomeSamplingContext context)
        {
            if (coastDistance >
                MaxCoastDistance * 1.15f)
            {
                return 0f;
            }

            float rockyNoise = BiomeSamplingUtility.SampleNoise(
                uv,
                context.SeedOffset,
                new float2(211.17f, -163.43f),
                NoiseFrequency);

            float noise01 =
                rockyNoise * 0.5f + 0.5f;

            float coastOuter =
                MaxCoastDistance *
                math.lerp(
                    0.88f,
                    1.12f,
                    noise01);

            float coastInfluence =
                1f -
                BiomeSamplingUtility.SmoothRange(
                    CoreCoastDistance,
                    coastOuter,
                    coastDistance);

            return math.saturate(coastInfluence);
        }
    }
}