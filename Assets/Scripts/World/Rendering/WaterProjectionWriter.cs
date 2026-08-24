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
            byte opticalDepth,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.Top,
                opticalDepth,
                x,
                y);
        }

        public bool TryAddSideUpper(
            BlockData block,
            byte opticalDepth,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideUpper,
                opticalDepth,
                x,
                y);
        }

        public bool TryAddSideLower(
            BlockData block,
            byte opticalDepth,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideLower,
                opticalDepth,
                x,
                y);
        }

        private bool TryAdd(
            BlockData block,
            ProjectedFaceType faceType,
            byte opticalDepth,
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

            cell.Reserved = opticalDepth;

            result.Add(new ProjectedWaterCellData(cell));

            return true;
        }
    }
}