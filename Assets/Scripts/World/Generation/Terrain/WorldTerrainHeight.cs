using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Generation.Terrain
{
    public static class WorldTerrainHeight
    {
        // ================================================================
        // Height
        // ================================================================

        public static int Sample(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            float macroElevation = heightMap.Sample(
                worldX,
                worldZ);

            return Sample(
                worldX,
                worldZ,
                macroElevation,
                heightMap,
                worldSeed,
                settings);
        }

        public static int Sample(
            int worldX,
            int worldZ,
            float macroElevation,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            if (!WorldSamplingUtility.IsInsideWorld(
                    worldX,
                    worldZ,
                    heightMap))
            {
                return settings.DeepOceanHeight;
            }

            float macroHeight;

            if (macroElevation <= settings.MacroSeaLevel)
            {
                float oceanT = math.saturate(
                    macroElevation /
                    settings.MacroSeaLevel);

                macroHeight = math.lerp(
                    settings.DeepOceanHeight,
                    settings.SeaLevelHeight,
                    oceanT);
            }
            else
            {
                float landT = math.saturate(
                    (macroElevation - settings.MacroSeaLevel) /
                    (1f - settings.MacroSeaLevel));

                macroHeight = math.lerp(
                    settings.SeaLevelHeight,
                    settings.HighLandHeight,
                    landT);
            }

            float detail = WorldTerrainNoise.SampleHeightOffset(
                worldX,
                worldZ,
                worldSeed);

            float finalHeight =
                macroHeight +
                detail;

            return math.clamp(
                (int)math.round(finalHeight),
                1,
                ChunkSettings.SizeY - 1);
        }

        // ================================================================
        // Slope
        // ================================================================

        public static int GetLocalSlope(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            int center = Sample(
                worldX,
                worldZ,
                heightMap,
                worldSeed,
                settings);

            int right = Sample(
                worldX + 1,
                worldZ,
                heightMap,
                worldSeed,
                settings);

            int left = Sample(
                worldX - 1,
                worldZ,
                heightMap,
                worldSeed,
                settings);

            int forward = Sample(
                worldX,
                worldZ + 1,
                heightMap,
                worldSeed,
                settings);

            int back = Sample(
                worldX,
                worldZ - 1,
                heightMap,
                worldSeed,
                settings);

            int slopeX = math.max(
                math.abs(center - right),
                math.abs(center - left));

            int slopeZ = math.max(
                math.abs(center - forward),
                math.abs(center - back));

            return math.max(
                slopeX,
                slopeZ);
        }

        // ================================================================
        // Water
        // ================================================================

        public static bool IsWaterColumn(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap,
            uint worldSeed,
            in WorldGenerationSettingsComponent settings)
        {
            int terrainHeight = Sample(
                worldX,
                worldZ,
                heightMap,
                worldSeed,
                settings);

            return terrainHeight <
                   settings.SeaLevelHeight;
        }
    }
}