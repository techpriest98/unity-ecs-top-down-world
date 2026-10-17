using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct WaterProjectionWriter
    {
        private const ushort OpticalDepthMask = 0xFF;
        private const ushort TopInsetFlag = 1 << 8;

        private ProjectionOccupancy opaqueOccupancy;
        private ProjectionOccupancy waterOccupancy;
        private DynamicBuffer<ProjectedWaterCellData> result;

        public WaterProjectionWriter(
            ProjectionOccupancy opaqueOccupancy,
            ProjectionOccupancy waterOccupancy,
            DynamicBuffer<ProjectedWaterCellData> result)
        {
            this.opaqueOccupancy = opaqueOccupancy;
            this.waterOccupancy = waterOccupancy;
            this.result = result;
        }

        public bool TryAddTop(
            BlockData block,
            byte opticalDepth,
            bool topInset,
            ushort sourceAirIndex,
            ushort x,
            ushort y,
            byte shoreMask = 0)
        {
            return TryAdd(
                block,
                ProjectedFaceType.Top,
                opticalDepth,
                topInset,
                sourceAirIndex,
                x,
                y, shoreMask: shoreMask);
        }

        public bool TryAddSideUpper(
            BlockData block,
            byte opticalDepth,
            ushort sourceAirIndex,
            ushort x,
            ushort y,
            bool upperHalf = false,
            int opaqueUnderlaySource = -1)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideUpper,
                opticalDepth,
                false,
                sourceAirIndex,
                x,
                y, upperHalf, opaqueUnderlaySource);
        }

        public bool TryAddSideLower(
            BlockData block,
            byte opticalDepth,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideLower,
                opticalDepth,
                false,
                sourceAirIndex,
                x,
                y);
        }

        private bool TryAdd(
            BlockData block,
            ProjectedFaceType faceType,
            byte opticalDepth,
            bool topInset,
            ushort sourceAirIndex,
            ushort x,
            ushort y,
            bool upperHalf = false,
            int opaqueUnderlaySource = -1,
            byte shoreMask = 0)
        {
            if (opaqueOccupancy.IsOccupied(x, y) &&
                (opaqueUnderlaySource < 0 || !opaqueOccupancy.IsLowerTop(x, y, (ushort)opaqueUnderlaySource)))
            {
                return false;
            }

            if (!waterOccupancy.TryOccupy(x, y, faceType))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                faceType,
                byte.MaxValue,
                x,
                y);

            cell.SourceAirIndex = sourceAirIndex;
            if (upperHalf) cell.BlockData |= ProjectedCellPacking.UpperHalfFlag;
            // Seven shore bits fit FaceData; the fourth outside corner uses
            // the spare BlockData bit, keeping the cell/GPU buffer stride intact.
            cell.FaceData = (ushort)(PackFaceData(opticalDepth, topInset) | ((shoreMask & 127) << 9));
            cell.BlockData |= (uint)(shoreMask >> 7) << 15;

            result.Add(new ProjectedWaterCellData(cell));

            return true;
        }

        private static ushort PackFaceData(
            byte opticalDepth,
            bool topInset)
        {
            ushort faceData = (ushort)(opticalDepth & OpticalDepthMask);

            if (topInset)
            {
                faceData |= TopInsetFlag;
            }

            return faceData;
        }
    }
}
