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

        public bool HasSameGeometry(uint block, uint position, uint face) =>
            BlockData == block && Position == position && FaceData == face;

        public bool UpdateLighting(uint light, uint direction, uint bottomLeft, uint bottomRight, uint topLeft, uint topRight)
        {
            bool changed = LightData != light || LocalLightDirection != direction || LocalLightBottomLeft != bottomLeft ||
                LocalLightBottomRight != bottomRight || LocalLightTopLeft != topLeft || LocalLightTopRight != topRight;
            LightData = light; LocalLightDirection = direction;
            LocalLightBottomLeft = bottomLeft; LocalLightBottomRight = bottomRight;
            LocalLightTopLeft = topLeft; LocalLightTopRight = topRight;
            return changed;
        }

        public static bool IsValidRange(int start, int length, int count) =>
            start >= 0 && length >= 0 && start <= count && length <= count - start;
    }
}
