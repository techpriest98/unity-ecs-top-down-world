using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Generation.Biomes.RockyShore
{
    public enum RockyShoreZone : byte
    {
        Cliff = 0,
        Ramp = 1,
        GrassTop = 2
    }

    public struct RockyShoreTerrainSample
    {
        public int Height;
        public RockyShoreZone Zone;
    }

    public static class RockyShoreTerrain
    {
        public static RockyShoreTerrainSample Sample(
            int worldX,
            int worldZ,
            int baseHeight,
            float shoreDistance,
            float biomeInfluence,
            uint worldSeed,
            in WorldGenerationSettingsComponent worldSettings,
            in RockyShoreSettingsComponent settings)
        {
            biomeInfluence = math.saturate(
                biomeInfluence);

            // ============================================================
            // Rocky Shore впливає тільки на сушу.
            //
            // Водяні колонки поки просто повертаємо без змін.
            // Пізніше взагалі перестанемо передавати їх сюди
            // з ChunkGenerationSystem.
            // ============================================================

            if (shoreDistance <= 0f)
            {
                return new RockyShoreTerrainSample
                {
                    Height = baseHeight,
                    Zone = RockyShoreZone.GrassTop
                };
            }

            float cliffWidth = math.max(
                1f,
                settings.CliffWidth);

            float inlandBlendWidth = math.max(
                6f,
                cliffWidth * 0.75f);

            float cliffEnd =
                cliffWidth;

            float2 worldPosition =
                new float2(
                    worldX,
                    worldZ);

            float2 seedOffset =
                GetSeedOffset(
                    worldSeed);

            // ============================================================
            // Cliff height
            // ============================================================

            float cliffNoise =
                noise.snoise(
                    (worldPosition +
                     seedOffset +
                     new float2(
                         137.17f,
                         -491.73f)) *
                    settings.CliffNoiseScale);

            cliffNoise =
                cliffNoise * 0.5f + 0.5f;

            float cliffHeight =
                math.lerp(
                    settings.CliffMinHeight,
                    settings.CliffMaxHeight,
                    cliffNoise);

            // Скеля росте від приблизного рівня берега.
            //
            // Water у нас зараз на SeaLevel - 2,
            // тому SeaLevel добре працює як підніжжя скелі.
            float cliffBaseHeight =
                worldSettings.SeaLevelHeight;

            float cliffTopHeight =
                math.max(
                    baseHeight,
                    cliffBaseHeight +
                    cliffHeight);

            // ============================================================
            // Ramp mask
            // ============================================================

            float rampNoise =
                noise.snoise(
                    (worldPosition +
                     seedOffset * 1.731f +
                     new float2(
                         -683.41f,
                         219.37f)) *
                    settings.RampNoiseScale);

            float rampThreshold =
                math.min(
                    settings.RampThreshold,
                    0.999f);

            float rampMask =
                math.smoothstep(
                    rampThreshold,
                    1f,
                    rampNoise);

            rampMask =
                math.saturate(
                    rampMask *
                    settings.RampStrength);

            // ============================================================
            // Cliff / Ramp
            // ============================================================

            if (shoreDistance <= cliffEnd)
            {
                float cliffProgress =
                    math.saturate(
                        shoreDistance /
                        cliffWidth);

                // --------------------------------------------------------
                // Sharp cliff
                // --------------------------------------------------------

                float sharpness =
                    math.max(
                        0.1f,
                        settings.CliffSharpness);

                float cliffProfile =
                    1f -
                    math.pow(
                        1f - cliffProgress,
                        sharpness);

                // --------------------------------------------------------
                // Ramp
                // --------------------------------------------------------

                float rampProfile =
                    Smooth01(
                        cliffProgress);

                float heightProfile =
                    math.lerp(
                        cliffProfile,
                        rampProfile,
                        rampMask);

                float shapedHeight =
                    math.lerp(
                        cliffBaseHeight,
                        cliffTopHeight,
                        heightProfile);

                // Rocky Shore може підняти terrain,
                // але не зрізає існуючий base terrain.
                float targetHeight =
                    math.max(
                        baseHeight,
                        shapedHeight);

                int height =
                    BlendHeight(
                        baseHeight,
                        targetHeight,
                        biomeInfluence);

                RockyShoreZone zone;

                if (rampMask > 0.30f)
                {
                    zone =
                        RockyShoreZone.Ramp;
                }
                else if (heightProfile < 0.82f)
                {
                    zone =
                        RockyShoreZone.Cliff;
                }
                else
                {
                    zone =
                        RockyShoreZone.GrassTop;
                }

                return new RockyShoreTerrainSample
                {
                    Height = ClampHeight(height),
                    Zone = zone
                };
            }

            // ============================================================
            // Inland blend
            //
            // Скеля закінчилась.
            // Плавно повертаємось до звичайного terrain.
            // ============================================================

            float inlandDistance =
                shoreDistance -
                cliffEnd;

            float inlandProgress =
                math.saturate(
                    inlandDistance /
                    inlandBlendWidth);

            inlandProgress =
                Smooth01(
                    inlandProgress);

            float raisedHeight =
                math.max(
                    baseHeight,
                    cliffTopHeight);

            float inlandTargetHeight =
                math.lerp(
                    raisedHeight,
                    baseHeight,
                    inlandProgress);

            int blendedHeight =
                BlendHeight(
                    baseHeight,
                    inlandTargetHeight,
                    biomeInfluence);

            return new RockyShoreTerrainSample
            {
                Height = ClampHeight(
                    blendedHeight),

                Zone = RockyShoreZone.GrassTop
            };
        }

        // ================================================================
        // Blocks
        // ================================================================

        public static BlockId GetBlock(
            int depth,
            RockyShoreZone zone,
            in RockyShoreSettingsComponent settings)
        {
            switch (zone)
            {
                case RockyShoreZone.Cliff:
                    return BlockId.Stone;

                case RockyShoreZone.Ramp:
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
            return (int)math.round(
                math.lerp(
                    baseHeight,
                    targetHeight,
                    influence));
        }

        private static int ClampHeight(
            int height)
        {
            return math.clamp(
                height,
                1,
                ChunkSettings.SizeY - 1);
        }

        // ================================================================
        // Utility
        // ================================================================

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
                xHash & 0xFFFFu,
                zHash & 0xFFFFu);
        }
    }
}