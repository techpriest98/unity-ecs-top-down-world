using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Game.World.Rendering
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ProjectedCellRenderData
    {
        public uint BlockData;
        public uint Position;
        public uint LightData;
        public uint FaceData;
        public Game.World.Blocks.BlockId BlockId => ProjectedCellPacking.GetBlockId(BlockData);
        public ProjectedFaceType FaceType => ProjectedCellPacking.GetFaceType(BlockData);
        public ushort ProjectionX => ProjectedCellPacking.GetX(Position);
        public ushort ProjectionY => ProjectedCellPacking.GetY(Position);

        public float3 ChunkPosition;
        // BL/BR/TL/TR packed directions, reuses the old 4-byte padding.
        public uint LocalLightDirection;

        public uint LocalLightBottomLeft;
        public uint LocalLightBottomRight;
        public uint LocalLightTopLeft;
        public uint LocalLightTopRight;
    }
}
