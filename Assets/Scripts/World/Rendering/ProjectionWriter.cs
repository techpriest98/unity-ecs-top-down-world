using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ProjectionWriter
    {
        private ProjectionOccupancy _occupancy;
        private DynamicBuffer<ProjectedCellData> _result;

        public ProjectionWriter(
            ProjectionOccupancy occupancy,
            DynamicBuffer<ProjectedCellData> result)
        {
            _occupancy = occupancy;
            _result = result;
        }

        public bool TryAddTop(
            BlockData block,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.Top))
                return false;

            _result.Add(new ProjectedCellData(
                block,
                ProjectedFaceType.Top,
                byte.MaxValue,
                x,
                y));

            return true;
        }

        public bool TryAddSideUpper(
            BlockData block,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.SideUpper))
                return false;

            _result.Add(new ProjectedCellData(
                block,
                ProjectedFaceType.SideUpper,
                byte.MaxValue,
                x,
                y));

            return true;
        }

        public bool TryAddSideLower(
            BlockData block,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.SideLower))
                return false;

            _result.Add(new ProjectedCellData(
                block,
                ProjectedFaceType.SideLower,
                byte.MaxValue,
                x,
                y));

            return true;
        }
    }
}