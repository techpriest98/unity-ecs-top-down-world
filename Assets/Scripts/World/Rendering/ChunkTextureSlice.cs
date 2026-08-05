using Unity.Entities;
using Unity.Rendering;

namespace Game.World.Rendering
{
    [MaterialProperty("_ChunkTextureSlice")]
    public struct ChunkTextureSlice : IComponentData
    {
        public float Value;
    }
}