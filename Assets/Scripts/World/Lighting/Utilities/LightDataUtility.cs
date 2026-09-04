using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class LightDataUtility
    {
        public static uint Pack(float3 indirectLight, float sunVisibility)
        {
            indirectLight = math.saturate(indirectLight);

            return ToByte(indirectLight.x) |
                   ((uint)ToByte(indirectLight.y) << 8) |
                   ((uint)ToByte(indirectLight.z) << 16) |
                   ((uint)ToByte(sunVisibility) << 24);
        }

        public static float3 UnpackIndirect(uint lightData)
        {
            return new float3(
                lightData & 0xFFu,
                (lightData >> 8) & 0xFFu,
                (lightData >> 16) & 0xFFu) / 255f;
        }

        public static float UnpackSunVisibility(uint lightData)
        {
            return ((lightData >> 24) & 0xFFu) / 255f;
        }

        public static uint ReplaceIndirect(uint lightData, float3 indirectLight)
        {
            indirectLight = math.saturate(indirectLight);

            uint r = ToByte(indirectLight.x);
            uint g = ToByte(indirectLight.y);
            uint b = ToByte(indirectLight.z);

            return
                (lightData & 0xFF000000u) |
                r |
                (g << 8) |
                (b << 16);
        }

        private static byte ToByte(float value)
        {
            return (byte)math.round(math.saturate(value) * byte.MaxValue);
        }
    }
}
