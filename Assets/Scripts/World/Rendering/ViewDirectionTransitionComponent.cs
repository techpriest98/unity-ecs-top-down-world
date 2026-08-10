using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ViewDirectionTransitionComponent :
        IComponentData
    {
        public ViewDirection TargetDirection;
        public bool IsActive;
    }
}