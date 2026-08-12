using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct WorldProgressionMap :
        IDisposable
    {
        // ================================================================
        // Data
        //
        // Progression now means:
        //
        // 0 = coastline
        // 1 = deepest inland
        //
        // It is NOT based on Start -> Final direction.
        // ================================================================

        private NativeArray<float>
            values;


        private int resolution;

        private int worldMinX;

        private int worldMinZ;

        private int worldSizeInBlocks;


        public bool IsCreated =>
            values.IsCreated;


        public int Resolution =>
            resolution;


        // ================================================================
        // Creation
        // ================================================================

        public static WorldProgressionMap Create(
            WorldHeightMap heightMap,
            CoastDistanceMap coastDistanceMap,
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


            // ============================================================
            // Generate
            // ================================================================

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


                    // ====================================================
                    // Ocean
                    // ====================================================

                    if (elevation <
                        seaLevel)
                    {
                        result[index] =
                            0f;

                        continue;
                    }


                    // ====================================================
                    // Progression = real inland distance
                    //
                    // CoastDistanceMap already gives:
                    //
                    // 0 = coast
                    // 1 = deepest inland
                    //
                    // This is exactly our world progression axis.
                    // ====================================================

                    float progression =
                        coastDistanceMap.Get(
                            x,
                            z);


                    result[index] =
                        math.saturate(
                            progression);
                }
            }


            return new WorldProgressionMap
            {
                values =
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
            if (!values.IsCreated ||
                worldSizeInBlocks <= 0)
            {
                return 0f;
            }


            // ============================================================
            // World -> local
            // ================================================================

            float localX =
                globalX -
                worldMinX;


            float localZ =
                globalZ -
                worldMinZ;


            // ============================================================
            // Outside world
            // ================================================================

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
            // Normalized map position
            // ================================================================

            float normalizedX =
                localX /
                worldSizeInBlocks;


            float normalizedZ =
                localZ /
                worldSizeInBlocks;


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
            // ================================================================

            int x0 =
                math.clamp(
                    (int)math.floor(
                        mapX),
                    0,
                    resolution - 1);


            int z0 =
                math.clamp(
                    (int)math.floor(
                        mapZ),
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
            // ================================================================

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
        // Direct macro access
        // ================================================================

        public float Get(
            int x,
            int z)
        {
            if (!values.IsCreated)
            {
                return 0f;
            }


            if (x < 0 ||
                x >= resolution ||
                z < 0 ||
                z >= resolution)
            {
                return 0f;
            }


            return values[
                ToIndex(
                    x,
                    z,
                    resolution)];
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
            if (values.IsCreated)
            {
                values.Dispose();
            }
        }
    }
}