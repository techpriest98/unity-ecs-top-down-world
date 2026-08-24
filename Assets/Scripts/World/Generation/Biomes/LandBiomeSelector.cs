using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class LandBiomeSelector
    {
        private const float ProgressionWarpFrequency = 2.6f;
        private const float ProgressionWarpStrength = 0.05f;

        private const float MeadowsNoiseFrequency = 2.7f;
        private const float PlainsNoiseFrequency = 2.9f;
        private const float DarkForestNoiseFrequency = 2.5f;
        private const float HighlandsNoiseFrequency = 2.4f;
        private const float PrimaryBiomeNoiseStrength = 0.45f;

        private const float PlainsMinProgression = 0.00f;
        private const float PlainsMaxProgression = 0.48f;
        private const float PlainsFeather = 0.15f;

        private const float MeadowsMinProgression = 0.14f;
        private const float MeadowsMaxProgression = 0.72f;
        private const float MeadowsFeather = 0.15f;

        private const float DarkForestMinProgression = 0.34f;
        private const float DarkForestMaxProgression = 0.88f;
        private const float DarkForestFeather = 0.16f;

        private const float HighlandsMinProgression = 0.56f;
        private const float HighlandsMaxProgression = 1.00f;
        private const float HighlandsFeather = 0.16f;

        private const float MountainMaxFinalDistance = 0.18f;
        private const float MountainNoiseFrequency = 4.0f;

        public static WorldBiome Sample(
            float2 uv,
            float elevation,
            float coastDistance,
            bool isMainland,
            float finalDistance,
            in WorldBiomeSamplingContext context)
        {
            float normalizedElevation =
                NormalizeLandElevation(
                    elevation,
                    context.SeaLevel);

            float progressionWarp =
                noise.snoise(
                    uv *
                    ProgressionWarpFrequency +
                    context.SeedOffset);

            float warpedProgression =
                math.saturate(
                    coastDistance +
                    progressionWarp *
                    ProgressionWarpStrength);

            float plainsNoise =
                BiomeSamplingUtility.SampleNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        -91.47f,
                        28.63f),
                    PlainsNoiseFrequency);

            float meadowsNoise =
                BiomeSamplingUtility.SampleNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        17.31f,
                        -43.77f),
                    MeadowsNoiseFrequency);

            float darkForestNoise =
                BiomeSamplingUtility.SampleNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        63.91f,
                        117.27f),
                    DarkForestNoiseFrequency);

            float highlandsNoise =
                BiomeSamplingUtility.SampleNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        -143.71f,
                        -81.39f),
                    HighlandsNoiseFrequency);

            float plainsRange =
                OverlapRange(
                    warpedProgression,
                    PlainsMinProgression,
                    PlainsMaxProgression,
                    PlainsFeather);

            float middleElevation =
                math.saturate(
                    1f -
                    math.abs(
                        normalizedElevation -
                        0.42f));

            float plainsScore =
                plainsRange *
                (
                    0.75f +
                    plainsNoise *
                    PrimaryBiomeNoiseStrength +
                    middleElevation * 0.07f +
                    (1f - coastDistance) * 0.05f
                );

            float meadowsRange =
                OverlapRange(
                    warpedProgression,
                    MeadowsMinProgression,
                    MeadowsMaxProgression,
                    MeadowsFeather);

            float meadowsScore =
                meadowsRange *
                (
                    0.75f +
                    meadowsNoise *
                    PrimaryBiomeNoiseStrength +
                    (1f - normalizedElevation) * 0.12f
                );

            float darkForestRange =
                OverlapRange(
                    warpedProgression,
                    DarkForestMinProgression,
                    DarkForestMaxProgression,
                    DarkForestFeather);

            float darkForestScore =
                darkForestRange *
                (
                    0.75f +
                    darkForestNoise *
                    PrimaryBiomeNoiseStrength +
                    coastDistance * 0.08f
                );

            float highlandsRange =
                OverlapRange(
                    warpedProgression,
                    HighlandsMinProgression,
                    HighlandsMaxProgression,
                    HighlandsFeather);

            float highlandsScore =
                highlandsRange *
                (
                    0.75f +
                    highlandsNoise *
                    PrimaryBiomeNoiseStrength +
                    normalizedElevation * 0.32f +
                    coastDistance * 0.06f
                );

            float mountainsScore =
                float.MinValue;

            if (isMainland &&
                finalDistance <=
                MountainMaxFinalDistance)
            {
                float finalProximity =
                    1f -
                    BiomeSamplingUtility.SmoothRange(
                        0.025f,
                        MountainMaxFinalDistance,
                        finalDistance);

                float mountainNoise =
                    BiomeSamplingUtility.SampleNoise(
                        uv,
                        context.SeedOffset,
                        new float2(
                            -247.31f,
                            193.67f),
                        MountainNoiseFrequency);

                mountainsScore =
                    finalProximity * 1.15f +
                    normalizedElevation * 0.42f +
                    warpedProgression * 0.20f +
                    mountainNoise * 0.18f;
            }

            WorldBiome biome =
                WorldBiome.Plains;

            float bestScore =
                plainsScore;

            TrySelect(
                WorldBiome.Meadows,
                meadowsScore,
                ref biome,
                ref bestScore);

            TrySelect(
                WorldBiome.DarkForest,
                darkForestScore,
                ref biome,
                ref bestScore);

            TrySelect(
                WorldBiome.Highlands,
                highlandsScore,
                ref biome,
                ref bestScore);

            TrySelect(
                WorldBiome.Mountains,
                mountainsScore,
                ref biome,
                ref bestScore);

            return biome;
        }

        private static float OverlapRange(
            float progression,
            float min,
            float max,
            float feather)
        {
            float enter =
                min <= 0f
                    ? 1f
                    : BiomeSamplingUtility.SmoothRange(
                        min - feather,
                        min,
                        progression);

            float exit =
                max >= 1f
                    ? 1f
                    : 1f -
                      BiomeSamplingUtility.SmoothRange(
                          max,
                          max + feather,
                          progression);

            return math.saturate(
                enter * exit);
        }

        private static void TrySelect(
            WorldBiome candidate,
            float candidateScore,
            ref WorldBiome currentBiome,
            ref float currentScore)
        {
            if (candidateScore <= currentScore)
                return;

            currentScore =
                candidateScore;

            currentBiome =
                candidate;
        }

        private static float NormalizeLandElevation(
            float elevation,
            float seaLevel)
        {
            return math.saturate(
                (elevation - seaLevel) /
                math.max(
                    0.0001f,
                    1f - seaLevel));
        }
    }
}