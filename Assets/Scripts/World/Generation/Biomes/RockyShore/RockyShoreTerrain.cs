using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Generation.Biomes.RockyShore
{
    public enum RockyShoreZone : byte
    {
        Cliff = 2,
        Ramp = 3,
        GrassTop = 4,
        Beach = 5,
        ShallowWater = 6,
    }

    public readonly struct RockyShoreTerrain
    {
        private readonly RockyShoreSettingsComponent settings;

        public RockyShoreTerrain(in RockyShoreSettingsComponent settings)
        {
            this.settings = settings;
        }

        public BiomeTerrainSample Sample(
            int worldX,
            int worldZ,
            int baseHeight,
            float shoreDistance,
            float biomeInfluence,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings)
        {
            biomeInfluence = math.saturate(biomeInfluence);

            if (shoreDistance <= 0f)
            {
                return new BiomeTerrainSample(baseHeight, (byte)RockyShoreZone.Beach);
            }

            float2 worldPosition = new float2(
                worldX,
                worldZ);

            float2 seedOffset = GetSeedOffset(worldSeed);

            // ============================================================
            // Cliff width
            // ============================================================

            float widthNoise = noise.snoise(
                (worldPosition +
                 seedOffset * 0.913f +
                 new float2(-317.43f, 711.19f)) *
                settings.CliffWidthNoiseScale);

            widthNoise = widthNoise * 0.5f + 0.5f;

            float baseCliffWidth = math.max(
                1f,
                settings.CliffWidth);

            float widthVariation = math.saturate(
                settings.CliffWidthVariation);

            float cliffWidth = baseCliffWidth * math.lerp(
                1f - widthVariation,
                1f + widthVariation,
                widthNoise);

            float inlandBlendWidth = math.max(
                6f,
                cliffWidth * 0.75f);

            float cliffEnd = cliffWidth;

            // ============================================================
            // Cliff height
            // ============================================================

            float cliffNoise = noise.snoise(
                (worldPosition +
                 seedOffset +
                 new float2(137.17f, -491.73f)) *
                settings.CliffNoiseScale);

            cliffNoise = cliffNoise * 0.5f + 0.5f;

            float cliffHeight = math.lerp(
                settings.CliffMinHeight,
                settings.CliffMaxHeight,
                cliffNoise);

            float cliffBaseHeight =
                worldSettings.SeaLevelHeight;

            float naturalCliffTopHeight =
                cliffBaseHeight + cliffHeight;

            // Дозволяємо базовому рельєфу виступати максимум
            // на 3 блоки над основною висотою скелі.
            const float MaxTopRelief = 3f;

            float trimmedBaseHeight = math.min(
                baseHeight,
                naturalCliffTopHeight + MaxTopRelief);

            float cliffTopHeight = math.max(
                trimmedBaseHeight,
                naturalCliffTopHeight);

            // ============================================================
            // Ramp mask
            // ============================================================

            float rampNoise = noise.snoise(
                (worldPosition +
                 seedOffset * 1.731f +
                 new float2(-683.41f, 219.37f)) *
                settings.RampNoiseScale);

            rampNoise = rampNoise * 0.5f + 0.5f;

            float rampThreshold = math.clamp(
                settings.RampThreshold,
                0f,
                0.999f);

            float rampMask = math.smoothstep(
                rampThreshold,
                1f,
                rampNoise);

            rampMask = math.saturate(
                rampMask * settings.RampStrength);

            // ============================================================
            // Cliff / Ramp
            // ============================================================

            if (shoreDistance <= cliffEnd)
            {
                float cliffProgress = math.saturate(
                    shoreDistance / cliffWidth);

                float sharpness = math.max(
                    0.1f,
                    settings.CliffSharpness);

                float cliffProfile = 1f - math.pow(
                    1f - cliffProgress,
                    sharpness);

                float rampProfile = Smooth01(
                    cliffProgress);

                float heightProfile = math.lerp(
                    cliffProfile,
                    rampProfile,
                    rampMask);

                float shapedHeight = math.lerp(
                    cliffBaseHeight,
                    cliffTopHeight,
                    heightProfile);

                int height = BlendHeight(
                    baseHeight,
                    shapedHeight,
                    biomeInfluence);

                RockyShoreZone zone;

                if (rampMask > 0.30f)
                {
                    zone = RockyShoreZone.Ramp;
                }
                else if (heightProfile < 0.82f)
                {
                    zone = RockyShoreZone.Cliff;
                }
                else
                {
                    zone = RockyShoreZone.GrassTop;
                }

                return new BiomeTerrainSample(ClampHeight(height), (byte)zone);
            }

            // ============================================================
            // Inland blend
            // ============================================================

            float inlandDistance = shoreDistance - cliffEnd;
            float inlandProgress = math.saturate(
                inlandDistance / inlandBlendWidth);

            inlandProgress = Smooth01(inlandProgress);

            float raisedHeight = math.max(
                baseHeight,
                cliffTopHeight);

            float targetHeight = math.lerp(
                raisedHeight,
                baseHeight,
                inlandProgress);

            int blendedHeight = BlendHeight(
                baseHeight,
                targetHeight,
                biomeInfluence);

            return new BiomeTerrainSample(
                ClampHeight(blendedHeight),
                (byte)RockyShoreZone.GrassTop);
        }

        public int GetShoreSearchDistance()
        {
            float cliffWidth = math.max(
                1f,
                settings.CliffWidth);

            float widthVariation = math.saturate(
                settings.CliffWidthVariation);

            float maxCliffWidth =
                cliffWidth * (1f + widthVariation);

            float maxInlandBlendWidth = math.max(
                6f,
                maxCliffWidth * 0.75f);

            return math.max(
                1,
                (int)math.ceil(
                    maxCliffWidth +
                    maxInlandBlendWidth));
        }
        
        // ================================================================
        // Blocks
        // ================================================================

        public BlockId GetBlock(int y, RockyShoreZone zone, int terrainHeight, int waterLevel)
        {
            if (y >= terrainHeight)
            {
                return BlockId.Air;
            }

            int depth = terrainHeight - 1 - y;
        
            switch (zone)
            {
                case RockyShoreZone.Beach:
                    return BlockId.Stone;

                case RockyShoreZone.Cliff:
                    return BlockId.Stone;

                case RockyShoreZone.Ramp:
                    if (depth == 0)
                        return BlockId.PlainsGrass;

                    return BlockId.Stone;

                case RockyShoreZone.GrassTop:
                    if (depth == 0)
                        return BlockId.PlainsGrass;

                    if (depth <= settings.GrassDepth)
                        return BlockId.Dirt;

                    return BlockId.Stone;

                default:
                    return BlockId.Stone;
            }
        }

        // ================================================================
        // Height
        // ================================================================

        private static int BlendHeight(
            int baseHeight,
            float targetHeight,
            float influence)
        {
            return (int)math.round(math.lerp(
                baseHeight,
                targetHeight,
                influence));
        }

        private static int ClampHeight(int height)
        {
            return math.clamp(
                height,
                1,
                ChunkSettings.SizeY - 1);
        }

        // ================================================================
        // Utility
        // ================================================================

        private static float Smooth01(float value)
        {
            value = math.saturate(value);

            return value *
                   value *
                   (3f - 2f * value);
        }

        private static float2 GetSeedOffset(uint seed)
        {
            uint xHash = math.hash(new uint2(
                seed,
                0x9E3779B9u));

            uint zHash = math.hash(new uint2(
                seed ^ 0x85EBCA6Bu,
                0xC2B2AE35u));

            return new float2(
                xHash & 0xFFFFu,
                zHash & 0xFFFFu);
        }
    }
}