using Unity.Entities;
using Unity.Mathematics;

namespace Game.World.Time
{
    // Session-only world clock control. Does not change movement/physics speed.
    public struct WorldTimeDebugSpeed : IComponentData { public float Value; }

    public static class WorldTimeDebugUtility
    {
        public const float SecondsPerHour = 90f;
        public static float ChangeSpeed(float speed, bool increase) =>
            math.clamp((math.isfinite(speed) && speed > 0 ? speed : 1f) * (increase ? 2f : .5f), .25f, 64f);

        public static void Advance(ref WorldTime time, float deltaTime, float speed)
        {
            time.DeltaSeconds = 0f;
            if (!math.isfinite(deltaTime) || deltaTime <= 0) return;
            speed = math.isfinite(speed) && speed > 0 ? math.clamp(speed, .25f, 64f) : 1f;
            time.DeltaSeconds = deltaTime * speed;
            // All world aging shares this clock, including unloaded chunks.
            time.SimulationSeconds += time.DeltaSeconds;
            time.UpdateTimer += time.DeltaSeconds;
            int hours = (int)math.floor(time.UpdateTimer / SecondsPerHour);
            time.UpdateTimer -= hours * SecondsPerHour;
            time.Hour = (time.Hour + hours) % 24;
        }

        public static int Minute(in WorldTime time) =>
            math.clamp((int)math.floor(time.UpdateTimer * 60f / SecondsPerHour), 0, 59);

        public static void NudgeWind(ref WorldWind wind, float amount)
        {
            if (!wind.Initialized || !math.isfinite(amount)) return;
            // Keep target, timer and RNG intact: normal weather immediately
            // continues its existing exponential relaxation to the target.
            wind.Strength = math.saturate(wind.Strength + amount);
        }
    }
}
