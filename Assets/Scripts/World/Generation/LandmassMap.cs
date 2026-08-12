using System;
using Unity.Collections;

namespace Game.World.Generation
{
    public struct LandmassMap :
        IDisposable
    {
        // ================================================================
        // Data
        //
        // 0 = ocean
        // 1+ = landmass ID
        // ================================================================

        private NativeArray<int> landmassIds;

        private int resolution;

        private int mainLandmassId;

        private int mainLandmassSize;

        private int landmassCount;


        public bool IsCreated =>
            landmassIds.IsCreated;


        public int Resolution =>
            resolution;


        public int MainLandmassId =>
            mainLandmassId;


        public int MainLandmassSize =>
            mainLandmassSize;


        public int LandmassCount =>
            landmassCount;


        // ================================================================
        // Creation
        // ================================================================

        public static LandmassMap Create(
            WorldHeightMap heightMap,
            float seaLevel,
            Allocator allocator)
        {
            int resolution =
                heightMap.Resolution;


            int cellCount =
                resolution *
                resolution;


            var ids =
                new NativeArray<int>(
                    cellCount,
                    allocator,
                    NativeArrayOptions.ClearMemory);


            // Temporary flood-fill queue.
            //
            // Максимум у черзі може бути весь macro map.
            var queue =
                new NativeArray<int>(
                    cellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);


            int currentLandmassId =
                0;


            int mainLandmassId =
                0;


            int mainLandmassSize =
                0;


            // ============================================================
            // Find connected landmasses
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


                    // Already assigned.
                    if (ids[index] != 0)
                    {
                        continue;
                    }


                    // Ocean.
                    if (heightMap.Get(
                            x,
                            z) <
                        seaLevel)
                    {
                        continue;
                    }


                    // ====================================================
                    // New landmass
                    // ====================================================

                    currentLandmassId++;


                    int head =
                        0;


                    int tail =
                        0;


                    queue[tail++] =
                        index;


                    ids[index] =
                        currentLandmassId;


                    int currentSize =
                        0;


                    // ====================================================
                    // Flood fill
                    //
                    // Використовуємо 4-connected topology:
                    //
                    //     N
                    //   W X E
                    //     S
                    //
                    // Два шматки суші, які торкаються лише кутами,
                    // не вважаємо одним континентом.
                    // ====================================================

                    while (head <
                           tail)
                    {
                        int currentIndex =
                            queue[head++];


                        int currentX =
                            currentIndex %
                            resolution;


                        int currentZ =
                            currentIndex /
                            resolution;


                        currentSize++;


                        TryAddLand(
                            heightMap,
                            seaLevel,
                            ids,
                            queue,
                            resolution,
                            currentLandmassId,
                            currentX - 1,
                            currentZ,
                            ref tail);


                        TryAddLand(
                            heightMap,
                            seaLevel,
                            ids,
                            queue,
                            resolution,
                            currentLandmassId,
                            currentX + 1,
                            currentZ,
                            ref tail);


                        TryAddLand(
                            heightMap,
                            seaLevel,
                            ids,
                            queue,
                            resolution,
                            currentLandmassId,
                            currentX,
                            currentZ - 1,
                            ref tail);


                        TryAddLand(
                            heightMap,
                            seaLevel,
                            ids,
                            queue,
                            resolution,
                            currentLandmassId,
                            currentX,
                            currentZ + 1,
                            ref tail);
                    }


                    // ====================================================
                    // Largest landmass = main continent
                    // ====================================================

                    if (currentSize >
                        mainLandmassSize)
                    {
                        mainLandmassSize =
                            currentSize;


                        mainLandmassId =
                            currentLandmassId;
                    }
                }
            }


            // ============================================================
            // Cleanup
            // ================================================================

            queue.Dispose();


            return new LandmassMap
            {
                landmassIds =
                    ids,

                resolution =
                    resolution,

                mainLandmassId =
                    mainLandmassId,

                mainLandmassSize =
                    mainLandmassSize,

                landmassCount =
                    currentLandmassId
            };
        }


        // ================================================================
        // Queries
        // ================================================================

        public int GetLandmassId(
            int x,
            int z)
        {
            if (!landmassIds.IsCreated)
            {
                return 0;
            }


            if (x < 0 ||
                x >= resolution ||
                z < 0 ||
                z >= resolution)
            {
                return 0;
            }


            return landmassIds[
                ToIndex(
                    x,
                    z,
                    resolution)];
        }


        public bool IsLand(
            int x,
            int z)
        {
            return
                GetLandmassId(
                    x,
                    z) !=
                0;
        }


        public bool IsMainland(
            int x,
            int z)
        {
            if (mainLandmassId ==
                0)
            {
                return false;
            }


            return
                GetLandmassId(
                    x,
                    z) ==
                mainLandmassId;
        }


        // ================================================================
        // Flood fill
        // ================================================================

        private static void TryAddLand(
            WorldHeightMap heightMap,
            float seaLevel,
            NativeArray<int> ids,
            NativeArray<int> queue,
            int resolution,
            int landmassId,
            int x,
            int z,
            ref int tail)
        {
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


            // Already visited.
            if (ids[index] !=
                0)
            {
                return;
            }


            // Ocean.
            if (heightMap.Get(
                    x,
                    z) <
                seaLevel)
            {
                return;
            }


            // Mark BEFORE adding to queue,
            // щоб cell не могла потрапити туди кілька разів.
            ids[index] =
                landmassId;


            queue[tail++] =
                index;
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
            if (landmassIds.IsCreated)
            {
                landmassIds.Dispose();
            }
        }
    }
}