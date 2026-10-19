using Game.World.Effects;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.UI
{
    // Intro uses gameplay's biome music / closest-shore ambience levels and global sliders.
    public sealed class AudioCategoryVolume : MonoBehaviour
    {
        private AudioSource source;
        private float baseVolume;
        private bool isMusic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= BindScene;
            SceneManager.sceneLoaded += BindScene;
        }
        private static void BindScene(Scene scene, LoadSceneMode mode)
        {
            var settings = Resources.Load<CoastalAudioSettings>("CoastalAudioSettings");
            if (settings == null) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var audio in root.GetComponentsInChildren<AudioSource>(true))
                {
                    if (audio.clip == null || audio.GetComponent<AudioCategoryVolume>() != null) continue;
                    if (audio.clip != settings.rockyShoreMusic && audio.clip != settings.seaAmbience) continue;
                    var binding = audio.gameObject.AddComponent<AudioCategoryVolume>();
                    binding.source = audio;
                    binding.isMusic = audio.clip == settings.rockyShoreMusic;
                    binding.baseVolume = scene.name == "Intro"
                        ? Mathf.Clamp01(binding.isMusic ? settings.musicVolume : settings.seaVolume)
                        : audio.volume;
                    binding.Apply();
                }
        }
        private void OnEnable() { AudioVolumeSettings.Changed += Apply; Apply(); }
        private void OnDisable() => AudioVolumeSettings.Changed -= Apply;
        private void Apply()
        {
            if (source != null) source.volume = baseVolume * (isMusic ? AudioVolumeSettings.Music : AudioVolumeSettings.Ambience);
        }
    }
}
