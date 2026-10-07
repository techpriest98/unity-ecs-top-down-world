using Unity.Entities;

namespace Game.World.Rendering
{
    // Cells removed by near-chunk projection, before deeper faces fill the hole.
    public struct ProjectedClipCell : IBufferElementData
    {
        public ushort X;
        public ushort Y;
    }
}
