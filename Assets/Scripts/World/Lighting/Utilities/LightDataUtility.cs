using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class LightDataUtility
    {
        private const uint LocalLightMask = 0x00FFFFFFu;
        private const uint SkyLightMask = 0x0F000000u;
        private const uint SunVisibilityAMask = 0x10000000u;
        private const uint SunVisibilityBMask = 0x20000000u;

        public static uint Pack(
            float3 localLight,
            byte skyLevel,
            bool sunVisible)
        {
            uint packedSkyLevel =
                (uint)math.min((int)skyLevel, 15);

            uint sunVisibility = sunVisible
                ? SunVisibilityAMask | SunVisibilityBMask
                : 0u;

            return PackLocalLight(localLight) |
                   (packedSkyLevel << 24) |
                   sunVisibility;
        }

        public static float3 UnpackLocalLight(uint lightData)
        {
            return new float3(
                lightData & 0xFFu,
                (lightData >> 8) & 0xFFu,
                (lightData >> 16) & 0xFFu) / 255f;
        }

        public static byte UnpackSkyLevel(uint lightData)
        {
            return (byte)((lightData & SkyLightMask) >> 24);
        }

        public static float UnpackSkyVisibility(uint lightData)
        {
            return UnpackSkyLevel(lightData) / 15f;
        }

        public static float UnpackSunVisibility(uint lightData)
        {
            return UnpackSunVisibility(lightData, 0);
        }

        public static float UnpackSunVisibility(
            uint lightData,
            byte maskIndex)
        {
            uint mask = maskIndex == 0
                ? SunVisibilityAMask
                : SunVisibilityBMask;

            return (lightData & mask) != 0u
                ? 1f
                : 0f;
        }

        public static uint ReplaceSunVisibility(
            uint lightData,
            byte maskIndex,
            bool visible)
        {
            uint mask = maskIndex == 0
                ? SunVisibilityAMask
                : SunVisibilityBMask;

            return visible
                ? lightData | mask
                : lightData & ~mask;
        }

        public static uint ReplaceLocalLight(
            uint lightData,
            float3 localLight)
        {
            return
                (lightData & ~LocalLightMask) |
                PackLocalLight(localLight);
        }

        private static uint PackLocalLight(float3 localLight)
        {
            localLight = math.saturate(localLight);

            return ToByte(localLight.x) |
                   ((uint)ToByte(localLight.y) << 8) |
                   ((uint)ToByte(localLight.z) << 16);
        }

        private static byte ToByte(float value)
        {
            return (byte)math.round(
                math.saturate(value) *
                byte.MaxValue);
        }
    }
}
