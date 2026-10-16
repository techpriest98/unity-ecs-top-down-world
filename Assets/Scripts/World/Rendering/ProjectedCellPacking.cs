using Game.World.Blocks;

namespace Game.World.Rendering
{
    // Storage contract shared with Shaders/ProjectedCellPacking.hlsl.
    public static class ProjectedCellPacking
    {
        public static BlockId GetBlockId(uint data) => (BlockId)(data & 0xFFu);
        public const uint LowerHalfFlag = 1u << 12;
        public const uint RaisedFootFlag = 1u << 13;
        public const uint UpperHalfFlag = 1u << 14;
        public static bool IsHalfFace(uint data) => (data & (LowerHalfFlag | UpperHalfFlag)) != 0;
        public static ProjectedFaceType GetFaceType(uint data) => (ProjectedFaceType)((data >> 8) & 0x0Fu);
        public static bool IsLowerHalf(uint data) => (data & LowerHalfFlag) != 0;
        public static byte GetDurability(uint data) => (byte)(data >> 16);
        public static byte GetMaxDurability(uint data) => (byte)(data >> 24);
        public static ushort GetX(uint position) => (ushort)position;
        public static ushort GetY(uint position) => (ushort)(position >> 16);
        public static uint PackPosition(ushort x, ushort y) => (uint)x | ((uint)y << 16);
        public static uint PackBlock(BlockData block, ProjectedFaceType face, byte maximum) =>
            (uint)block.BlockId | ((uint)face << 8) | (block.Shape == BlockShape.LowerHalf ? LowerHalfFlag : 0u) |
            ((uint)block.Durability << 16) | ((uint)maximum << 24);
        public static byte GetNeighborMask(ushort faceData) => (byte)faceData;
        public static BlockId GetFootBlockId(ushort faceData) => (BlockId)(faceData >> 8);
        public static byte GetOpticalDepth(ushort faceData) => (byte)faceData;
        public static bool HasWaterTopInset(ushort faceData) => (faceData & (1 << 8)) != 0;
    }
}
