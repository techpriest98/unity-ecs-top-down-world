using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Lighting
{
    public struct DirectionalLightData : IComponentData
    {
        public float3 DirectionToLight;
        public float3 Color;
        public float Intensity;
        public float3 AmbientColor;
    }
}