using System;
using Unity.Collections;
using Unity.Mathematics;

using Random = Unity.Mathematics.Random;

namespace Game.World.Generation
{
    public static class DiamondSquareHeightMap
    {
        public static NativeArray<float> Generate(
            int resolution,
            uint seed,
            float roughness,
            Allocator allocator)
        {
            ValidateResolution(
                resolution);


            var heights =
                new NativeArray<float>(
                    resolution *
                    resolution,
                    allocator,
                    NativeArrayOptions.ClearMemory);


            // Unity.Mathematics.Random
            // не дозволяє seed == 0.
            uint safeSeed =
                seed == 0
                    ? 1u
                    : seed;


            var random =
                new Random(
                    safeSeed);


            int last =
                resolution - 1;


            // ============================================================
            // Initial corners
            // ============================================================

            Set(
                heights,
                resolution,
                0,
                0,
                random.NextFloat(
                    -1f,
                    1f));


            Set(
                heights,
                resolution,
                last,
                0,
                random.NextFloat(
                    -1f,
                    1f));


            Set(
                heights,
                resolution,
                0,
                last,
                random.NextFloat(
                    -1f,
                    1f));


            Set(
                heights,
                resolution,
                last,
                last,
                random.NextFloat(
                    -1f,
                    1f));


            // ============================================================
            // Diamond-Square
            // ============================================================

            int step =
                last;


            float amplitude =
                1f;


            while (step > 1)
            {
                int halfStep =
                    step / 2;


                // ========================================================
                // Square step
                // ========================================================

                for (int z = 0;
                     z < last;
                     z += step)
                {
                    for (int x = 0;
                         x < last;
                         x += step)
                    {
                        float topLeft =
                            Get(
                                heights,
                                resolution,
                                x,
                                z);


                        float topRight =
                            Get(
                                heights,
                                resolution,
                                x + step,
                                z);


                        float bottomLeft =
                            Get(
                                heights,
                                resolution,
                                x,
                                z + step);


                        float bottomRight =
                            Get(
                                heights,
                                resolution,
                                x + step,
                                z + step);


                        float average =
                            (
                                topLeft +
                                topRight +
                                bottomLeft +
                                bottomRight
                            ) *
                            0.25f;


                        float offset =
                            random.NextFloat(
                                -amplitude,
                                amplitude);


                        Set(
                            heights,
                            resolution,
                            x + halfStep,
                            z + halfStep,
                            average + offset);
                    }
                }


                // ========================================================
                // Diamond step
                // ========================================================

                for (int z = 0;
                     z < resolution;
                     z += halfStep)
                {
                    int startX =
                        (
                            (
                                z /
                                halfStep
                            ) &
                            1
                        ) == 0
                            ? halfStep
                            : 0;


                    for (int x = startX;
                         x < resolution;
                         x += step)
                    {
                        float sum =
                            0f;


                        int count =
                            0;


                        if (x - halfStep >= 0)
                        {
                            sum +=
                                Get(
                                    heights,
                                    resolution,
                                    x - halfStep,
                                    z);

                            count++;
                        }


                        if (x + halfStep <
                            resolution)
                        {
                            sum +=
                                Get(
                                    heights,
                                    resolution,
                                    x + halfStep,
                                    z);

                            count++;
                        }


                        if (z - halfStep >= 0)
                        {
                            sum +=
                                Get(
                                    heights,
                                    resolution,
                                    x,
                                    z - halfStep);

                            count++;
                        }


                        if (z + halfStep <
                            resolution)
                        {
                            sum +=
                                Get(
                                    heights,
                                    resolution,
                                    x,
                                    z + halfStep);

                            count++;
                        }


                        float average =
                            sum /
                            count;


                        float offset =
                            random.NextFloat(
                                -amplitude,
                                amplitude);


                        Set(
                            heights,
                            resolution,
                            x,
                            z,
                            average + offset);
                    }
                }


                step =
                    halfStep;


                amplitude *=
                    roughness;
            }


            // ============================================================
            // Normalize to 0..1
            // ============================================================

            Normalize(
                heights);


            return heights;
        }


        private static void Normalize(
            NativeArray<float> heights)
        {
            float min =
                float.MaxValue;


            float max =
                float.MinValue;


            for (int index = 0;
                 index < heights.Length;
                 index++)
            {
                float value =
                    heights[index];


                min =
                    math.min(
                        min,
                        value);


                max =
                    math.max(
                        max,
                        value);
            }


            float range =
                max - min;


            if (range <=
                0.000001f)
            {
                for (int index = 0;
                     index < heights.Length;
                     index++)
                {
                    heights[index] =
                        0.5f;
                }

                return;
            }


            for (int index = 0;
                 index < heights.Length;
                 index++)
            {
                heights[index] =
                    (
                        heights[index] -
                        min
                    ) /
                    range;
            }
        }


        private static float Get(
            NativeArray<float> heights,
            int resolution,
            int x,
            int z)
        {
            return heights[
                x +
                z *
                resolution];
        }


        private static void Set(
            NativeArray<float> heights,
            int resolution,
            int x,
            int z,
            float value)
        {
            heights[
                x +
                z *
                resolution] =
                value;
        }


        private static void ValidateResolution(
            int resolution)
        {
            if (resolution < 3)
            {
                throw new ArgumentException(
                    "Diamond-Square resolution " +
                    "must be at least 3.");
            }


            int intervals =
                resolution - 1;


            bool isPowerOfTwo =
                (
                    intervals &
                    (
                        intervals -
                        1
                    )
                ) == 0;


            if (!isPowerOfTwo)
            {
                throw new ArgumentException(
                    "Diamond-Square resolution " +
                    "must be 2^n + 1. " +
                    "Examples: 129, 257, 513, 4097.");
            }
        }
    }
}