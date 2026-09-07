using Unity.Entities;

namespace Game.World.Chunks
{
    public struct ChunkStreamingRuntime : IComponentData
    {
        public int LoadRadius;
    }
}