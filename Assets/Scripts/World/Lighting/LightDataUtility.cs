using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class LightDataUtility
    {
        public static uint Pack(float3 light)
        {
            light = math.saturate(light);

            float level = math.cmax(light);

            if (level <= 0f)
            {
                return 0;
            }

            float3 color = light / level;

            byte packedLevel = ToByte(level);
            byte packedR = ToByte(color.x);
            byte packedG = ToByte(color.y);
            byte packedB = ToByte(color.z);

            return packedLevel |
                   ((uint)packedR << 8) |
                   ((uint)packedG << 16) |
                   ((uint)packedB << 24);
        }

        public static float3 Unpack(uint lightData)
        {
            float level = (lightData & 0xFFu) / 255f;

            float3 color = new float3(
                (lightData >> 8) & 0xFFu,
                (lightData >> 16) & 0xFFu,
                (lightData >> 24) & 0xFFu) / 255f;

            return color * level;
        }

        private static byte ToByte(float value)
        {
            return (byte)math.round(math.saturate(value) * byte.MaxValue);
        }
    }
}