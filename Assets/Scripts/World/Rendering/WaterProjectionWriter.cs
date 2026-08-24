using Game.World.Blocks;
using Unity.Entities;

namespace Game.World.Rendering
{
    public struct WaterProjectionWriter
    {
        private const uint OpticalDepthMask =
            0xFF;

        private const uint TopInsetFlag =
            1u << 8;

        private ProjectionOccupancy occupancy;

        private DynamicBuffer<ProjectedWaterCellData>
            result;

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
            bool topInset,
            ushort x,
            ushort y)
        {
            return TryAdd(
                block,
                ProjectedFaceType.Top,
                opticalDepth,
                topInset,
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
                false,
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
                false,
                x,
                y);
        }

        private bool TryAdd(
            BlockData block,
            ProjectedFaceType faceType,
            byte opticalDepth,
            bool topInset,
            ushort x,
            ushort y)
        {
            if (!occupancy.TryOccupy(
                    x,
                    y,
                    faceType))
            {
                return false;
            }

            ProjectedCellData cell = new(
                block,
                faceType,
                byte.MaxValue,
                x,
                y);

            cell.Reserved =
                PackReserved(
                    opticalDepth,
                    topInset);

            result.Add(
                new ProjectedWaterCellData(
                    cell));

            return true;
        }

        private static uint PackReserved(
            byte opticalDepth,
            bool topInset)
        {
            uint packed =
                (uint)opticalDepth &
                OpticalDepthMask;

            if (topInset)
            {
                packed |=
                    TopInsetFlag;
            }

            return packed;
        }
    }
}