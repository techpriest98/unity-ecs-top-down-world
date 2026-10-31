using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Trees
{
    // Lives on the item-library entity so it survives chunk streaming/unloading.
    [InternalBufferCapacity(0)]
    public struct TreeResinRecord : IBufferElementData
    {
        public int2 Chunk;
        public int TreeId;
        public int Seed;
        public int GeneratorVersion;
        public float3 BasePosition;
        public double ReadyAt;
    }
}
