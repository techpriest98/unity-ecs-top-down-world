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

        // GPU-only upper bits carry source Y/X/Z; CPU face flags stay ushort.
        public static uint PackFaceData(uint face, ushort source) => (face & 0xffffu) | ((uint)source << 16);
        // Terrain shaders do not use maximum durability. Reuse that GPU-only
        // byte for the Top transition, preserving CPU durability and 48B stride.
        public static uint PackBlockData(uint block, Game.World.Blocks.BlockId frontMaterial = 0) =>
            (block & 0x00ffffffu) | ((uint)frontMaterial << 24);
        public bool HasSameGeometry(uint block, uint position, uint face, ushort source = 0,
            Game.World.Blocks.BlockId frontMaterial = 0) =>
            BlockData == PackBlockData(block, frontMaterial) && Position == position && FaceData == PackFaceData(face, source);

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
