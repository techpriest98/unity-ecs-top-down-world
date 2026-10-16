using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct ProjectionWriter
    {
        private ProjectionOccupancy occupancy;
        private DynamicBuffer<ProjectedCellData> result;
        private DynamicBuffer<ProjectedClipCell> clippedCells;

        public ProjectionWriter(
            ProjectionOccupancy occupancy,
            DynamicBuffer<ProjectedCellData> result,
            DynamicBuffer<ProjectedClipCell> clippedCells = default)
        {
            this.occupancy = occupancy;
            this.result = result;
            this.clippedCells = clippedCells;
        }

        public void RecordClippedCell(ushort x, ushort y)
        {
            if (clippedCells.IsCreated)
            {
                clippedCells.Add(new ProjectedClipCell { X = x, Y = y });
            }
        }

        public bool TryAddTop(
            BlockData block,
            byte neighborMask,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, ProjectedFaceType.Top, sourceAirIndex, block.Shape == BlockShape.LowerHalf))
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
            cell.SetTopNeighborMask(neighborMask);

            result.Add(cell);

            return true;
        }

        public bool TryAddCutTop(
            BlockData block,
            ushort sourceCellIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(x, y, ProjectedFaceType.Top))
                return false;

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.Top,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceCellIndex;
            cell.SetCutTop();

            result.Add(cell);
            return true;
        }

        public bool TryAddCutSideUpper(
            BlockData block,
            ushort sourceCellIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(
                    x,
                    y,
                    ProjectedFaceType.SideUpper))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.SideUpper,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceCellIndex;
            cell.SetCutSide();

            result.Add(cell);
            return true;
        }

        public bool TryAddCutSideLower(
            BlockData block,
            ushort sourceCellIndex,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(
                    x,
                    y,
                    ProjectedFaceType.SideLower))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                ProjectedFaceType.SideLower,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceCellIndex;
            cell.SetCutSide();

            result.Add(cell);
            return true;
        }

        // Temporary overload for the current ChunkProjectionBuilder.
        public bool TryAddSideUpper(BlockData block, ushort sourceAirIndex, ushort x, ushort y)
        {
            return TryAddSideUpper(block, 0, BlockId.Air, sourceAirIndex, x, y);
        }

        public bool TryAddSideUpper(
            BlockData block,
            byte neighborMask,
            BlockId footBlockId,
            ushort sourceAirIndex,
            ushort x,
            ushort y,
            bool raisedFoot = false,
            bool upperHalf = false)
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
            cell.SetSideFaceData(neighborMask, footBlockId);
            if (raisedFoot) cell.BlockData |= ProjectedCellPacking.RaisedFootFlag;
            if (upperHalf) cell.BlockData |= ProjectedCellPacking.UpperHalfFlag;

            result.Add(cell);

            return true;
        }

        public bool TryAddSideLower(
            BlockData block,
            byte neighborMask,
            BlockId footBlockId,
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
            cell.SetSideFaceData(neighborMask, footBlockId);

            result.Add(cell);

            return true;
        }
    }
}
