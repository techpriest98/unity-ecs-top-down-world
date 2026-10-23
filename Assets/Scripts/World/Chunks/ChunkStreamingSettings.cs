using Unity.Entities;

namespace Game.World.Chunks
{
    public struct ChunkStreamingSettings :
        IComponentData
    {
        // Total radius, including the outer preparation ring (1 => 3x3).
        public int LoadRadius;
    }
}
