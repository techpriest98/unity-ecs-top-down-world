using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct CoastDistanceMap :
        IDisposable
    {
        private const float DiagonalCost =
            1.41421356237f;


        private NativeArray<float>
            distances;


        private int resolution;

        private int worldMinX;

        private int worldMinZ;

        private int worldSizeInBlocks;


        public bool IsCreated =>
            distances.IsCreated;


        public int Resolution =>
            resolution;


        // ================================================================
        // Creation
        // ================================================================

        public static CoastDistanceMap Create(
            WorldHeightMap heightMap,
            float seaLevel,
            Allocator allocator)
        {
            int resolution =
                heightMap.Resolution;


            int cellCount =
                resolution *
                resolution;


            var result =
                new NativeArray<float>(
                    cellCount,
                    allocator,
                    NativeArrayOptions.UninitializedMemory);


            // Temporary land/ocean mask.
            //
            // Тут НЕ використовуємо "using var",
            // бо NativeArray<byte> потрібно змінювати
            // через land[index] = ...
            var land =
                new NativeArray<byte>(
                    cellCount,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);


            // ============================================================
            // 1. Land / Ocean
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
                        ToIndex(
                            x,
                            z,
                            resolution);


                    float elevation =
                        heightMap.Get(
                            x,
                            z);


                    land[index] =
                        elevation >=
                        seaLevel
                            ? (byte)1
                            : (byte)0;


                    result[index] =
                        float.MaxValue;
                }
            }


            // ============================================================
            // 2. Detect coastline
            //
            // Coastal land cell =
            // land cell touching ocean.
            //
            // Ocean cells themselves also stay at 0.
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
                        ToIndex(
                            x,
                            z,
                            resolution);


                    // ----------------------------------------------------
                    // Ocean
                    // ----------------------------------------------------

                    if (land[index] == 0)
                    {
                        result[index] =
                            0f;

                        continue;
                    }


                    // ----------------------------------------------------
                    // Coast
                    // ----------------------------------------------------

                    if (TouchesOcean(
                            land,
                            resolution,
                            x,
                            z))
                    {
                        result[index] =
                            0f;
                    }
                }
            }


            // ============================================================
            // 3. Chamfer distance transform
            //
            // Forward pass
            //
            // Поширюємо відстань від узбережжя
            // у напрямку:
            //
            // left
            // up
            // upper-left
            // upper-right
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
                        ToIndex(
                            x,
                            z,
                            resolution);


                    if (land[index] == 0 ||
                        result[index] == 0f)
                    {
                        continue;
                    }


                    float distance =
                        result[index];


                    // Left
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x - 1,
                        z,
                        1f,
                        ref distance);


                    // Up
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x,
                        z - 1,
                        1f,
                        ref distance);


                    // Upper-left
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x - 1,
                        z - 1,
                        DiagonalCost,
                        ref distance);


                    // Upper-right
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x + 1,
                        z - 1,
                        DiagonalCost,
                        ref distance);


                    result[index] =
                        distance;
                }
            }


            // ============================================================
            // Backward pass
            //
            // Поширюємо відстань у зворотному напрямку:
            //
            // right
            // down
            // lower-right
            // lower-left
            // ============================================================

            for (int z =
                     resolution - 1;
                 z >= 0;
                 z--)
            {
                for (int x =
                         resolution - 1;
                     x >= 0;
                     x--)
                {
                    int index =
                        ToIndex(
                            x,
                            z,
                            resolution);


                    if (land[index] == 0 ||
                        result[index] == 0f)
                    {
                        continue;
                    }


                    float distance =
                        result[index];


                    // Right
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x + 1,
                        z,
                        1f,
                        ref distance);


                    // Down
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x,
                        z + 1,
                        1f,
                        ref distance);


                    // Lower-right
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x + 1,
                        z + 1,
                        DiagonalCost,
                        ref distance);


                    // Lower-left
                    TryNeighbour(
                        land,
                        result,
                        resolution,
                        x - 1,
                        z + 1,
                        DiagonalCost,
                        ref distance);


                    result[index] =
                        distance;
                }
            }


            // ============================================================
            // 4. Find deepest inland point
            // ============================================================

            float maxDistance =
                0f;


            for (int index = 0;
                 index < cellCount;
                 index++)
            {
                if (land[index] == 0)
                {
                    continue;
                }


                float distance =
                    result[index];


                if (distance ==
                    float.MaxValue)
                {
                    continue;
                }


                maxDistance =
                    math.max(
                        maxDistance,
                        distance);
            }


            // ============================================================
            // 5. Normalize to 0..1
            //
            // 0 = ocean / coast
            // 1 = globally deepest inland location
            // ============================================================

            if (maxDistance >
                0.0001f)
            {
                for (int index = 0;
                     index < cellCount;
                     index++)
                {
                    // ----------------------------------------------------
                    // Ocean
                    // ----------------------------------------------------

                    if (land[index] == 0)
                    {
                        result[index] =
                            0f;

                        continue;
                    }


                    // ----------------------------------------------------
                    // Safety fallback
                    // ----------------------------------------------------

                    if (result[index] ==
                        float.MaxValue)
                    {
                        result[index] =
                            0f;

                        continue;
                    }


                    // ----------------------------------------------------
                    // Normalize
                    // ----------------------------------------------------

                    result[index] =
                        math.saturate(
                            result[index] /
                            maxDistance);
                }
            }
            else
            {
                // Немає нормальної inland distance.
                //
                // Наприклад, якщо вся карта —
                // океан або дуже вузька суша.

                for (int index = 0;
                     index < cellCount;
                     index++)
                {
                    result[index] =
                        0f;
                }
            }


            // ============================================================
            // Temporary mask cleanup
            // ============================================================

            land.Dispose();


            // ============================================================
            // Result
            // ============================================================

            return new CoastDistanceMap
            {
                distances =
                    result,

                resolution =
                    resolution,

                worldMinX =
                    heightMap.WorldMinX,

                worldMinZ =
                    heightMap.WorldMinZ,

                worldSizeInBlocks =
                    heightMap.WorldSizeInBlocks
            };
        }


        // ================================================================
        // Runtime sample
        //
        // Bilinear interpolation between macro samples.
        //
        // Result:
        //
        // 0..1
        // ================================================================

        public float Sample(
            int globalX,
            int globalZ)
        {
            return Sample(
                (float)globalX,
                (float)globalZ);
        }


        public float Sample(
            float globalX,
            float globalZ)
        {
            if (!distances.IsCreated ||
                worldSizeInBlocks <= 0)
            {
                return 0f;
            }


            // ============================================================
            // World -> local coordinates
            // ============================================================

            float localX =
                globalX -
                worldMinX;


            float localZ =
                globalZ -
                worldMinZ;


            // ============================================================
            // Outside world = ocean
            // ============================================================

            if (localX < 0f ||
                localZ < 0f ||
                localX >
                    worldSizeInBlocks ||
                localZ >
                    worldSizeInBlocks)
            {
                return 0f;
            }


            // ============================================================
            // World position -> normalized map position
            // ============================================================

            float normalizedX =
                localX /
                worldSizeInBlocks;


            float normalizedZ =
                localZ /
                worldSizeInBlocks;


            // ============================================================
            // Normalized position -> macro sample position
            // ============================================================

            float mapX =
                normalizedX *
                (
                    resolution -
                    1
                );


            float mapZ =
                normalizedZ *
                (
                    resolution -
                    1
                );


            // ============================================================
            // Four surrounding samples
            // ============================================================

            int x0 =
                (int)math.floor(
                    mapX);


            int z0 =
                (int)math.floor(
                    mapZ);


            x0 =
                math.clamp(
                    x0,
                    0,
                    resolution - 1);


            z0 =
                math.clamp(
                    z0,
                    0,
                    resolution - 1);


            int x1 =
                math.min(
                    x0 + 1,
                    resolution - 1);


            int z1 =
                math.min(
                    z0 + 1,
                    resolution - 1);


            float tx =
                math.saturate(
                    mapX -
                    x0);


            float tz =
                math.saturate(
                    mapZ -
                    z0);


            // ============================================================
            // A ---------- B
            // |            |
            // |      *     |
            // |            |
            // C ---------- D
            // ============================================================

            float a =
                Get(
                    x0,
                    z0);


            float b =
                Get(
                    x1,
                    z0);


            float c =
                Get(
                    x0,
                    z1);


            float d =
                Get(
                    x1,
                    z1);


            // ============================================================
            // Bilinear interpolation
            // ============================================================

            float top =
                math.lerp(
                    a,
                    b,
                    tx);


            float bottom =
                math.lerp(
                    c,
                    d,
                    tx);


            return math.lerp(
                top,
                bottom,
                tz);
        }


        // ================================================================
        // Direct macro sample access
        // ================================================================

        public float Get(
            int x,
            int z)
        {
            if (!distances.IsCreated)
            {
                return 0f;
            }


            x =
                math.clamp(
                    x,
                    0,
                    resolution - 1);


            z =
                math.clamp(
                    z,
                    0,
                    resolution - 1);


            return distances[
                ToIndex(
                    x,
                    z,
                    resolution)];
        }


        // ================================================================
        // Coast detection
        // ================================================================

        private static bool TouchesOcean(
            NativeArray<byte> land,
            int resolution,
            int x,
            int z)
        {
            for (int dz = -1;
                 dz <= 1;
                 dz++)
            {
                for (int dx = -1;
                     dx <= 1;
                     dx++)
                {
                    if (dx == 0 &&
                        dz == 0)
                    {
                        continue;
                    }


                    int neighbourX =
                        x + dx;


                    int neighbourZ =
                        z + dz;


                    // ----------------------------------------------------
                    // Edge of macro map is ocean
                    // ----------------------------------------------------

                    if (neighbourX < 0 ||
                        neighbourX >=
                            resolution ||
                        neighbourZ < 0 ||
                        neighbourZ >=
                            resolution)
                    {
                        return true;
                    }


                    int neighbourIndex =
                        ToIndex(
                            neighbourX,
                            neighbourZ,
                            resolution);


                    if (land[
                            neighbourIndex] ==
                        0)
                    {
                        return true;
                    }
                }
            }


            return false;
        }


        // ================================================================
        // Distance propagation helper
        // ================================================================

        private static void TryNeighbour(
            NativeArray<byte> land,
            NativeArray<float> distances,
            int resolution,
            int x,
            int z,
            float movementCost,
            ref float currentDistance)
        {
            // ------------------------------------------------------------
            // Outside map
            // ------------------------------------------------------------

            if (x < 0 ||
                x >= resolution ||
                z < 0 ||
                z >= resolution)
            {
                return;
            }


            int index =
                ToIndex(
                    x,
                    z,
                    resolution);


            // ------------------------------------------------------------
            // Не поширюємо distance через океан.
            // ------------------------------------------------------------

            if (land[index] == 0)
            {
                return;
            }


            float neighbourDistance =
                distances[index];


            if (neighbourDistance ==
                float.MaxValue)
            {
                return;
            }


            float candidate =
                neighbourDistance +
                movementCost;


            currentDistance =
                math.min(
                    currentDistance,
                    candidate);
        }


        // ================================================================
        // Index
        // ================================================================

        private static int ToIndex(
            int x,
            int z,
            int resolution)
        {
            return
                x +
                z *
                resolution;
        }


        // ================================================================
        // Lifetime
        // ================================================================

        public void Dispose()
        {
            if (distances.IsCreated)
            {
                distances.Dispose();
            }
        }
    }
}