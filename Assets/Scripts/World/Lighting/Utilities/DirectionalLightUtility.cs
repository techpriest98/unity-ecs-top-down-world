using Unity.Mathematics;

namespace Game.World.Lighting
{
    public static class DirectionalLightUtility
    {
        private const float MaxSunElevation = 70f;

        private static readonly float3 NightAmbient = new(0.04f, 0.06f, 0.12f);
        private static readonly float3 DayAmbient = new(0.3f, 0.32f, 0.36f);
        private static readonly float3 WarmSunColor = new(1f, 0.55f, 0.3f);
        private static readonly float3 DaySunColor = new(1f, 0.95f, 0.85f);

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
            float elevationProgress = math.sin(dayProgress * math.PI);
            float elevationAngle = math.radians(MaxSunElevation) * elevationProgress;
            float azimuth = math.lerp(-math.PI * 0.5f, math.PI * 0.5f, dayProgress);

            float horizontalLength = math.cos(elevationAngle);

            float3 directionToLight = new float3(
                math.sin(azimuth) * horizontalLength,
                math.sin(elevationAngle),
                -math.cos(azimuth) * horizontalLength);

            float intensity = math.saturate(elevationProgress);
            float colorBlend = math.saturate(elevationProgress * 2f);

            return new DirectionalLightData
            {
                DirectionToLight = math.normalize(directionToLight),
                Color = math.lerp(WarmSunColor, DaySunColor, colorBlend),
                Intensity = intensity,
                AmbientColor = math.lerp(NightAmbient, DayAmbient, intensity)
            };
        }
    }
}