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
        public Game.World.Blocks.BlockId BlockId => ProjectedCellPacking.GetBlockId(BlockData);
        public ushort ProjectionX => ProjectedCellPacking.GetX(ProjectionPosition);
        public ushort ProjectionY => ProjectedCellPacking.GetY(ProjectionPosition);
        public ushort SourceAirIndex;
        public ProjectedFaceType FaceType;
        public bool IsValid;
    }
}
