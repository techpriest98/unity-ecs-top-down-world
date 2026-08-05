using Unity.Entities;

namespace Game.World.Chunks
{
    public struct ChunkStreamingSettings :
        IComponentData
    {
        public int LoadRadius;
    }
}