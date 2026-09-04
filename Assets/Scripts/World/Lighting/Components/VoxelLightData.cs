using Unity.Entities;

namespace Game.World.Lighting
{
    [InternalBufferCapacity(0)]
    public struct VoxelLightData : IBufferElementData
    {
        public byte Sky;
        public byte R;
        public byte G;
        public byte B;

        public VoxelLightData(
            byte sky,
            byte r = 0,
            byte g = 0,
            byte b = 0)
        {
            Sky = sky;
            R = r;
            G = g;
            B = b;
        }
    }
}