using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Chunks
{
    public struct ChunkStreamingCenter :
        IComponentData
    {
        public int2 Coordinate;
    }
}