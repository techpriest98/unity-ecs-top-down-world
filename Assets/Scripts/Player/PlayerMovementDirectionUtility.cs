using Game.World.Rendering;
using Unity.Mathematics;

namespace Game.Player
{
    public static class PlayerMovementDirectionUtility
    {
        public static float2 ScreenToWorld(
            float2 screenDirection,
            ViewDirection viewDirection)
        {
            return viewDirection switch
            {
                ViewDirection.Front =>
                    new float2(
                        screenDirection.x,
                        screenDirection.y),

                ViewDirection.Right =>
                    new float2(
                        -screenDirection.y,
                        screenDirection.x),

                ViewDirection.Back =>
                    new float2(
                        -screenDirection.x,
                        -screenDirection.y),

                ViewDirection.Left =>
                    new float2(
                        screenDirection.y,
                        -screenDirection.x),

                _ =>
                    screenDirection
            };
        }
    }
}