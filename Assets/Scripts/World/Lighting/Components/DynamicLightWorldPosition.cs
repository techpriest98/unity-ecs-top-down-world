using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public struct DynamicLightWorldPosition : IComponentData
    {
        public float3 Value;
    }
}