using Game.World.Rendering;
using Unity.Mathematics;

namespace Game.Player
{
    public enum PlayerSpriteDirection : byte
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    public static class PlayerSpriteDirectionUtility
    {
        public static PlayerSpriteDirection WorldToScreen(
            PlayerFacingDirection facing,
            ViewDirection viewDirection)
        {
            float2 worldDirection =
                GetWorldDirection(facing);

            float2 screenDirection =
                viewDirection switch
            {
               ViewDirection.Front =>
                    new float2(
                        worldDirection.x,
                        worldDirection.y),

                ViewDirection.Right =>
                    new float2(
                        worldDirection.y,
                        -worldDirection.x),

                ViewDirection.Back =>
                    new float2(
                        -worldDirection.x,
                        -worldDirection.y),

                ViewDirection.Left =>
                    new float2(
                        -worldDirection.y,
                        worldDirection.x),

                _ => worldDirection
            };

            return GetSpriteDirection(screenDirection);
        }

        private static float2 GetWorldDirection(PlayerFacingDirection facing)
        {
            return facing switch
            {
                PlayerFacingDirection.PositiveX => new float2(1f, 0f),
                PlayerFacingDirection.NegativeX => new float2(-1f, 0f),
                PlayerFacingDirection.PositiveZ => new float2(0f, 1f),
                PlayerFacingDirection.NegativeZ => new float2(0f, -1f),

                _ => new float2(0f, -1f)
            };
        }

        private static PlayerSpriteDirection GetSpriteDirection(
            float2 screenDirection)
        {
            if (math.abs(screenDirection.x) > math.abs(screenDirection.y))
            {
                return screenDirection.x > 0f
                    ? PlayerSpriteDirection.Right
                    : PlayerSpriteDirection.Left;
            }

            return screenDirection.y > 0f
                ? PlayerSpriteDirection.Down
                : PlayerSpriteDirection.Up;
        }
    }
}