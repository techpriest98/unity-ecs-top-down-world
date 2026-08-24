using Unity.Entities;

namespace Game.World.Rendering
{
    [InternalBufferCapacity(0)]
    public struct ProjectedWaterCellData :
        IBufferElementData
    {
        public ProjectedCellData Value;

        public ProjectedWaterCellData(
            in ProjectedCellData value)
        {
            Value = value;
        }
    }
}