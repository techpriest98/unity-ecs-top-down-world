using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct WorldBiomeSamplingContext
    {
        public float2 StartUv;
        public float2 FinalUv;
        public float2 SeedOffset;
        public float SeaLevel;
    }

    public static class WorldBiomeSampler
    {
        // Progression warp
        private const float ProgressionWarpFrequency = 2.6f;
        private const float ProgressionWarpStrength = 0.05f;

        // Primary biome noise
        private const float MeadowsNoiseFrequency = 2.7f;
        private const float PlainsNoiseFrequency = 2.9f;
        private const float DarkForestNoiseFrequency = 2.5f;
        private const float HighlandsNoiseFrequency = 2.4f;
        private const float PrimaryBiomeNoiseStrength = 0.45f;

        // Plains / steppe
        private const float PlainsMinProgression = 0.00f;
        private const float PlainsMaxProgression = 0.48f;
        private const float PlainsFeather = 0.15f;

        // Meadows
        private const float MeadowsMinProgression = 0.14f;
        private const float MeadowsMaxProgression = 0.72f;
        private const float MeadowsFeather = 0.15f;

        // Dark Forest
        private const float DarkForestMinProgression = 0.34f;
        private const float DarkForestMaxProgression = 0.88f;
        private const float DarkForestFeather = 0.16f;

        // Highlands
        private const float HighlandsMinProgression = 0.56f;
        private const float HighlandsMaxProgression = 1.00f;
        private const float HighlandsFeather = 0.16f;

        // Rocky Shore
        private const float RockyShoreMaxCoastDistance = 0.055f;
        private const float RockyShoreCoreCoastDistance = 0.025f;
        private const float RockyNoiseFrequency = 5.0f;

        // Mountains
        private const float MountainMaxFinalDistance = 0.18f;
        private const float MountainNoiseFrequency = 4.0f;

        public static WorldBiomeSamplingContext CreateContext(
            WorldAnchors anchors,
            float seaLevel,
            uint seed)
        {
            return new WorldBiomeSamplingContext
            {
                StartUv = anchors.StartUv,
                FinalUv = anchors.FinalUv,
                SeedOffset = GetSeedOffset(seed),
                SeaLevel = seaLevel
            };
        }
        public static float SampleRockyShoreInfluence(
            float2 uv,
            float coastDistance,
            WorldBiomeSamplingContext context)
        {
            if (coastDistance > RockyShoreMaxCoastDistance * 1.15f)
            {
                return 0f;
            }

            float rockyNoise =
                SampleBiomeNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        211.17f,
                        -163.43f),
                    RockyNoiseFrequency);

            float noise01 =
                rockyNoise * 0.5f + 0.5f;

            // Noise робить внутрішню межу біома нерівною.
            // Безпосередньо берег залишається суцільним Rocky Shore.
            float coastOuter =
                RockyShoreMaxCoastDistance *
                math.lerp(
                    0.88f,
                    1.12f,
                    noise01);

            float coastInfluence =
                1f -
                SmoothRange(
                    RockyShoreCoreCoastDistance,
                    coastOuter,
                    coastDistance);

            return math.saturate(
                coastInfluence);
        }

        public static WorldBiome Sample(
            float2 uv,
            float elevation,
            float coastDistance,
            bool isMainland,
            WorldBiomeSamplingContext context)
        {
            // Категоріальний biome для води все ще Ocean.
            //
            // RockyShoreInfluence для цієї ж координати при цьому
            // може бути > 0 і буде використаний terrain generator'ом.
            if (elevation < context.SeaLevel)
                return WorldBiome.Ocean;

            float normalizedElevation =
                NormalizeLandElevation(
                    elevation,
                    context.SeaLevel);

            float startDistance =
                math.distance(
                    uv,
                    context.StartUv);

            float finalDistance =
                math.distance(
                    uv,
                    context.FinalUv);

            // ============================================================
            // Exact anchors
            // ============================================================

            if (startDistance <= 0.000001f)
                return WorldBiome.RockyShore;

            if (finalDistance <= 0.000001f)
                return WorldBiome.Mountains;

            // ============================================================
            // Rocky Shore
            //
            // На суші categorical biome використовує той самий influence,
            // який пізніше зможемо використати й під водою.
            // ============================================================

            float rockyShoreInfluence =
                SampleRockyShoreInfluence(
                    uv,
                    coastDistance,
                    context);

            if (rockyShoreInfluence >= 0.5f)
                return WorldBiome.RockyShore;

            // ============================================================
            // Progression
            // ============================================================

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

            // ============================================================
            // Biome noise
            // ============================================================

            float plainsNoise =
                SampleBiomeNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        -91.47f,
                        28.63f),
                    PlainsNoiseFrequency);

            float meadowsNoise =
                SampleBiomeNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        17.31f,
                        -43.77f),
                    MeadowsNoiseFrequency);

            float darkForestNoise =
                SampleBiomeNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        63.91f,
                        117.27f),
                    DarkForestNoiseFrequency);

            float highlandsNoise =
                SampleBiomeNoise(
                    uv,
                    context.SeedOffset,
                    new float2(
                        -143.71f,
                        -81.39f),
                    HighlandsNoiseFrequency);

            // ============================================================
            // Plains
            // ============================================================

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

            // ============================================================
            // Meadows
            // ============================================================

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

            // ============================================================
            // Dark Forest
            // ============================================================

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

            // ============================================================
            // Highlands
            // ============================================================

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

            // ============================================================
            // Mountains
            // ============================================================

            float mountainsScore =
                float.MinValue;

            if (isMainland &&
                finalDistance <=
                MountainMaxFinalDistance)
            {
                float finalProximity =
                    1f -
                    SmoothRange(
                        0.025f,
                        MountainMaxFinalDistance,
                        finalDistance);

                float mountainNoise =
                    SampleBiomeNoise(
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

            // ============================================================
            // Select primary biome
            // ============================================================

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
                    : SmoothRange(
                        min - feather,
                        min,
                        progression);

            float exit =
                max >= 1f
                    ? 1f
                    : 1f -
                      SmoothRange(
                          max,
                          max + feather,
                          progression);

            return math.saturate(
                enter * exit);
        }

        private static float SampleBiomeNoise(
            float2 uv,
            float2 seedOffset,
            float2 biomeOffset,
            float frequency)
        {
            return noise.snoise(
                uv * frequency +
                seedOffset +
                biomeOffset);
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

        private static float SmoothRange(
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

        private static float Smooth01(
            float value)
        {
            value =
                math.saturate(value);

            return
                value *
                value *
                (3f - 2f * value);
        }

        private static float2 GetSeedOffset(
            uint seed)
        {
            uint xHash =
                math.hash(
                    new uint2(
                        seed,
                        0x9E3779B9u));

            uint zHash =
                math.hash(
                    new uint2(
                        seed ^ 0x85EBCA6Bu,
                        0xC2B2AE35u));

            return new float2(
                (xHash & 0xFFFFu) / 4096f,
                (zHash & 0xFFFFu) / 4096f);
        }
    }
}