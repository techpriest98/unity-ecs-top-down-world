using Unity.Mathematics;

namespace Game.World.Generation.Terrain
{
    public static class ShoreDistanceSampler
    {
        private const float DiagonalDistance = 1.41421356f;

        public static float SampleDistanceToWater(
            int worldX,
            int worldZ,
            int maxDistance,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            if (WorldTerrainHeight.IsWaterColumn(
                    worldX,
                    worldZ,
                    heightMap,
                    worldSeed,
                    settings))
            {
                return 0f;
            }

            float nearestDistance = maxDistance;

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    1, 0,
                    1f,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    -1, 0,
                    1f,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    0, 1,
                    1f,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    0, -1,
                    1f,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    1, 1,
                    DiagonalDistance,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    -1, 1,
                    DiagonalDistance,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    1, -1,
                    DiagonalDistance,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            nearestDistance = math.min(
                nearestDistance,
                ProbeDirection(
                    worldX, worldZ,
                    -1, -1,
                    DiagonalDistance,
                    maxDistance,
                    heightMap,
                    worldSeed,
                    settings));

            return nearestDistance;
        }

        private static float ProbeDirection(
            int worldX,
            int worldZ,
            int directionX,
            int directionZ,
            float stepDistance,
            int maxDistance,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            for (int step = 1; step <= maxDistance; step++)
            {
                int sampleX = worldX + directionX * step;
                int sampleZ = worldZ + directionZ * step;

                if (!WorldSamplingUtility.IsInsideWorld(
                        sampleX,
                        sampleZ,
                        heightMap))
                {
                    return step * stepDistance;
                }

                if (!WorldTerrainHeight.IsWaterColumn(
                        sampleX,
                        sampleZ,
                        heightMap,
                        worldSeed,
                        settings))
                {
                    continue;
                }

                return step * stepDistance;
            }

            return maxDistance;
        }
    }
}