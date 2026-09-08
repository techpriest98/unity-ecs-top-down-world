using UnityEngine;

namespace Game.World.Generation
{
    public static class WorldLaunchRequest
    {
        public static string WorldName { get; private set; }
        public static uint Seed { get; private set; }
        public static bool Pending { get; private set; }

        public static void Set(string worldName, uint seed)
        {
            WorldName = worldName;
            Seed = seed;
            Pending = true;
        }

        public static void MarkApplied() => Pending = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            WorldName = string.Empty;
            Seed = 0;
            Pending = false;
        }
    }
}
