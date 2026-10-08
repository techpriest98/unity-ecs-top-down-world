using Game.World.Blocks;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace Game.World.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    [InternalBufferCapacity(0)]
    public struct ProjectedCellData : IBufferElementData
    {
        /// <summary>
        /// Bits:
        /// 0-7   BlockId
        /// 8-15  FaceType
        /// 16-23 Durability
        /// 24-31 MaxDurability
        /// </summary>
        public uint BlockData;

        public BlockId BlockId => ProjectedCellPacking.GetBlockId(BlockData);
        public ProjectedFaceType FaceType => ProjectedCellPacking.GetFaceType(BlockData);
        public byte Durability => ProjectedCellPacking.GetDurability(BlockData);
        public byte MaxDurability => ProjectedCellPacking.GetMaxDurability(BlockData);

        /// <summary>
        /// Bits:
        /// 0-15  X
        /// 16-31 Y
        /// </summary>
        public uint Position;
        public ushort ProjectionX => ProjectedCellPacking.GetX(Position);
        public ushort ProjectionY => ProjectedCellPacking.GetY(Position);

        /// <summary>
        /// Packed cell lighting, written by ChunkLightingSystem through LightDataUtility.
        /// Contains local RGB, sky visibility and sun visibility masks.
        /// The legacy constructor below still accepts the old lighting layout.
        /// </summary>
        public uint LightData;

        /// <summary>
        /// Index of the air cell from which this projected face was generated.
        /// </summary>
        public ushort SourceAirIndex;

        /// <summary>
        /// Opaque Top:
        /// bits 0-7 NeighborMask (unchanged).
        ///
        /// Opaque SideUpper / SideLower:
        /// bits 0-7 NeighborMask:
        ///   bit 0 North = above solid,
        ///   bit 1 East = right solid,
        ///   bit 3 West = left solid.
        /// Other neighbor bits are currently unused for Side.
        /// bits 8-15 FootBlockId (Air means no bottom overlay).
        /// Directions are relative to the current view.
        /// Both Side halves receive the same data.
        ///
        /// Water:
        /// bits 0-7 OpticalDepth,
        /// bit 8 TopInset.
        /// </summary>
        public ushort FaceData;
        // Interpret according to face type and block category.
        public byte NeighborMask => ProjectedCellPacking.GetNeighborMask(FaceData);
        public BlockId FootBlockId => ProjectedCellPacking.GetFootBlockId(FaceData);
        public byte OpticalDepth => ProjectedCellPacking.GetOpticalDepth(FaceData);
        public bool WaterTopInset => ProjectedCellPacking.HasWaterTopInset(FaceData);
        public bool IsCutTop => FaceType == ProjectedFaceType.Top && (FaceData & CutTopFlag) != 0;
        public bool IsCutSide => FaceType != ProjectedFaceType.Top && (FaceData & CutSideFlag) != 0;

        /// <summary>
        /// Local light at the four corners of this projection quad, relative to screen.
        /// Each value packs R in bits 0-7, G in 8-15, B in 16-23 (0-255).
        /// Bits 24-31 are reserved. These values contain no sky or sun lighting.
        /// For SideUpper / SideLower, corners belong to each half separately;
        /// values along their shared edge must match.
        /// </summary>
        public uint LocalLightBottomLeft;
        public uint LocalLightBottomRight;
        public uint LocalLightTopLeft;
        public uint LocalLightTopRight;

        public const ushort CutTopFlag = 1 << 15;
        public const ushort CutSideFlag = 1 << 2;


        public void SetTopNeighborMask(byte neighborMask)
        {
            FaceData = neighborMask;
        }

        // Use only for opaque Side faces. The caller resolves view-relative
        // neighbors and the solid block below the air cell in front of the wall.
        public void SetSideFaceData(byte neighborMask, BlockId footBlockId)
        {
            FaceData = (ushort)(neighborMask | ((uint)footBlockId << 8));
        }

        public void SetCutTop()
        {
            FaceData = CutTopFlag;
        }

        public void SetCutSide()
        {
            FaceData = CutSideFlag;
        }

        public ProjectedCellData(
            BlockData block,
            ProjectedFaceType faceType,
            byte maximumDurability,
            ushort x,
            ushort y)
        {
            BlockData = ProjectedCellPacking.PackBlock(block, faceType, maximumDurability);

            Position = ProjectedCellPacking.PackPosition(x, y);

            LightData =
                byte.MaxValue |
                ((uint)byte.MaxValue << 8) |
                ((uint)byte.MaxValue << 16) |
                ((uint)byte.MaxValue << 24);

            SourceAirIndex = 0;
            FaceData = 0;
            LocalLightBottomLeft = 0;
            LocalLightBottomRight = 0;
            LocalLightTopLeft = 0;
            LocalLightTopRight = 0;
        }

        public ProjectedCellData(
            BlockData block,
            ProjectedFaceType faceType,
            byte maximumDurability,
            ushort x,
            ushort y,
            byte lightLevel,
            byte lightR,
            byte lightG,
            byte lightB)
        {
            BlockData = ProjectedCellPacking.PackBlock(block, faceType, maximumDurability);

            Position = ProjectedCellPacking.PackPosition(x, y);

            LightData =
                lightLevel |
                ((uint)lightR << 8) |
                ((uint)lightG << 16) |
                ((uint)lightB << 24);

            SourceAirIndex = 0;
            FaceData = 0;
            LocalLightBottomLeft = 0;
            LocalLightBottomRight = 0;
            LocalLightTopLeft = 0;
            LocalLightTopRight = 0;
        }
    }
}
