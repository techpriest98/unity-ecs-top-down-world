using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct WorldAnchors
    {
        public float2 StartUv;

        public float2 FinalUv;


        public int2 StartMapPosition;

        public int2 FinalMapPosition;
    }


    public static class WorldAnchorGenerator
    {
        // ================================================================
        // Final anchor
        // ================================================================

        private const float MinimumFinalCoastDistance =
            0.45f;


        private const float FinalCoastWeight =
            0.60f;


        private const float FinalElevationWeight =
            0.35f;


        private const float FinalRandomWeight =
            0.05f;


        // ================================================================
        // Start anchor
        // ================================================================

        private const float CoastalThreshold =
            0.001f;


        // Стартова точка не може знаходитися
        // надто близько до фізичної межі світу.
        //
        // 8 samples * 512 blocks =
        // приблизно 4096 блоків.

        private const int MinimumStartEdgeMargin =
            8;


        private const float StartDistanceWeight =
            0.55f;


        private const float StartLandSupportWeight =
            0.25f;


        private const float StartElevationWeight =
            0.15f;


        private const float StartRandomWeight =
            0.05f;


        private const int LandSupportRadius =
            2;


        // ================================================================
        // Generate
        // ================================================================

        public static WorldAnchors Generate(
            WorldHeightMap heightMap,
            CoastDistanceMap coastDistanceMap,
            LandmassMap landmassMap,
            float seaLevel,
            uint seed)
        {
            int2 finalPosition =
                FindFinalAnchor(
                    heightMap,
                    coastDistanceMap,
                    landmassMap,
                    seaLevel,
                    seed);


            int2 startPosition =
                FindStartAnchor(
                    heightMap,
                    coastDistanceMap,
                    landmassMap,
                    seaLevel,
                    seed,
                    finalPosition);


            int resolution =
                heightMap.Resolution;


            return new WorldAnchors
            {
                StartMapPosition =
                    startPosition,

                FinalMapPosition =
                    finalPosition,

                StartUv =
                    MapToUv(
                        startPosition,
                        resolution),

                FinalUv =
                    MapToUv(
                        finalPosition,
                        resolution)
            };
        }


        // ================================================================
        // Final anchor
        // ================================================================

        private static int2 FindFinalAnchor(
            WorldHeightMap heightMap,
            CoastDistanceMap coastDistanceMap,
            LandmassMap landmassMap,
            float seaLevel,
            uint seed)
        {
            int resolution =
                heightMap.Resolution;


            float bestScore =
                float.MinValue;


            int2 bestPosition =
                new int2(
                    resolution / 2,
                    resolution / 2);


            bool foundPreferredCandidate =
                false;


            // ============================================================
            // Preferred candidates
            // ================================================================

            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    // ----------------------------------------------------
                    // Main continent only
                    // ----------------------------------------------------

                    if (!landmassMap.IsMainland(
                            x,
                            z))
                    {
                        continue;
                    }


                    float elevation =
                        heightMap.Get(
                            x,
                            z);


                    float coastDistance =
                        coastDistanceMap.Get(
                            x,
                            z);


                    // ----------------------------------------------------
                    // Final should be deep inland
                    // ----------------------------------------------------

                    if (coastDistance <
                        MinimumFinalCoastDistance)
                    {
                        continue;
                    }


                    float score =
                        GetFinalScore(
                            elevation,
                            coastDistance,
                            seaLevel,
                            seed,
                            x,
                            z);


                    if (score <=
                        bestScore)
                    {
                        continue;
                    }


                    bestScore =
                        score;


                    bestPosition =
                        new int2(
                            x,
                            z);


                    foundPreferredCandidate =
                        true;
                }
            }


            if (foundPreferredCandidate)
            {
                return bestPosition;
            }


            // ============================================================
            // Fallback
            //
            // Any point on main continent.
            // ================================================================

            bestScore =
                float.MinValue;


            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    if (!landmassMap.IsMainland(
                            x,
                            z))
                    {
                        continue;
                    }


                    float elevation =
                        heightMap.Get(
                            x,
                            z);


                    float coastDistance =
                        coastDistanceMap.Get(
                            x,
                            z);


                    float score =
                        GetFinalScore(
                            elevation,
                            coastDistance,
                            seaLevel,
                            seed,
                            x,
                            z);


                    if (score <=
                        bestScore)
                    {
                        continue;
                    }


                    bestScore =
                        score;


                    bestPosition =
                        new int2(
                            x,
                            z);
                }
            }


            return bestPosition;
        }


        // ================================================================
        // Final score
        // ================================================================

        private static float GetFinalScore(
            float elevation,
            float coastDistance,
            float seaLevel,
            uint seed,
            int x,
            int z)
        {
            float normalizedElevation =
                math.saturate(
                    (
                        elevation -
                        seaLevel
                    ) /
                    math.max(
                        0.0001f,
                        1f -
                        seaLevel));


            float random =
                SampleDeterministicRandom(
                    seed,
                    x,
                    z,
                    0x8DA6B343u);


            return
                coastDistance *
                FinalCoastWeight +

                normalizedElevation *
                FinalElevationWeight +

                random *
                FinalRandomWeight;
        }


        // ================================================================
        // Start anchor
        // ================================================================

        private static int2 FindStartAnchor(
            WorldHeightMap heightMap,
            CoastDistanceMap coastDistanceMap,
            LandmassMap landmassMap,
            float seaLevel,
            uint seed,
            int2 finalPosition)
        {
            int resolution =
                heightMap.Resolution;


            float2 finalUv =
                MapToUv(
                    finalPosition,
                    resolution);


            float bestScore =
                float.MinValue;


            int2 bestPosition =
                finalPosition;


            bool foundCoast =
                false;


            // ============================================================
            // Preferred start:
            //
            // - main continent
            // - coastline
            // - safe distance from world edge
            // ================================================================

            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    // ----------------------------------------------------
                    // Main continent only
                    // ----------------------------------------------------

                    if (!landmassMap.IsMainland(
                            x,
                            z))
                    {
                        continue;
                    }


                    // ----------------------------------------------------
                    // Physical world edge safety
                    // ----------------------------------------------------

                    if (!IsInsideStartSafeArea(
                            x,
                            z,
                            resolution))
                    {
                        continue;
                    }


                    float elevation =
                        heightMap.Get(
                            x,
                            z);


                    float coastDistance =
                        coastDistanceMap.Get(
                            x,
                            z);


                    // ----------------------------------------------------
                    // Coastline only
                    // ----------------------------------------------------

                    if (coastDistance >
                        CoastalThreshold)
                    {
                        continue;
                    }


                    float2 uv =
                        MapToUv(
                            new int2(
                                x,
                                z),
                            resolution);


                    // ====================================================
                    // Distance from Final
                    // ====================================================

                    float distanceFromFinal =
                        math.distance(
                            uv,
                            finalUv);


                    distanceFromFinal =
                        math.saturate(
                            distanceFromFinal /
                            1.41421356237f);


                    // ====================================================
                    // Local mainland support
                    // ====================================================

                    float landSupport =
                        GetMainlandSupport(
                            landmassMap,
                            x,
                            z);


                    // ====================================================
                    // Prefer relatively low coastal terrain
                    // ====================================================

                    float normalizedElevation =
                        math.saturate(
                            (
                                elevation -
                                seaLevel
                            ) /
                            math.max(
                                0.0001f,
                                1f -
                                seaLevel));


                    float elevationSuitability =
                        1f -
                        normalizedElevation;


                    // ====================================================
                    // Deterministic variation
                    // ====================================================

                    float random =
                        SampleDeterministicRandom(
                            seed,
                            x,
                            z,
                            0xC2B2AE35u);


                    float score =
                        distanceFromFinal *
                        StartDistanceWeight +

                        landSupport *
                        StartLandSupportWeight +

                        elevationSuitability *
                        StartElevationWeight +

                        random *
                        StartRandomWeight;


                    if (score <=
                        bestScore)
                    {
                        continue;
                    }


                    bestScore =
                        score;


                    bestPosition =
                        new int2(
                            x,
                            z);


                    foundCoast =
                        true;
                }
            }


            if (foundCoast)
            {
                return bestPosition;
            }


            // ============================================================
            // Safe-area fallback
            //
            // Якщо через дуже дивну форму світу
            // не знайшли coastal cell,
            // шукаємо mainland cell у safe area,
            // максимально далеку від Final.
            // ================================================================

            bestScore =
                float.MinValue;


            bool foundSafeFallback =
                false;


            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    if (!landmassMap.IsMainland(
                            x,
                            z))
                    {
                        continue;
                    }


                    if (!IsInsideStartSafeArea(
                            x,
                            z,
                            resolution))
                    {
                        continue;
                    }


                    float2 uv =
                        MapToUv(
                            new int2(
                                x,
                                z),
                            resolution);


                    float distanceFromFinal =
                        math.distance(
                            uv,
                            finalUv);


                    // Prefer being close to coast,
                    // even in fallback mode.

                    float coastDistance =
                        coastDistanceMap.Get(
                            x,
                            z);


                    float coastSuitability =
                        1f -
                        coastDistance;


                    float score =
                        distanceFromFinal *
                        0.75f +

                        coastSuitability *
                        0.25f;


                    if (score <=
                        bestScore)
                    {
                        continue;
                    }


                    bestScore =
                        score;


                    bestPosition =
                        new int2(
                            x,
                            z);


                    foundSafeFallback =
                        true;
                }
            }


            if (foundSafeFallback)
            {
                return bestPosition;
            }


            // ============================================================
            // Emergency fallback
            //
            // Це вже крайній випадок:
            // беремо будь-яку mainland cell.
            // ================================================================

            bestScore =
                float.MinValue;


            for (int z = 0;
                 z < resolution;
                 z++)
            {
                for (int x = 0;
                     x < resolution;
                     x++)
                {
                    if (!landmassMap.IsMainland(
                            x,
                            z))
                    {
                        continue;
                    }


                    float2 uv =
                        MapToUv(
                            new int2(
                                x,
                                z),
                            resolution);


                    float score =
                        math.distance(
                            uv,
                            finalUv);


                    if (score <=
                        bestScore)
                    {
                        continue;
                    }


                    bestScore =
                        score;


                    bestPosition =
                        new int2(
                            x,
                            z);
                }
            }


            return bestPosition;
        }


        // ================================================================
        // Start safe area
        // ================================================================

        private static bool IsInsideStartSafeArea(
            int x,
            int z,
            int resolution)
        {
            return
                x >=
                    MinimumStartEdgeMargin &&

                z >=
                    MinimumStartEdgeMargin &&

                x <
                    resolution -
                    MinimumStartEdgeMargin &&

                z <
                    resolution -
                    MinimumStartEdgeMargin;
        }


        // ================================================================
        // Mainland support
        // ================================================================

        private static float GetMainlandSupport(
            LandmassMap landmassMap,
            int centerX,
            int centerZ)
        {
            int resolution =
                landmassMap.Resolution;


            int mainlandCount =
                0;


            int sampleCount =
                0;


            for (int dz =
                     -LandSupportRadius;
                 dz <=
                     LandSupportRadius;
                 dz++)
            {
                for (int dx =
                         -LandSupportRadius;
                     dx <=
                         LandSupportRadius;
                     dx++)
                {
                    int x =
                        centerX +
                        dx;


                    int z =
                        centerZ +
                        dz;


                    if (x < 0 ||
                        x >= resolution ||
                        z < 0 ||
                        z >= resolution)
                    {
                        continue;
                    }


                    sampleCount++;


                    if (landmassMap.IsMainland(
                            x,
                            z))
                    {
                        mainlandCount++;
                    }
                }
            }


            if (sampleCount ==
                0)
            {
                return 0f;
            }


            return
                mainlandCount /
                (float)
                sampleCount;
        }


        // ================================================================
        // Map -> UV
        // ================================================================

        private static float2 MapToUv(
            int2 position,
            int resolution)
        {
            float inverse =
                1f /
                math.max(
                    1,
                    resolution -
                    1);


            return new float2(
                position.x *
                inverse,

                position.y *
                inverse);
        }


        // ================================================================
        // Deterministic random
        // ================================================================

        private static float SampleDeterministicRandom(
            uint seed,
            int x,
            int z,
            uint salt)
        {
            uint hash =
                math.hash(
                    new uint4(
                        seed,
                        (uint)x,
                        (uint)z,
                        salt));


            return
                (
                    hash &
                    0x00FFFFFFu
                ) /
                16777215f;
        }
    }
}