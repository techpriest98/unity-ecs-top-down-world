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
        public uint Reserved;

        public float3 ChunkPosition;
        public float Padding;
    }
}