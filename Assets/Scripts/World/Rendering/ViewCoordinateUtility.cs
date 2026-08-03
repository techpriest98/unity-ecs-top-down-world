using Unity.Mathematics;

namespace Game.World.Rendering
{
    public static class ViewCoordinateUtility
    {
        public static ViewBounds GetViewBounds(
            int2 chunkSizeXZ,
            int chunkHeight,
            ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front or ViewDirection.Back =>
                    new ViewBounds(
                        chunkSizeXZ.x,
                        chunkSizeXZ.y,
                        chunkHeight),

                ViewDirection.Right or ViewDirection.Left =>
                    new ViewBounds(
                        chunkSizeXZ.y,
                        chunkSizeXZ.x,
                        chunkHeight),

                _ => default
            };
        }

        public static ViewCoordinate WorldToView(
            int3 world,
            int2 chunkSizeXZ,
            ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front =>
                    new ViewCoordinate(
                        world.x,
                        world.z,
                        world.y),

                ViewDirection.Back =>
                    new ViewCoordinate(
                        chunkSizeXZ.x - 1 - world.x,
                        chunkSizeXZ.y - 1 - world.z,
                        world.y),

                ViewDirection.Right =>
                    new ViewCoordinate(
                        world.z,
                        chunkSizeXZ.x - 1 - world.x,
                        world.y),

                ViewDirection.Left =>
                    new ViewCoordinate(
                        chunkSizeXZ.y - 1 - world.z,
                        world.x,
                        world.y),

                _ => default
            };
        }

        public static int3 ViewToWorld(
            ViewCoordinate view,
            int2 chunkSizeXZ,
            ViewDirection direction)
        {
            return direction switch
            {
                ViewDirection.Front =>
                    new int3(
                        view.U,
                        view.H,
                        view.V),

                ViewDirection.Back =>
                    new int3(
                        chunkSizeXZ.x - 1 - view.U,
                        view.H,
                        chunkSizeXZ.y - 1 - view.V),

                ViewDirection.Right =>
                    new int3(
                        chunkSizeXZ.x - 1 - view.V,
                        view.H,
                        view.U),

                ViewDirection.Left =>
                    new int3(
                        view.V,
                        view.H,
                        chunkSizeXZ.y - 1 - view.U),

                _ => int3.zero
            };
        }
    }
}