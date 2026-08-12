using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class WorldTerrainNoise
    {
        // ================================================================
        // Domain Warp
        //
        // Дуже великий масштаб.
        // Він НЕ додає висоту напряму,
        // а викривляє координати Regional noise.
        // ================================================================

        private const float DomainWarpScale =
            1f / 300f;

        private const float DomainWarpStrength =
            35f;


        // ================================================================
        // Regional terrain
        //
        // Великі пагорби / долини.
        // ================================================================

        private const float RegionalScale =
            1f / 160f;

        private const float RegionalAmplitude =
            10f;


        // ================================================================
        // Local terrain
        //
        // Дрібніша нерівність.
        //
        // Поки domain warp до нього НЕ застосовуємо.
        // ================================================================

        private const float LocalScale =
            1f / 48f;

        private const float LocalAmplitude =
            3f;


        public static float SampleHeightOffset(
            int globalX,
            int globalZ,
            uint seed)
        {
            float2 worldPosition =
                new float2(
                    globalX,
                    globalZ);


            float2 seedOffset =
                GetSeedOffset(
                    seed);


            // ============================================================
            // DOMAIN WARP
            //
            // Нам потрібні два незалежні noise:
            //
            // warpX
            // warpZ
            //
            // щоб координати могли зміщуватись
            // у двох напрямках.
            // ============================================================

            float warpX =
                noise.snoise(
                    (
                        worldPosition +
                        seedOffset +
                        new float2(
                            137.17f,
                            491.73f)
                    ) *
                    DomainWarpScale);


            float warpZ =
                noise.snoise(
                    (
                        worldPosition +
                        seedOffset +
                        new float2(
                            -683.41f,
                            219.37f)
                    ) *
                    DomainWarpScale);


            float2 warp =
                new float2(
                    warpX,
                    warpZ) *
                DomainWarpStrength;


            float2 warpedPosition =
                worldPosition +
                warp;


            // ============================================================
            // REGIONAL
            //
            // Саме цей noise тепер читаємо
            // через warpedPosition.
            // ============================================================

            float regional =
                noise.snoise(
                    (
                        warpedPosition +
                        seedOffset
                    ) *
                    RegionalScale);


            // ============================================================
            // LOCAL
            //
            // Залишаємо без warp.
            //
            // Він повинен давати лише невелику
            // локальну нерівність,
            // а не деформувати великі форми.
            // ============================================================

            float2 localOffset =
                seedOffset *
                1.731f +
                new float2(
                    173.17f,
                    -491.63f);


            float local =
                noise.snoise(
                    (
                        worldPosition +
                        localOffset
                    ) *
                    LocalScale);


            // ============================================================
            // FINAL HEIGHT OFFSET
            //
            // Результат одразу в блоках.
            // ============================================================

            return
                regional *
                RegionalAmplitude +
                local *
                LocalAmplitude;
        }


        // ================================================================
        // Seed -> deterministic coordinate offset
        // ================================================================

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
                        seed ^
                        0x85EBCA6Bu,
                        0xC2B2AE35u));


            return new float2(
                xHash &
                0xFFFFu,

                zHash &
                0xFFFFu);
        }
    }
}