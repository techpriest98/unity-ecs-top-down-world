using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public struct DynamicLightSourceState : IComponentData
    {
        public int3 Cell;
        public float3 Color;
        public float Radius;
        public float Intensity;
        public bool Initialized;
    }

    public struct DynamicLightBounds
    {
        public int3 Min, Max;
        public static DynamicLightBounds Around(int3 cell, float radius)
        {
            int extent = (int)math.ceil(math.clamp(radius, 0f, 8f));
            return new DynamicLightBounds { Min = cell - extent, Max = cell + extent };
        }
        public bool Intersects(DynamicLightBounds other) => math.all(Min <= other.Max) && math.all(Max >= other.Min);
    }
}
