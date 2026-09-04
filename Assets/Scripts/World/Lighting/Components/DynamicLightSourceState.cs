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
}