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
            ushort blockIndex,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.Top))
                return false;

            _result.Add(new ProjectedCellData(
                ProjectedFaceType.Top,
                blockIndex,
                x,
                y));

            return true;
        }

        public bool TryAddSideUpper(
            ushort blockIndex,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.SideUpper))
                return false;

            _result.Add(new ProjectedCellData(
                ProjectedFaceType.SideUpper,
                blockIndex,
                x,
                y));

            return true;
        }

        public bool TryAddSideLower(
            ushort blockIndex,
            ushort x,
            ushort y)
        {
            if (!_occupancy.TryOccupy(x, y, ProjectedFaceType.SideLower))
                return false;

            _result.Add(new ProjectedCellData(
                ProjectedFaceType.SideLower,
                blockIndex,
                x,
                y));

            return true;
        }
    }
}