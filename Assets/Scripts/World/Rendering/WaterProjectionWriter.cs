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
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.Top,
                opticalDepth,
                topInset,
                sourceAirIndex,
                x,
                y);
        }

        public bool TryAddSideUpper(
            BlockData block,
            byte opticalDepth,
            ushort sourceAirIndex,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.SideUpper,
                opticalDepth,
                false,
                sourceAirIndex,
                x,
                y);
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
            ushort y)
        {
            if (opaqueOccupancy.IsOccupied(x, y))
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
            cell.FaceData = PackFaceData(opticalDepth, topInset);

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