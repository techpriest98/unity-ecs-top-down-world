using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class WorldHeightMapGenerator
    {
        // ================================================================
        // Defaults
        // ================================================================

        public const int DefaultResolution =
            129;


        public const int MacroCellSize =
            512;


        // ================================================================
        // Continental elevation
        // ================================================================

        private const float OceanBaseElevation =
            0.15f;


        private const float InlandBaseElevation =
            0.75f;


        private const float MacroVariationStrength =
            0.5f;


        // ================================================================
        // World border
        //
        // Останні 10 macro samples поступово опускаються
        // до OceanBaseElevation.
        //
        // При MacroCellSize = 512 це зона приблизно 5120 блоків.
        //
        // Це гарантує, що суша не буде обрізана
        // фізичною межею світу.
        // ================================================================

        public const int OceanBorderWidthInSamples =
            10;


        // ================================================================
        // Generate
        // ================================================================

        public static NativeArray<float> Generate(
            int resolution,
            uint seed,
            float roughness,
            Allocator allocator)
        {
            using NativeArray<float> diamondSquare =
                DiamondSquareHeightMap.Generate(
                    resolution,
                    seed,
                    roughness,
                    Allocator.Temp);


            var result =
                new NativeArray<float>(
                    resolution *
                    resolution,
                    allocator,
                    NativeArrayOptions.UninitializedMemory);


            // ============================================================
            // Build final macro elevation
            // ============================================================

            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    int index =
                        x +
                        z *
                        resolution;


                    // ====================================================
                    // Continental shape
                    // ====================================================

                    float continentMask =
                        ContinentMask.Sample(
                            x,
                            z,
                            resolution);


                    // ====================================================
                    // Diamond-Square variation
                    //
                    // DS is normalized 0..1.
                    //
                    // Convert it approximately to:
                    //
                    // -0.25 .. +0.25
                    // ====================================================

                    float macroVariation =
                        (
                            diamondSquare[index] -
                            0.5f
                        ) *
                        MacroVariationStrength;


                    // ====================================================
                    // Base elevation
                    //
                    // Ocean:
                    // ~0.15
                    //
                    // Deep inland:
                    // ~0.75
                    // ====================================================

                    float continentBase =
                        math.lerp(
                            OceanBaseElevation,
                            InlandBaseElevation,
                            continentMask);


                    float elevation =
                        continentBase +
                        macroVariation;


                    elevation =
                        math.saturate(
                            elevation);


                    // ====================================================
                    // Guaranteed ocean world border
                    // ====================================================

                    float borderMask =
                        GetOceanBorderMask(
                            x,
                            z,
                            resolution);


                    // At the physical edge:
                    //
                    // elevation = OceanBaseElevation
                    //
                    // Far enough from edge:
                    //
                    // elevation = generated elevation

                    elevation =
                        math.lerp(
                            OceanBaseElevation,
                            elevation,
                            borderMask);


                    result[index] =
                        math.saturate(
                            elevation);
                }
            }


            return result;
        }


        // ================================================================
        // Ocean border mask
        //
        // 0 = physical world edge
        // 1 = normal world generation
        // ================================================================

        private static float GetOceanBorderMask(
            int x,
            int z,
            int resolution)
        {
            if (resolution <= 1)
            {
                return 0f;
            }


            int rightDistance =
                resolution -
                1 -
                x;


            int topDistance =
                resolution -
                1 -
                z;


            int edgeDistance =
                math.min(
                    math.min(
                        x,
                        rightDistance),

                    math.min(
                        z,
                        topDistance));


            float normalized =
                math.saturate(
                    edgeDistance /
                    (float)
                    OceanBorderWidthInSamples);


            // Smoothstep without relying on a particular overload.
            //
            // 3t² - 2t³

            return
                normalized *
                normalized *
                (
                    3f -
                    2f *
                    normalized
                );
        }
    }
}