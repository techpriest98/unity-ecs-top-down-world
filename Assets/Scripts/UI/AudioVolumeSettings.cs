using System;
using UnityEngine;

namespace Game.UI
{
    public static class AudioVolumeSettings
    {
        private const string MusicKey = "Audio.MusicVolume";
        private const string AmbienceKey = "Audio.AmbienceVolume";
        private static bool loaded;
        private static float music, ambience;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { loaded = false; Changed = null; }

        public static float Music { get { Load(); return music; } set { Load(); Set(ref music, value, MusicKey); } }
        public static float Ambience { get { Load(); return ambience; } set { Load(); Set(ref ambience, value, AmbienceKey); } }
        private static float Valid(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp01(value);
        private static void Load()
        {
            if (loaded) return;
            music = Valid(PlayerPrefs.GetFloat(MusicKey, 1));
            ambience = Valid(PlayerPrefs.GetFloat(AmbienceKey, 1));
            loaded = true;
        }
        private static void Set(ref float field, float value, string key)
        {
            value = Valid(value);
            if (field == value) return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }
        public static void Save() => PlayerPrefs.Save();
    }
}
