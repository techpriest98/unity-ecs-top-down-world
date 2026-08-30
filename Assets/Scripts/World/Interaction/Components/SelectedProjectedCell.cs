using Game.World.Rendering;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Interaction
{
    public struct SelectedProjectedCell : IComponentData
    {
        public int2 ChunkCoordinate;
        public uint ProjectionPosition;
        public uint BlockData;
        public ushort SourceAirIndex;
        public ProjectedFaceType FaceType;
        public bool IsValid;
    }
}