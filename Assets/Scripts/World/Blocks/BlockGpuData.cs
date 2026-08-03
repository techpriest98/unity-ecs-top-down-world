using System.Runtime.InteropServices;

namespace Game.World.Blocks
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BlockGpuData
    {
        public uint AtlasPosition;
        public uint Flags;

        public BlockGpuData(
            byte atlasColumn,
            byte atlasRow,
            uint flags = 0)
        {
            AtlasPosition =
                atlasColumn |
                ((uint)atlasRow << 8);

            Flags = flags;
        }
    }
}