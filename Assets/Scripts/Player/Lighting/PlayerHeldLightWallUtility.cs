using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;

namespace Game.Player
{
    public static class PlayerHeldLightWallUtility
    {
        public const float WallMargin = .04f;

        public static float3 Clamp(float3 feet, float height, float3 desired, in ChunkBlockAccessor accessor)
        {
            if (!math.all(math.isfinite(desired))) return feet + new float3(0, math.max(.05f, height * .5f), 0);
            float3 start = feet + new float3(0, math.clamp(desired.y - feet.y, .05f, math.max(.05f, height - .05f)), 0);
            float3 ray = desired - start;
            float distance = math.length(ray);
            // Fail closed for malformed authored anchors; normal hand offsets are much shorter.
            if (distance > 8f) return start;
            if (distance < .00001f) return desired;
            float nearest = 2;
            int3 minimum = (int3)math.floor(math.min(start, desired));
            int3 maximum = (int3)math.floor(math.max(start, desired));
            for (int y = minimum.y; y <= maximum.y; y++)
            for (int z = minimum.z; z <= maximum.z; z++)
            for (int x = minimum.x; x <= maximum.x; x++)
            {
                if (y >= ChunkSettings.SizeY) continue;
                if (y < 0 || !accessor.TryGetRawBlock(int2.zero, x, y, z, out _)) return start;
                var contents = accessor.GetContentsOrAir(int2.zero, x, y, z);
                for (int half = 0; half < 2; half++)
                {
                    var id = half == 0 ? contents.Lower.Id : contents.Upper.Id;
                    if (!BlockUtility.IsSolid(id) || id == BlockId.OceanWater || id == BlockId.RiverWater) continue;
                    float3 boxMin = new float3(x, y + half * .5f, z);
                    if (EntryFraction(start, ray, boxMin, boxMin + new float3(1, .5f, 1), out float entry))
                        nearest = math.min(nearest, entry);
                }
            }
            if (nearest > 1) return desired;
            return start + ray * math.max(0, nearest - WallMargin / distance);
        }

        public static bool EntryFraction(float3 start, float3 ray, float3 minimum, float3 maximum, out float entry)
        {
            entry = 0;
            float exit = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                if (math.abs(ray[axis]) < .000001f)
                {
                    // Merely touching an edge is not penetrating the solid volume.
                    if (start[axis] <= minimum[axis] || start[axis] >= maximum[axis]) return false;
                    continue;
                }
                float a = (minimum[axis] - start[axis]) / ray[axis];
                float b = (maximum[axis] - start[axis]) / ray[axis];
                entry = math.max(entry, math.min(a, b));
                exit = math.min(exit, math.max(a, b));
                if (entry >= exit) return false;
            }
            return entry <= 1 && exit > .000001f;
        }
    }
}
