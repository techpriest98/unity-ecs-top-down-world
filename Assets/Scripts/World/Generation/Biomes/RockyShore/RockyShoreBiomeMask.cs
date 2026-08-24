using Unity.Mathematics;

namespace Game.World.Generation.Biomes.RockyShore
{
    public static class RockyShoreBiomeMask
    {
        private const float MaxCoastDistance = 0.055f;
        private const float CoreCoastDistance = 0.025f;
        private const float NoiseFrequency = 5f;

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

            float rockyNoise = SampleNoise(
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
                SmoothRange(
                    CoreCoastDistance,
                    coastOuter,
                    coastDistance);

            return math.saturate(coastInfluence);
        }

        private static float SampleNoise(
            float2 uv,
            float2 seedOffset,
            float2 salt,
            float frequency)
        {
            return noise.snoise(
                (uv + seedOffset + salt) *
                frequency);
        }

        private static float SmoothRange(
            float minimum,
            float maximum,
            float value)
        {
            float range = math.max(
                0.0001f,
                maximum - minimum);

            return Smooth01(
                (value - minimum) / range);
        }

        private static float Smooth01(float value)
        {
            value = math.saturate(value);

            return value *
                   value *
                   (3f - 2f * value);
        }
    }
}