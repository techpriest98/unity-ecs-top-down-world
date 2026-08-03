using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ViewDirectionUtility
    {
        public static int2 Forward(ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front => new int2(0, 1),
                ViewDirection.Right => new int2(-1, 0),
                ViewDirection.Back  => new int2(0, -1),
                ViewDirection.Left  => new int2(1, 0),

                _ => int2.zero
            };
        }

        public static int2 Right(ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front => new int2(1, 0),
                ViewDirection.Right => new int2(0, 1),
                ViewDirection.Back  => new int2(-1, 0),
                ViewDirection.Left  => new int2(0, -1),

                _ => int2.zero
            };
        }

        public static int2 Left(ViewDirection direction)
        {
            return -Right(direction);
        }

        public static int2 Back(ViewDirection direction)
        {
            return -Forward(direction);
        }

        public static ViewDirection RotateClockwise(
            ViewDirection direction)
        {
            return (ViewDirection)(((int)direction + 1) % 4);
        }

        public static ViewDirection RotateCounterClockwise(
            ViewDirection direction)
        {
            return (ViewDirection)(((int)direction + 3) % 4);
        }
    }
}