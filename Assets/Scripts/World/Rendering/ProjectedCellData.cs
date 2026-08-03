using Unity.Entities;

namespace Game.World.Rendering
{
    [InternalBufferCapacity(0)]
    public struct ProjectedCellData : IBufferElementData
    {
        public ProjectedFaceType FaceType;
        public ushort BlockIndex;

        public ushort X;
        public ushort Y;

        public ProjectedCellData(
            ProjectedFaceType faceType,
            ushort blockIndex,
            ushort x,
            ushort y)
        {
            FaceType = faceType;
            BlockIndex = blockIndex;
            X = x;
            Y = y;
        }
    }
}