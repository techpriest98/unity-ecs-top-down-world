using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ViewDirectionComponent : IComponentData
    {
        public ViewDirection Value;
    }
}