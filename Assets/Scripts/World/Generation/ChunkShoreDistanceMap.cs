using System;
using Game.World.Chunks;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation.Terrain
{
    public struct ChunkShoreDistanceMap : IDisposable
    {
        private const float DiagonalCost = 1.41421356f;
        private const float FarDistance = 100000f;

        private NativeArray<float> distances;

        public bool IsCreated => distances.IsCreated;

        public static ChunkShoreDistanceMap Create(
            int2 chunkCoordinate,
            int2 macroOrigin,
            int maxDistance,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings,
            Allocator allocator)
        {
            maxDistance = math.max(
                1,
                maxDistance);

            int margin =
                maxDistance + 1;

            int expandedWidth =
                ChunkSettings.SizeX +
                margin * 2;

            int expandedDepth =
                ChunkSettings.SizeZ +
                margin * 2;

            int expandedLength =
                expandedWidth *
                expandedDepth;

            // ============================================================
            // Two distance fields
            //
            // distanceToWater:
            //      water = 0
            //      land  = infinity
            //
            // distanceToLand:
            //      land  = 0
            //      water = infinity
            //
            // Після distance transform вони дадуть нам signed distance.
            // ============================================================

            var distanceToWater =
                new NativeArray<float>(
                    expandedLength,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);

            var distanceToLand =
                new NativeArray<float>(
                    expandedLength,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);

            FillFields(
                distanceToWater,
                distanceToLand,
                expandedWidth,
                expandedDepth,
                margin,
                chunkCoordinate,
                macroOrigin,
                heightMap,
                worldSeed,
                settings);

            // ============================================================
            // Distance to water
            // ============================================================

            ForwardPass(
                distanceToWater,
                expandedWidth,
                expandedDepth);

            BackwardPass(
                distanceToWater,
                expandedWidth,
                expandedDepth);

            // ============================================================
            // Distance to land
            // ============================================================

            ForwardPass(
                distanceToLand,
                expandedWidth,
                expandedDepth);

            BackwardPass(
                distanceToLand,
                expandedWidth,
                expandedDepth);

            // ============================================================
            // Extract central 16×16 chunk
            // ============================================================

            var result =
                new NativeArray<float>(
                    ChunkSettings.SizeX *
                    ChunkSettings.SizeZ,
                    allocator,
                    NativeArrayOptions.UninitializedMemory);

            for (int z = 0;
                 z < ChunkSettings.SizeZ;
                 z++)
            {
                for (int x = 0;
                     x < ChunkSettings.SizeX;
                     x++)
                {
                    int expandedX =
                        x + margin;

                    int expandedZ =
                        z + margin;

                    int expandedIndex =
                        ToIndex(
                            expandedX,
                            expandedZ,
                            expandedWidth);

                    float waterDistance =
                        distanceToWater[
                            expandedIndex];

                    float landDistance =
                        distanceToLand[
                            expandedIndex];

                    // Якщо distanceToWater == 0,
                    // сама ця колонка є водою.
                    bool isWater =
                        waterDistance <= 0f;

                    float signedDistance;

                    if (isWater)
                    {
                        // Water:
                        //
                        // -1 = перший водяний блок біля суші
                        // -2 = другий
                        // ...
                        signedDistance =
                            -math.min(
                                landDistance,
                                maxDistance);
                    }
                    else
                    {
                        // Land:
                        //
                        // +1 = перший сухий блок біля води
                        // +2 = другий
                        // ...
                        signedDistance =
                            math.min(
                                waterDistance,
                                maxDistance);
                    }

                    result[
                        ToIndex(
                            x,
                            z,
                            ChunkSettings.SizeX)] =
                        signedDistance;
                }
            }

            distanceToLand.Dispose();
            distanceToWater.Dispose();

            return new ChunkShoreDistanceMap
            {
                distances = result
            };
        }

        public float Get(
            int x,
            int z)
        {
            if (!distances.IsCreated)
                return 0f;

            if (x < 0 ||
                x >= ChunkSettings.SizeX ||
                z < 0 ||
                z >= ChunkSettings.SizeZ)
            {
                return 0f;
            }

            return distances[
                ToIndex(
                    x,
                    z,
                    ChunkSettings.SizeX)];
        }

        public void Dispose()
        {
            if (distances.IsCreated)
                distances.Dispose();

            distances = default;
        }

        // ================================================================
        // Initial fields
        // ================================================================

        private static void FillFields(
            NativeArray<float> distanceToWater,
            NativeArray<float> distanceToLand,
            int width,
            int depth,
            int margin,
            int2 chunkCoordinate,
            int2 macroOrigin,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            int chunkWorldX =
                chunkCoordinate.x *
                ChunkSettings.SizeX;

            int chunkWorldZ =
                chunkCoordinate.y *
                ChunkSettings.SizeZ;

            for (int z = 0;
                 z < depth;
                 z++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int globalX =
                        chunkWorldX +
                        x -
                        margin;

                    int globalZ =
                        chunkWorldZ +
                        z -
                        margin;

                    int worldX =
                        macroOrigin.x +
                        globalX;

                    int worldZ =
                        macroOrigin.y +
                        globalZ;

                    bool isWater;

                    if (!WorldSamplingUtility.IsInsideWorld(
                            worldX,
                            worldZ,
                            heightMap))
                    {
                        isWater = true;
                    }
                    else
                    {
                        isWater =
                            WorldTerrainHeight.IsWaterColumn(
                                worldX,
                                worldZ,
                                heightMap,
                                worldSeed,
                                settings);
                    }

                    int index =
                        ToIndex(
                            x,
                            z,
                            width);

                    distanceToWater[index] =
                        isWater
                            ? 0f
                            : FarDistance;

                    distanceToLand[index] =
                        isWater
                            ? FarDistance
                            : 0f;
                }
            }
        }

        // ================================================================
        // Distance transform
        // ================================================================

        private static void ForwardPass(
            NativeArray<float> field,
            int width,
            int depth)
        {
            for (int z = 0;
                 z < depth;
                 z++)
            {
                for (int x = 0;
                     x < width;
                     x++)
                {
                    int index =
                        ToIndex(
                            x,
                            z,
                            width);

                    float distance =
                        field[index];

                    if (distance <= 0f)
                        continue;

                    // Left
                    if (x > 0)
                    {
                        distance = math.min(
                            distance,
                            field[
                                ToIndex(
                                    x - 1,
                                    z,
                                    width)] +
                            1f);
                    }

                    // Back
                    if (z > 0)
                    {
                        distance = math.min(
                            distance,
                            field[
                                ToIndex(
                                    x,
                                    z - 1,
                                    width)] +
                            1f);

                        // Back-left
                        if (x > 0)
                        {
                            distance = math.min(
                                distance,
                                field[
                                    ToIndex(
                                        x - 1,
                                        z - 1,
                                        width)] +
                                DiagonalCost);
                        }

                        // Back-right
                        if (x < width - 1)
                        {
                            distance = math.min(
                                distance,
                                field[
                                    ToIndex(
                                        x + 1,
                                        z - 1,
                                        width)] +
                                DiagonalCost);
                        }
                    }

                    field[index] =
                        distance;
                }
            }
        }

        private static void BackwardPass(
            NativeArray<float> field,
            int width,
            int depth)
        {
            for (int z = depth - 1;
                 z >= 0;
                 z--)
            {
                for (int x = width - 1;
                     x >= 0;
                     x--)
                {
                    int index =
                        ToIndex(
                            x,
                            z,
                            width);

                    float distance =
                        field[index];

                    if (distance <= 0f)
                        continue;

                    // Right
                    if (x < width - 1)
                    {
                        distance = math.min(
                            distance,
                            field[
                                ToIndex(
                                    x + 1,
                                    z,
                                    width)] +
                            1f);
                    }

                    // Forward
                    if (z < depth - 1)
                    {
                        distance = math.min(
                            distance,
                            field[
                                ToIndex(
                                    x,
                                    z + 1,
                                    width)] +
                            1f);

                        // Forward-left
                        if (x > 0)
                        {
                            distance = math.min(
                                distance,
                                field[
                                    ToIndex(
                                        x - 1,
                                        z + 1,
                                        width)] +
                                DiagonalCost);
                        }

                        // Forward-right
                        if (x < width - 1)
                        {
                            distance = math.min(
                                distance,
                                field[
                                    ToIndex(
                                        x + 1,
                                        z + 1,
                                        width)] +
                                DiagonalCost);
                        }
                    }

                    field[index] =
                        distance;
                }
            }
        }

        private static int ToIndex(
            int x,
            int z,
            int width)
        {
            return x +
                   z * width;
        }
    }
}