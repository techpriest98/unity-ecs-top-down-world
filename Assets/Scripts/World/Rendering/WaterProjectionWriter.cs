using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct WaterProjectionWriter
    {
        private ProjectionOccupancy occupancy;
        private DynamicBuffer<ProjectedWaterCellData> result;

        public WaterProjectionWriter(
            ProjectionOccupancy occupancy,
            DynamicBuffer<ProjectedWaterCellData> result)
        {
            this.occupancy = occupancy;
            this.result = result;
        }

        public bool TryAddTop(
            BlockData block,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.Top,
                x,
                y);
        }

        public bool TryAddSideUpper(
            BlockData block,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideUpper,
                x,
                y);
        }

        public bool TryAddSideLower(
            BlockData block,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideLower,
                x,
                y);
        }

        private bool TryAdd(
            BlockData block,
            ProjectedFaceType faceType,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, faceType))
                return false;

            ProjectedCellData cell = new(
                block,
                faceType,
                byte.MaxValue,
                x,
                y);

            result.Add(new ProjectedWaterCellData(cell));

            return true;
        }
    }
}