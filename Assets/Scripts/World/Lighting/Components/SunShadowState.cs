using Unity.Entities;

namespace Game.World.Lighting
{
    public struct SunShadowState : IComponentData
    {
        public DirectionalLightData ActiveLight;
        public DirectionalLightData TargetLight;

        public byte ActiveMaskIndex;
        public bool IsTransitioning;
        public bool Initialized;
    }
}