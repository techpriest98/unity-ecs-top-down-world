using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    public struct ChunkStreamingRuntime : IComponentData
    {
        public int LoadRadius;
    }

    public static class CameraStreamingRadiusUtility
    {
        // Projection: one world block spans 1 horizontally and .5 in depth.
        // Ceil covers the player's position anywhere inside the centre chunk;
        // streaming adds its existing extra preload ring on top of this radius.
        public static int Calculate(float size, float aspect, float2 offset, float heightAllowance)
        {
            float width = math.max(.1f, size) * math.max(.1f, aspect) + math.abs(offset.x);
            float depth = (math.max(.1f, size) + math.abs(offset.y) + math.max(0f, heightAllowance)) * 2f;
            return math.max(1, (int)math.ceil(math.max(width / ChunkSettings.SizeX, depth / ChunkSettings.SizeZ)));
        }

        public static int Update(int current, int desired, float deltaTime, ref float shrinkDelay)
        {
            if (desired >= current)
            {
                shrinkDelay = 3f;
                return desired;
            }
            shrinkDelay -= math.max(0f, deltaTime);
            if (shrinkDelay > 0f) return current;
            shrinkDelay = 3f;
            return desired;
        }
    }
}
