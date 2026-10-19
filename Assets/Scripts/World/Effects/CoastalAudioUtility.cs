using Unity.Mathematics;

namespace Game.World.Effects
{
    public static class CoastalAudioUtility
    {
        public static float Gain(float distance, float full, float silent) =>
            1 - math.smoothstep(math.max(0, full), math.max(full + .01f, silent), distance);

        // BuildShoreMask in Front view: N=-Z, E=+X, S=+Z, W=-X.
        public static float Distance(float2 point, int2 cell, byte mask)
        {
            float2 q = point - (float2)cell;
            float result = float.PositiveInfinity;
            if ((mask & 1) != 0) result = math.min(result, math.length(q - new float2(math.clamp(q.x, 0, 1), 0)));
            if ((mask & 2) != 0) result = math.min(result, math.length(q - new float2(1, math.clamp(q.y, 0, 1))));
            if ((mask & 4) != 0) result = math.min(result, math.length(q - new float2(math.clamp(q.x, 0, 1), 1)));
            if ((mask & 8) != 0) result = math.min(result, math.length(q - new float2(0, math.clamp(q.y, 0, 1))));
            if ((mask & 16) != 0) result = math.min(result, math.distance(q, new float2(1, 0)));
            if ((mask & 32) != 0) result = math.min(result, math.distance(q, new float2(1, 1)));
            if ((mask & 64) != 0) result = math.min(result, math.distance(q, new float2(0, 1)));
            if ((mask & 128) != 0) result = math.min(result, math.distance(q, new float2(0, 0)));
            return result;
        }
    }
}
