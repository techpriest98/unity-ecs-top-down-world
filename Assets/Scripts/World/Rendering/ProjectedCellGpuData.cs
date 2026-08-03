using Game.World.Blocks;
using System.Runtime.InteropServices;

namespace Game.World.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProjectedCellGpuData
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

        public ProjectedCellGpuData(
            BlockId blockId,
            ProjectedFaceType faceType,
            byte durability,
            ushort x,
            ushort y)
        {
            BlockData =
                (uint)blockId |
                ((uint)faceType << 8) |
                ((uint)durability << 16) |
                ((uint)byte.MaxValue << 24);

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
    }
}