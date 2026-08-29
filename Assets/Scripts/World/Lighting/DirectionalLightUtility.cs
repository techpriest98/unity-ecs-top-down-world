using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class DirectionalLightUtility
    {
        private static readonly float3 NightAmbient = new float3(0.04f, 0.06f, 0.12f);
        private static readonly float3 DayAmbient = new float3(0.3f, 0.32f, 0.36f);
        private static readonly float3 WarmSunColor = new float3(1f, 0.55f, 0.3f);
        private static readonly float3 DaySunColor = new float3(1f, 0.95f, 0.85f);

        public static DirectionalLightData Sample(int hour)
        {
            hour = ((hour % 24) + 24) % 24;

            if (hour < 6 || hour >= 18)
            {
                return new DirectionalLightData
                {
                    DirectionToLight = new float3(0f, 1f, 0f),
                    Color = float3.zero,
                    Intensity = 0f,
                    AmbientColor = NightAmbient
                };
            }

            float dayProgress = (hour - 6) / 12f;
            float sunAngle = dayProgress * math.PI;
            float elevation = math.sin(sunAngle);
            float horizontalX = math.cos(sunAngle);

            float3 directionToLight = math.normalize(new float3(horizontalX, math.max(elevation, 0.05f), -0.35f));
            float intensity = math.saturate(elevation);
            float colorBlend = math.saturate(elevation * 2f);

            return new DirectionalLightData
            {
                DirectionToLight = directionToLight,
                Color = math.lerp(WarmSunColor, DaySunColor, colorBlend),
                Intensity = intensity,
                AmbientColor = math.lerp(NightAmbient, DayAmbient, intensity)
            };
        }
    }
}