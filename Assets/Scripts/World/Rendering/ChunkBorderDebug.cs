using UnityEngine;

namespace Game.World.Rendering
{
    public static class ChunkBorderDebug
    {
        public static bool Enabled { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Enabled = false;
        public static void Toggle() => Enabled = !Enabled;
    }
}
