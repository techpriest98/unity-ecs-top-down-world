using Unity.Entities;

namespace Game.World.Lighting
{
    public struct ChunkNeedsSunShadowUpdate :
        IComponentData,
        IEnableableComponent
    {
    }
}