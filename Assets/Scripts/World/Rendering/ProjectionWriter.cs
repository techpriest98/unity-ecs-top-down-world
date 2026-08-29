using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ProjectionWriter
    {
        private ProjectionOccupancy occupancy;
        private DynamicBuffer<ProjectedCellData> result;

        public ProjectionWriter(
            ProjectionOccupancy occupancy,
            DynamicBuffer<ProjectedCellData> result)
        {
            this.occupancy = occupancy;
            this.result = result;
        }

        public bool TryAddTop(
            BlockData block,
            byte neighborMask,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, ProjectedFaceType.Top))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.Top,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceAirIndex;
            cell.FaceData = neighborMask;

            result.Add(cell);

            return true;
        }

        public bool TryAddSideUpper(
            BlockData block,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, ProjectedFaceType.SideUpper))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.SideUpper,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceAirIndex;

            result.Add(cell);

            return true;
        }

        public bool TryAddSideLower(
            BlockData block,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, ProjectedFaceType.SideLower))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.SideLower,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceAirIndex;

            result.Add(cell);

            return true;
        }
    }
}