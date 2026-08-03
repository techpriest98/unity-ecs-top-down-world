namespace Game.World.Rendering
{
    public readonly struct ViewCoordinate
    {
        public readonly int U; // горизонталь на екрані
        public readonly int V; // глибина від камери
        public readonly int H; // висота світу

        public ViewCoordinate(int u, int v, int h)
        {
            U = u;
            V = v;
            H = h;
        }
    }
}