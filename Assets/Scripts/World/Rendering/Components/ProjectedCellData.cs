using Game.World.Blocks;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace Game.World.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    [InternalBufferCapacity(0)]
    public struct ProjectedCellData :
        IBufferElementData
    {
        /// <summary>
        /// Bits:
        /// 0-7   BlockId
        /// 8-15  FaceType
        /// 16-23 Durability
        /// 24-31 MaxDurability
        /// </summary>
        public uint BlockData;

        /// <summary>
        /// Bits:
        /// 0-15  X
        /// 16-31 Y
        /// </summary>
        public uint Position;

        /// <summary>
        /// Bits:
        /// 0-7   LightLevel
        /// 8-15  LightR
        /// 16-23 LightG
        /// 24-31 LightB
        /// </summary>
        public uint LightData;

        /// <summary>
        /// Reserved for future use.
        /// </summary>
        public uint Reserved;

        public ProjectedCellData(
            BlockData block,
            ProjectedFaceType faceType,
            byte maximumDurability,
            ushort x,
            ushort y)
        {
            BlockData =
                (uint)block.BlockId |
                ((uint)faceType << 8) |
                ((uint)block.Durability << 16) |
                ((uint)maximumDurability << 24);

            Position =
                (uint)x |
                ((uint)y << 16);

            LightData =
                (uint)byte.MaxValue |
                ((uint)byte.MaxValue << 8) |
                ((uint)byte.MaxValue << 16) |
                ((uint)byte.MaxValue << 24);

            Reserved = 0;
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
            BlockData =
                (uint)block.BlockId |
                ((uint)faceType << 8) |
                ((uint)block.Durability << 16) |
                ((uint)maximumDurability << 24);

            Position =
                (uint)x |
                ((uint)y << 16);

            LightData =
                (uint)lightLevel |
                ((uint)lightR << 8) |
                ((uint)lightG << 16) |
                ((uint)lightB << 24);

            Reserved = 0;
        }
    }
}