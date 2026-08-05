using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ChunkTextureSliceCleanup :
        ICleanupComponentData
    {
        public int Value;
    }
}