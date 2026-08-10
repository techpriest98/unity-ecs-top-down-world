using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class WorldPositionProjectionUtility
    {
        private const float ProjectedCellWidth =
            1f;

        private const float ProjectedCellHeight =
            0.5f;

        public static float2 Project(
            float3 worldPosition,
            ViewDirection direction)
        {
            float projectedX;
            float depthOffset;

            switch (direction)
            {
                case ViewDirection.Front:
                    projectedX =
                        worldPosition.x;

                    depthOffset =
                        -worldPosition.z;

                    break;

                case ViewDirection.Right:
                    projectedX =
                        worldPosition.z;

                    depthOffset =
                        worldPosition.x;

                    break;

                case ViewDirection.Back:
                    projectedX =
                        -worldPosition.x;

                    depthOffset =
                        worldPosition.z;

                    break;

                case ViewDirection.Left:
                    projectedX =
                        -worldPosition.z;

                    depthOffset =
                        -worldPosition.x;

                    break;

                default:
                    projectedX =
                        worldPosition.x;

                    depthOffset =
                        -worldPosition.z;

                    break;
            }

            float projectedY =
                worldPosition.y +
                depthOffset *
                ProjectedCellHeight;

            return new float2(
                projectedX *
                ProjectedCellWidth,
                projectedY);
        }
    }
}