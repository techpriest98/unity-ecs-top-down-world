using Unity.Entities;

namespace Game.World.Lighting
{
    public struct ChunkNeedsLocalLightUpdate :
        IComponentData,
        IEnableableComponent
    {
    }
}