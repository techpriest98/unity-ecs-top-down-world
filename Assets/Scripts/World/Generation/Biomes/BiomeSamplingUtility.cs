using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class BiomeSamplingUtility
    {
        public static float SampleNoise(
            float2 uv,
            float2 seedOffset,
            float2 offset,
            float frequency)
        {
            return noise.snoise(
                uv * frequency +
                seedOffset +
                offset);
        }

        public static float SmoothRange(
            float start,
            float end,
            float value)
        {
            float t =
                math.saturate(
                    (value - start) /
                    math.max(
                        0.0001f,
                        end - start));

            return Smooth01(t);
        }

        public static float Smooth01(float value)
        {
            value = math.saturate(value);

            return
                value *
                value *
                (3f - 2f * value);
        }
    }
}