using UnityEngine;

namespace Game.UI
{
    public static class GraphicsPreferences
    {
        public const int MinimumChunkRadius = 1;
        public const int MaximumChunkRadius = 4;
        public const int MinimumLightingRadius = 8;
        public const int MaximumLightingRadius = 16;
        private const string LightingRadiusKey = "Graphics.DetailedLightingRadius";
        private static bool lightingLoaded;
        private static int lightingRadius;
        private const string ChunkRadiusKey = "Graphics.ChunksLoadingRadius";
        private static bool loaded;
        private static int chunkRadius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { loaded = false; lightingLoaded = false; }

        public static int DetailedLightingRadius
        {
            get
            {
                if (!lightingLoaded)
                {
                    lightingRadius = Mathf.Clamp(PlayerPrefs.GetInt(LightingRadiusKey, 8), MinimumLightingRadius, MaximumLightingRadius);
                    lightingLoaded = true;
                }
                return lightingRadius;
            }
            set
            {
                int next = Mathf.Clamp(value, MinimumLightingRadius, MaximumLightingRadius);
                if (DetailedLightingRadius == next) return;
                lightingRadius = next;
                PlayerPrefs.SetInt(LightingRadiusKey, next);
            }
        }

        public static int ChunkLoadingRadius
        {
            get
            {
                if (!loaded)
                {
                    chunkRadius = Mathf.Clamp(PlayerPrefs.GetInt(ChunkRadiusKey, 1), MinimumChunkRadius, MaximumChunkRadius);
                    loaded = true;
                }
                return chunkRadius;
            }
            set
            {
                int next = Mathf.Clamp(value, MinimumChunkRadius, MaximumChunkRadius);
                if (ChunkLoadingRadius == next) return;
                chunkRadius = next;
                PlayerPrefs.SetInt(ChunkRadiusKey, next);
            }
        }
        public static void Save() => PlayerPrefs.Save();
    }
}
