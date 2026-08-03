using Unity.Mathematics;

namespace Game.World.Rendering
{
    public readonly struct ViewBounds
    {
        public readonly int Width;  // U
        public readonly int Depth;  // V
        public readonly int Height; // Z

        public ViewBounds(int width, int depth, int height)
        {
            Width = width;
            Depth = depth;
            Height = height;
        }

        public int2 SizeUV => new int2(Width, Depth);
    }
}