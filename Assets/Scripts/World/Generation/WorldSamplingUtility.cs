using Unity.Mathematics;

namespace Game.World.Generation
{
    public static class WorldSamplingUtility
    {
        public static bool IsInsideWorld(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap)
        {
            int maxX = heightMap.WorldMinX + heightMap.WorldSizeInBlocks;
            int maxZ = heightMap.WorldMinZ + heightMap.WorldSizeInBlocks;

            return worldX >= heightMap.WorldMinX &&
                   worldX <= maxX &&
                   worldZ >= heightMap.WorldMinZ &&
                   worldZ <= maxZ;
        }

        public static float2 WorldToUv(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap)
        {
            float inverseSize = 1f / math.max(
                1,
                heightMap.WorldSizeInBlocks);

            return new float2(
                (worldX - heightMap.WorldMinX) * inverseSize,
                (worldZ - heightMap.WorldMinZ) * inverseSize);
        }

        public static int2 UvToWorld(
            float2 uv,
            WorldHeightMap heightMap)
        {
            int worldX = (int)math.round(
                heightMap.WorldMinX +
                uv.x * heightMap.WorldSizeInBlocks);

            int worldZ = (int)math.round(
                heightMap.WorldMinZ +
                uv.y * heightMap.WorldSizeInBlocks);

            return new int2(worldX, worldZ);
        }

        public static bool IsMainland(
            int worldX,
            int worldZ,
            WorldHeightMap heightMap,
            LandmassMap landmassMap)
        {
            if (!IsInsideWorld(worldX, worldZ, heightMap))
                return false;

            float2 uv = WorldToUv(
                worldX,
                worldZ,
                heightMap);

            int mapX = math.clamp(
                (int)math.round(
                    uv.x * (landmassMap.Resolution - 1)),
                0,
                landmassMap.Resolution - 1);

            int mapZ = math.clamp(
                (int)math.round(
                    uv.y * (landmassMap.Resolution - 1)),
                0,
                landmassMap.Resolution - 1);

            return landmassMap.IsMainland(
                mapX,
                mapZ);
        }
    }
}