using UnityEngine;

namespace Game.World.Effects
{
    [CreateAssetMenu(menuName = "World/Coastal Audio Settings")]
    public sealed class CoastalAudioSettings : ScriptableObject
    {
        public AudioClip rockyShoreMusic;
        public AudioClip seaAmbience;
        [Range(0, 1)] public float musicVolume = .35f;
        [Range(0, 1)] public float seaVolume = .75f;
        [Min(0)] public float fullVolumeDistance = 3;
        [Range(4, 64)] public float silentDistance = 32;
        [Min(.1f)] public float fadeSeconds = 2;
    }
}
