using Game.World.Generation.Biomes;
using Game.World.Generation;
using Unity.Entities;

namespace Game.World.Chunks
{
    [InternalBufferCapacity(0)]
    public struct ChunkColumnData : IBufferElementData
    {
        public WorldBiome Biome;
        public byte Zone;
        public int SurfaceHeight;
    }
}