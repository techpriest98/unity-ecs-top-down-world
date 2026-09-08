using UnityEngine;

namespace Game.World.Generation
{
    public static class WorldLaunchRequest
    {
        public static string WorldId { get; private set; }
        public static string WorldName { get; private set; }
        public static uint Seed { get; private set; }
        public static bool Pending { get; private set; }

        public static void Set(string worldName, uint seed)
        {
            Set(string.Empty, worldName, seed);
        }

        public static void Set(string worldId, string worldName, uint seed)
        {
            WorldId = worldId;
            WorldName = worldName;
            Seed = seed;
            Pending = true;
        }

        public static void MarkApplied() => Pending = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            WorldId = string.Empty;
            WorldName = string.Empty;
            Seed = 0;
            Pending = false;
        }
    }
}
