using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class ContinentMask
    {
        // ================================================================
        // Public land mask
        //
        // 0 = ocean
        // 1 = land
        //
        // Використовується для форми континенту.
        // ================================================================

        public static float Sample(
            int x,
            int z,
            int resolution)
        {
            if (resolution <= 1)
            {
                return 1f;
            }


            float inverse =
                1f /
                (
                    resolution -
                    1
                );


            float2 uv =
                new float2(
                    x * inverse,
                    z * inverse);


            return Sample(
                uv);
        }


        public static float Sample(
            float2 uv)
        {
            float raw =
                SampleRaw(
                    uv);


            raw =
                math.saturate(
                    raw);


            return math.smoothstep(
                0.12f,
                0.80f,
                raw);
        }

        // ================================================================
        // Raw continental field
        //
        // Це НЕ нормалізоване 0..1 значення.
        //
        // Саме тому воно корисне для Inlandness:
        // внутрішні області можуть мати > 1.
        // ================================================================

        private static float SampleRaw(
            float2 uv)
        {
            // uv:
            //
            // 0..1
            //
            // p:
            //
            // -1..1

            float2 p =
                uv *
                2f -
                1f;


            // ============================================================
            // Macro warp
            // ============================================================

            p.x +=
                math.sin(
                    (
                        uv.y *
                        1.35f +
                        0.15f
                    ) *
                    2f *
                    math.PI) *
                0.10f;


            p.y +=
                math.sin(
                    (
                        uv.x *
                        1.10f -
                        0.20f
                    ) *
                    2f *
                    math.PI) *
                0.07f;


            // ============================================================
            // Main continental body
            // ============================================================

            float mainBody =
                Ellipse(
                    p,
                    new float2(
                        0.03f,
                        -0.04f),
                    new float2(
                        0.95f,
                        0.82f),
                    1.0f);


            // ============================================================
            // Large land masses
            // ============================================================

            float westMass =
                Blob(
                    p,
                    new float2(
                        -0.58f,
                        -0.02f),
                    new float2(
                        0.42f,
                        0.34f),
                    0.60f);


            float eastMass =
                Blob(
                    p,
                    new float2(
                        0.52f,
                        0.10f),
                    new float2(
                        0.38f,
                        0.32f),
                    0.52f);


            float southMass =
                Blob(
                    p,
                    new float2(
                        0.00f,
                        -0.52f),
                    new float2(
                        0.55f,
                        0.28f),
                    0.48f);


            float northMass =
                Blob(
                    p,
                    new float2(
                        -0.06f,
                        0.52f),
                    new float2(
                        0.48f,
                        0.26f),
                    0.42f);


            // ============================================================
            // Bays / cuts
            // ============================================================

            float northEastGulf =
                Blob(
                    p,
                    new float2(
                        0.42f,
                        0.44f),
                    new float2(
                        0.25f,
                        0.20f),
                    0.42f);


            float southWestBay =
                Blob(
                    p,
                    new float2(
                        -0.46f,
                        -0.38f),
                    new float2(
                        0.28f,
                        0.20f),
                    0.36f);


            float eastCut =
                Blob(
                    p,
                    new float2(
                        0.72f,
                        -0.08f),
                    new float2(
                        0.18f,
                        0.28f),
                    0.28f);


            // ============================================================
            // Combine
            // ============================================================

            float land =
                mainBody *
                0.78f +
                westMass +
                eastMass +
                southMass +
                northMass;


            land -=
                northEastGulf +
                southWestBay +
                eastCut;


            // ============================================================
            // Macro irregularity
            // ============================================================

            float macroNoise =
                math.sin(
                    (
                        p.x *
                        2.7f +
                        p.y *
                        1.9f
                    ) *
                    math.PI) *
                0.04f +
                math.cos(
                    (
                        p.x *
                        -1.8f +
                        p.y *
                        2.4f
                    ) *
                    math.PI) *
                0.03f;


            land +=
                macroNoise;


            // ============================================================
            // Force outer world into ocean
            // ============================================================

            float ring =
                math.length(
                    p);


            float edgeFade =
                1f -
                math.saturate(
                    (
                        ring -
                        0.72f
                    ) /
                    0.40f);


            land *=
                edgeFade;


            return land;
        }


        // ================================================================
        // Shape helpers
        // ================================================================

        private static float Ellipse(
            float2 point,
            float2 center,
            float2 radii,
            float power)
        {
            float2 normalized =
                (
                    point -
                    center
                ) /
                radii;


            float distanceSquared =
                math.dot(
                    normalized,
                    normalized);


            float value =
                math.saturate(
                    1f -
                    distanceSquared);


            return math.pow(
                value,
                power);
        }


        private static float Blob(
            float2 point,
            float2 center,
            float2 radii,
            float strength)
        {
            float2 normalized =
                (
                    point -
                    center
                ) /
                radii;


            float distanceSquared =
                math.dot(
                    normalized,
                    normalized);


            return
                math.exp(
                    -distanceSquared *
                    2.2f) *
                strength;
        }
    }
}