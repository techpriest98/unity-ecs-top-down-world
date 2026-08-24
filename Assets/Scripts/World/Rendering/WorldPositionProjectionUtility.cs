using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class
        WorldPositionProjectionUtility
    {
        private const float
            ProjectedCellWidth = 1f;

        private const float
            ProjectedCellHeight = 0.5f;

        public static float2 Project(
            float3 worldPosition,
            ViewDirection direction)
        {
            int2 chunkCoordinate =
                GetChunkCoordinate(
                    worldPosition);

            float2 localPosition =
                GetLocalPosition(
                    worldPosition,
                    chunkCoordinate);

            GetViewPosition(
                localPosition,
                direction,
                out float viewU,
                out float viewV);

            float3 chunkRenderPosition =
                ChunkRenderPositionUtility
                    .GetPosition(
                        chunkCoordinate,
                        direction,
                        ProjectedCellWidth,
                        ProjectedCellHeight,
                        depthStep: 0f);

            float chunkWidth =
                ChunkSettings.SizeX *
                ProjectedCellWidth;

            float projectionHeight =
                (
                    ChunkSettings.SizeY *
                    2 +
                    ChunkSettings.SizeZ
                ) *
                ProjectedCellHeight;

            float chunkLeft =
                chunkRenderPosition.x -
                chunkWidth *
                0.5f;

            float chunkTop =
                chunkRenderPosition.y +
                projectionHeight;

            float heightFromTop =
                ChunkSettings.SizeY -
                1 -
                worldPosition.y;

            float projectionY =
                heightFromTop *
                2f +
                viewV;

            float projectedX =
                chunkLeft +
                viewU *
                ProjectedCellWidth;

            float projectedY =
                chunkTop -
                (
                    projectionY +
                    0.5f
                ) *
                ProjectedCellHeight;

            return new float2(
                projectedX,
                projectedY);
        }

        private static int2 GetChunkCoordinate(
            float3 worldPosition)
        {
            return new int2(
                (int)math.floor(
                    worldPosition.x /
                    ChunkSettings.SizeX),

                (int)math.floor(
                    worldPosition.z /
                    ChunkSettings.SizeZ));
        }

        private static float2 GetLocalPosition(
            float3 worldPosition,
            int2 chunkCoordinate)
        {
            return new float2(
                worldPosition.x -
                chunkCoordinate.x *
                ChunkSettings.SizeX,

                worldPosition.z -
                chunkCoordinate.y *
                ChunkSettings.SizeZ);
        }

        private static void GetViewPosition(
            float2 localPosition,
            ViewDirection direction,
            out float viewU,
            out float viewV)
        {
            switch (direction)
            {
                case ViewDirection.Front:
                    viewU =
                        localPosition.x;

                    viewV =
                        localPosition.y;

                    break;

                case ViewDirection.Back:
                    viewU =
                        ChunkSettings.SizeX -
                        localPosition.x;

                    viewV =
                        ChunkSettings.SizeZ -
                        localPosition.y;

                    break;

                case ViewDirection.Right:
                    viewU =
                        localPosition.y;

                    viewV =
                        ChunkSettings.SizeX -
                        localPosition.x;

                    break;

                case ViewDirection.Left:
                    viewU =
                        ChunkSettings.SizeZ -
                        localPosition.y;

                    viewV =
                        localPosition.x;

                    break;

                default:
                    viewU =
                        localPosition.x;

                    viewV =
                        localPosition.y;

                    break;
            }
        }
    }
}