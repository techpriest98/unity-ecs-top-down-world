using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ChunkRenderPositionUtility
    {
        public static float3 GetPosition(
            int2 chunkCoordinate,
            ViewDirection direction,
            float projectedCellWidth,
            float projectedCellHeight,
            float depthStep)
        {
            float chunkWidth =
                ChunkSettings.SizeX *
                projectedCellWidth;

            float chunkDepth =
                ChunkSettings.SizeZ *
                projectedCellHeight;

            return direction switch
            {
                ViewDirection.Front =>
                    GetFrontPosition(
                        chunkCoordinate,
                        chunkWidth,
                        chunkDepth,
                        depthStep),

                ViewDirection.Back =>
                    GetBackPosition(
                        chunkCoordinate,
                        chunkWidth,
                        chunkDepth,
                        depthStep),

                ViewDirection.Right =>
                    GetRightPosition(
                        chunkCoordinate,
                        chunkWidth,
                        chunkDepth,
                        depthStep),

                ViewDirection.Left =>
                    GetLeftPosition(
                        chunkCoordinate,
                        chunkWidth,
                        chunkDepth,
                        depthStep),

                _ =>
                    float3.zero
            };
        }

        private static float3 GetFrontPosition(
            int2 coordinate,
            float chunkWidth,
            float chunkDepth,
            float depthStep)
        {
            return new float3(
                coordinate.x * chunkWidth,
                -coordinate.y * chunkDepth,
                -coordinate.y * depthStep);
        }

        private static float3 GetBackPosition(
            int2 coordinate,
            float chunkWidth,
            float chunkDepth,
            float depthStep)
        {
            return new float3(
                -coordinate.x * chunkWidth,
                coordinate.y * chunkDepth,
                coordinate.y * depthStep);
        }

        private static float3 GetRightPosition(
            int2 coordinate,
            float chunkWidth,
            float chunkDepth,
            float depthStep)
        {
            return new float3(
                coordinate.y * chunkWidth,
                coordinate.x * chunkDepth,
                coordinate.x * depthStep);
        }

        private static float3 GetLeftPosition(
            int2 coordinate,
            float chunkWidth,
            float chunkDepth,
            float depthStep)
        {
            return new float3(
                -coordinate.y * chunkWidth,
                -coordinate.x * chunkDepth,
                -coordinate.x * depthStep);
        }
    }
}