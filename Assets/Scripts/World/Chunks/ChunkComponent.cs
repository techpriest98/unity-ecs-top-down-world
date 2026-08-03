using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    public struct ChunkComponent : IComponentData
    {
        public int2 Coordinate;
    }
}