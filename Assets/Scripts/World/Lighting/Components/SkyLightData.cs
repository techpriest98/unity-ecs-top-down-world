using Unity.Entities;

namespace Game.World.Lighting
{
    [InternalBufferCapacity(0)]
    public struct SkyLightData : IBufferElementData
    {
        public byte Value;

        public SkyLightData(byte value)
        {
            Value = value;
        }
    }
}