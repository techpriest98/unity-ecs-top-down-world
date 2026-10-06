using Unity.Entities;

namespace Game.World.Rendering
{
    [InternalBufferCapacity(0)]
    public struct LocalProjectedFrontCell : IBufferElementData
    {
        // Reuse face data and world-space source positions.
        public LocalProjectedCell Value;
    }
}