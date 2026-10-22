using Unity.Mathematics;

namespace Game.Player
{
    public static class PlayerSunShadowUtility
    {
        public static bool DirectionalSilhouetteUv(float3 receiver, float3 direction, float pixelScale, out float2 uv)
        {
            uv = float2.zero;
            if (math.abs(direction.z) < .000001f || pixelScale <= 0) return false;
            float distance = -receiver.z / direction.z;
            if (distance <= .00001f) return false;
            float3 hit = receiver + direction * distance;
            if (hit.y < 0 || hit.y > pixelScale * 103f) return false;
            uv = new float2((hit.x / pixelScale + 32) / 64, (hit.y / pixelScale + 25) / 128);
            return math.all(uv >= 0) && math.all(uv <= 1);
        }
        public static bool LocalSilhouetteUv(float3 receiver, float3 source, float pixelScale, out float2 uv)
        {
            uv = float2.zero;
            float depth = source.z - receiver.z;
            if (math.abs(depth) < .001f) return false;
            float t = -receiver.z / depth;
            if (t <= .001f || t >= .999f) return false;
            float3 hit = math.lerp(receiver, source, t);
            if (hit.y < 0 || hit.y > pixelScale * 103f) return false;
            uv = new float2((hit.x / pixelScale + 32) / 64, (hit.y / pixelScale + 25) / 128);
            return math.all(uv >= 0) && math.all(uv <= 1);
        }
        // The silhouette is a billboard, not a 3D body. Preserve a small artistic
        // thickness at edge-on sun angles instead of an unstable inverse projection.
        public static float2 Slopes(float3 spriteSun)
        {
            float y = math.max(.15f, spriteSun.y);
            float toward = spriteSun.z / y;
            if (math.abs(toward) < .15f) toward = toward < 0 ? -.15f : .15f;
            return new float2(math.clamp(spriteSun.x / y, -4f, 4f), math.clamp(toward, -4f, 4f));
        }

        public static float2 SilhouetteUv(float2 ground, float receiverDelta, float2 slopes, float pixelScale)
        {
            float height = receiverDelta - ground.y / slopes.y;
            float sourceX = ground.x + slopes.x * (height - receiverDelta);
            return new float2((sourceX / pixelScale + 32f) / 64f, (height / pixelScale + 25f) / 128f);
        }
    }
}
