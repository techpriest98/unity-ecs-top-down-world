using Unity.Entities;

namespace Game.World.Lighting
{
    public struct ChunkNeedsImmediateLighting :
        IComponentData,
        IEnableableComponent
    {
    }
}