using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Game.World.Generation
{
    public struct WorldBiomeMap : IDisposable
    {
        private NativeArray<byte> biomes;

        private int resolution;
        private int worldMinX;
        private int worldMinZ;
        private int worldSizeInBlocks;

        public bool IsCreated => biomes.IsCreated;
        public int Resolution => resolution;

        public static WorldBiomeMap Create(
            WorldHeightMap heightMap,
            CoastDistanceMap coastDistanceMap,
            WorldProgressionMap progressionMap,
            LandmassMap landmassMap,
            WorldAnchors anchors,
            float seaLevel,
            uint seed,
            Allocator allocator)
        {
            int resolution = heightMap.Resolution;

            var result = new NativeArray<byte>(
                resolution * resolution,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            WorldBiomeSamplingContext context =
                WorldBiomeSampler.CreateContext(anchors, seaLevel, seed);

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int index = x + z * resolution;

                    float elevation = heightMap.Get(x, z);
                    float coastDistance = coastDistanceMap.Get(x, z);
                    bool isMainland = landmassMap.IsMainland(x, z);

                    float2 uv = MapToUv(x, z, resolution);

                    WorldBiome biome = WorldBiomeSampler.Sample(
                        uv,
                        elevation,
                        coastDistance,
                        isMainland,
                        context);

                    result[index] = (byte)biome;
                }
            }

            return new WorldBiomeMap
            {
                biomes = result,
                resolution = resolution,
                worldMinX = heightMap.WorldMinX,
                worldMinZ = heightMap.WorldMinZ,
                worldSizeInBlocks = heightMap.WorldSizeInBlocks
            };
        }

        public WorldBiome Sample(int globalX, int globalZ)
        {
            return Sample((float)globalX, (float)globalZ);
        }

        public WorldBiome Sample(float globalX, float globalZ)
        {
            if (!biomes.IsCreated || worldSizeInBlocks <= 0)
                return WorldBiome.Ocean;

            float localX = globalX - worldMinX;
            float localZ = globalZ - worldMinZ;

            if (localX < 0f ||
                localZ < 0f ||
                localX > worldSizeInBlocks ||
                localZ > worldSizeInBlocks)
            {
                return WorldBiome.Ocean;
            }

            float normalizedX = localX / worldSizeInBlocks;
            float normalizedZ = localZ / worldSizeInBlocks;

            int x = math.clamp(
                (int)math.round(normalizedX * (resolution - 1)),
                0,
                resolution - 1);

            int z = math.clamp(
                (int)math.round(normalizedZ * (resolution - 1)),
                0,
                resolution - 1);

            return Get(x, z);
        }

        public WorldBiome Get(int x, int z)
        {
            if (!biomes.IsCreated)
                return WorldBiome.Ocean;

            if (x < 0 || x >= resolution || z < 0 || z >= resolution)
                return WorldBiome.Ocean;

            return (WorldBiome)biomes[x + z * resolution];
        }

        private static float2 MapToUv(int x, int z, int resolution)
        {
            float inverse = 1f / math.max(1, resolution - 1);
            return new float2(x * inverse, z * inverse);
        }

        public void Dispose()
        {
            if (biomes.IsCreated)
                biomes.Dispose();
        }
    }
}